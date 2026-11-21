//JJQR - AI (Nov 21, 2026) - doctor self-edit write path: validates, writes the local row(s) and enqueues the outbox op(s) in ONE transaction.
// Outbox PayloadJson (forwarded as payload.data by the sync layer for aggregates it does not snapshot itself) holds ONLY the fields that changed,
// plus the extras the cloud reads (SYNC-API-CONTRACT §5.7): "actor_user_id" (→ op.actor_user_id) and "field_updated_at" (→ payload.field_updated_at).
// Only changed fields travel, so an unchanged (stale) field can never beat a newer cloud edit in the per-field merge. base_row_version is filled by the sync loop at push time.
using System.Globalization;
using System.Text.Json.Nodes;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.DoctorExtra;

public sealed class DoctorSelfEditService : IDoctorSelfEditService
{
    public const string SessionEnded = "Your session has ended. Sign in again.";
    public const string NotYours = "You can only change your own availability and profile.";
    public const string DoctorMissing = "This doctor profile could not be found on this computer.";
    public const string SaveFailed = "The change could not be saved. Please try again.";

    public const string Doctor = "doctor";
    public const string DoctorSchedule = "doctor_schedule";
    public const string DoctorBlockedDate = "doctor_blocked_date";

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IAuthService _auth;
    private readonly ISyncOutbox _outbox;
    private readonly ISyncBridge? _sync;

    public DoctorSelfEditService(IDbContextFactory<ClinicAppDbContext> factory, IAuthService auth, ISyncOutbox outbox, ISyncBridge? sync = null)
    {
        _factory = factory;
        _auth = auth;
        _outbox = outbox;
        _sync = sync;
    }

    // ── actor ────────────────────────────────────────────────────────────
    /// <summary>The session user may edit when they are an admin, or the doctor themself.</summary>
    private (Guid? ActorUserId, string? Error) Authorize(Guid doctorId)
    {
        var s = _auth.Current;
        if (s is null) return (null, SessionEnded);
        if (s.IsAdmin) return (s.UserId, null);
        var mine = s.DoctorId ?? (s.IsDoctor ? s.StaffId : null);
        return s.IsDoctor && mine == doctorId ? (s.UserId, null) : (null, NotYours);
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'+00:00'", CultureInfo.InvariantCulture);
    private static string Hms(TimeOnly t) => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    private static string Ymd(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Task Enqueue(ClinicAppDbContext db, string aggregate, Guid entityId, string type, JsonObject payload, DateTimeOffset now, CancellationToken ct) =>
        _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(), Aggregate = aggregate, EntityId = entityId, OperationType = type, PayloadJson = payload.ToJsonString(), CreatedAt = now,
        }, ct);

    // ── profile ──────────────────────────────────────────────────────────
    public async Task<SelfEditResult> SaveProfileAsync(Guid doctorId, DoctorSelfProfileEdit edit, CancellationToken ct = default)
    {
        var (actor, denied) = Authorize(doctorId);
        if (denied is not null) return SelfEditResult.Failure(denied);
        var spec = Blank(edit.Specialization);
        var name = Blank(edit.FullName);
        var bio = Blank(edit.Bio);
        var contact = Blank(edit.ContactNumber);
        if (spec is null) return SelfEditResult.Failure("Specialization is required.");
        if (spec.Length > 100) return SelfEditResult.Failure("Specialization must be 100 characters or fewer.");
        if (name is null) return SelfEditResult.Failure("Full name is required.");
        if (name.Length > 200) return SelfEditResult.Failure("Full name must be 200 characters or fewer.");
        if (bio?.Length > 2000) return SelfEditResult.Failure("Bio must be 2,000 characters or fewer.");
        if (contact?.Length > 50) return SelfEditResult.Failure("Contact number must be 50 characters or fewer.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var d = await db.Doctors.Include(x => x.StaffAccount).SingleOrDefaultAsync(x => x.DoctorId == doctorId, ct);
        if (d is null) return SelfEditResult.Failure(DoctorMissing);

        var now = DateTimeOffset.UtcNow;
        var data = new JsonObject { ["doctor_id"] = doctorId.ToString("D") };
        var staffData = new JsonObject();
        var stamps = new JsonObject();
        var changed = new JsonArray();
        void Mark(string field) { stamps[field] = Iso(now); changed.Add(field); }

        if (!string.Equals(d.Specialization, spec, StringComparison.Ordinal)) { d.Specialization = spec; data["specialization"] = spec; Mark("specialization"); }
        if (!string.Equals(Blank(d.Bio), bio, StringComparison.Ordinal)) { d.Bio = bio; data["bio"] = bio; Mark("bio"); }
        if (d.StaffAccount is { } staff)
        {
            if (!string.Equals(staff.FullName, name, StringComparison.Ordinal)) { staff.FullName = name; staffData["full_name"] = name; Mark("staff.full_name"); }
            if (!string.Equals(Blank(staff.ContactNumber), contact, StringComparison.Ordinal)) { staff.ContactNumber = contact; staffData["contact_number"] = contact; Mark("staff.contact_number"); }
            if (staffData.Count > 0) staff.UpdatedAt = now;
        }
        if (changed.Count == 0) return SelfEditResult.Success(doctorId); // nothing differs: no write, no outbox op

        d.UpdatedAt = now;
        data["updated_at"] = Iso(now);
        if (staffData.Count > 0) data["staff_account"] = staffData;
        data["changed_fields"] = changed;
        data["actor_user_id"] = actor!.Value.ToString("D");
        data["field_updated_at"] = stamps;
        await Enqueue(db, Doctor, doctorId, "update", data, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _sync?.RequestSync();
        return SelfEditResult.Success(doctorId);
    }

    // ── weekly schedule ──────────────────────────────────────────────────
    public async Task<SelfEditResult> SaveWeeklyScheduleAsync(Guid doctorId, IReadOnlyList<DoctorScheduleDayEdit> days, CancellationToken ct = default)
    {
        var (actor, denied) = Authorize(doctorId);
        if (denied is not null) return SelfEditResult.Failure(denied);
        if (days is null || days.Count == 0) return SelfEditResult.Failure("There are no hours to save.");
        if (days.Any(x => x.DayOfWeek is < 0 or > 6)) return SelfEditResult.Failure("Day of week must be Sunday (0) to Saturday (6).");
        if (days.GroupBy(x => x.DayOfWeek).Any(g => g.Count() > 1)) return SelfEditResult.Failure("Each weekday can only appear once.");
        if (DoctorExtraRules.ScheduleError(days.Select(x => new ScheduleDay(x.DayOfWeek, x.IsActive, x.Start, x.End))) is { } bad) return SelfEditResult.Failure(bad);

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Doctors.AnyAsync(x => x.DoctorId == doctorId, ct)) return SelfEditResult.Failure(DoctorMissing);
        var existing = await db.DoctorSchedules.Where(s => s.DoctorId == doctorId).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var ops = 0;
        foreach (var day in days.OrderBy(x => x.DayOfWeek))
        {
            var row = existing.FirstOrDefault(s => s.DayOfWeek == day.DayOfWeek);
            var data = new JsonObject { ["doctor_id"] = doctorId.ToString("D"), ["day_of_week"] = (int)day.DayOfWeek };
            var stamps = new JsonObject();
            string type;
            if (row is null)
            {
                row = new DoctorSchedule
                {
                    Id = Guid.NewGuid(), DoctorId = doctorId, DayOfWeek = day.DayOfWeek, IsActive = day.IsActive,
                    StartTime = day.Start, EndTime = day.End, CreatedAt = now, UpdatedAt = now,
                };
                db.DoctorSchedules.Add(row);
                type = "create";
                data["is_active"] = day.IsActive; data["start_time"] = Hms(day.Start); data["end_time"] = Hms(day.End);
                foreach (var f in new[] { "is_active", "start_time", "end_time" }) stamps[f] = Iso(now);
            }
            else
            {
                type = "update";
                if (row.IsActive != day.IsActive) { row.IsActive = day.IsActive; data["is_active"] = day.IsActive; stamps["is_active"] = Iso(now); }
                if (row.StartTime != day.Start) { row.StartTime = day.Start; data["start_time"] = Hms(day.Start); stamps["start_time"] = Iso(now); }
                if (row.EndTime != day.End) { row.EndTime = day.End; data["end_time"] = Hms(day.End); stamps["end_time"] = Iso(now); }
                if (stamps.Count == 0) continue; // this weekday did not change
                row.UpdatedAt = now;
            }
            data["id"] = row.Id.ToString("D");
            data["updated_at"] = Iso(now);
            data["changed_fields"] = new JsonArray(stamps.Select(k => (JsonNode?)JsonValue.Create(k.Key)).ToArray());
            data["actor_user_id"] = actor!.Value.ToString("D");
            data["field_updated_at"] = stamps;
            await Enqueue(db, DoctorSchedule, row.Id, type, data, now, ct);
            ops++;
        }
        if (ops == 0) return SelfEditResult.Success(doctorId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _sync?.RequestSync();
        return SelfEditResult.Success(doctorId);
    }

    // ── blocked dates ────────────────────────────────────────────────────
    public async Task<SelfEditResult> AddBlockedDateAsync(Guid doctorId, DateOnly date, string? reason, CancellationToken ct = default)
    {
        var (actor, denied) = Authorize(doctorId);
        if (denied is not null) return SelfEditResult.Failure(denied);
        if (date == default) return SelfEditResult.Failure("Choose the date to block.");
        var why = Blank(reason);
        if (why?.Length > 500) return SelfEditResult.Failure("Reason must be 500 characters or fewer.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Doctors.AnyAsync(x => x.DoctorId == doctorId, ct)) return SelfEditResult.Failure(DoctorMissing);
        if (await db.DoctorBlockedDates.AnyAsync(b => b.DoctorId == doctorId && b.BlockedDate == date, ct))
            return SelfEditResult.Failure("That date is already blocked.");
        var now = DateTimeOffset.UtcNow;
        var row = new DoctorBlockedDate { Id = Guid.NewGuid(), DoctorId = doctorId, BlockedDate = date, Reason = why, CreatedAt = now };
        db.DoctorBlockedDates.Add(row);
        var data = new JsonObject
        {
            ["id"] = row.Id.ToString("D"), ["doctor_id"] = doctorId.ToString("D"), ["blocked_date"] = Ymd(date), ["reason"] = why,
            ["created_at"] = Iso(now), ["actor_user_id"] = actor!.Value.ToString("D"),
            ["field_updated_at"] = new JsonObject { ["reason"] = Iso(now) },
        };
        await Enqueue(db, DoctorBlockedDate, row.Id, "create", data, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _sync?.RequestSync();
        return SelfEditResult.Success(row.Id);
    }

    public async Task<SelfEditResult> UpdateBlockedDateReasonAsync(Guid blockedDateId, string? reason, CancellationToken ct = default)
    {
        var why = Blank(reason);
        if (why?.Length > 500) return SelfEditResult.Failure("Reason must be 500 characters or fewer.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var row = await db.DoctorBlockedDates.SingleOrDefaultAsync(b => b.Id == blockedDateId, ct);
        if (row is null) return SelfEditResult.Failure(SaveFailed);
        var (actor, denied) = Authorize(row.DoctorId);
        if (denied is not null) return SelfEditResult.Failure(denied);
        if (string.Equals(Blank(row.Reason), why, StringComparison.Ordinal)) return SelfEditResult.Success(row.Id);
        var now = DateTimeOffset.UtcNow;
        row.Reason = why;
        var data = new JsonObject
        {
            ["id"] = row.Id.ToString("D"), ["doctor_id"] = row.DoctorId.ToString("D"), ["blocked_date"] = Ymd(row.BlockedDate), ["reason"] = why,
            ["actor_user_id"] = actor!.Value.ToString("D"), ["field_updated_at"] = new JsonObject { ["reason"] = Iso(now) },
        };
        await Enqueue(db, DoctorBlockedDate, row.Id, "update", data, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _sync?.RequestSync();
        return SelfEditResult.Success(row.Id);
    }

    public async Task<SelfEditResult> RemoveBlockedDateAsync(Guid blockedDateId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var row = await db.DoctorBlockedDates.SingleOrDefaultAsync(b => b.Id == blockedDateId, ct);
        if (row is null) return SelfEditResult.Failure(SaveFailed);
        var (actor, denied) = Authorize(row.DoctorId);
        if (denied is not null) return SelfEditResult.Failure(denied);
        var now = DateTimeOffset.UtcNow;
        db.DoctorBlockedDates.Remove(row);
        // The delete payload stays thin on purpose: the cloud row decides which doctor it belongs to; the actor is what it validates.
        var data = new JsonObject
        {
            ["id"] = row.Id.ToString("D"), ["doctor_id"] = row.DoctorId.ToString("D"), ["blocked_date"] = Ymd(row.BlockedDate),
            ["actor_user_id"] = actor!.Value.ToString("D"),
        };
        await Enqueue(db, DoctorBlockedDate, row.Id, "delete", data, now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _sync?.RequestSync();
        return SelfEditResult.Success(row.Id);
    }
}
//JJQR - END
