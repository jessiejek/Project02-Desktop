//JJQR - AI (Nov 21, 2026) - D-SCHED "Clinic availability": weekly hours (per day upsert keyed on doctor + weekday), daily patient limit, blocked dates (+ affected-bookings warning). Ports doctor/schedule/page.tsx + DoctorsController schedules / blocked-dates.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.DoctorExtra;

public sealed record BlockedDateRow(Guid Id, DateOnly Date, string Reason);
public sealed record DoctorAvailability(IReadOnlyList<ScheduleDay> Days, int? DailyPatientLimit, IReadOnlyList<BlockedDateRow> BlockedDates);

public interface IDoctorScheduleService
{
    /// <summary>null when the doctor profile is missing ("Availability unavailable").</summary>
    Task<DoctorAvailability?> GetAsync(Guid doctorId, CancellationToken ct = default);
    Task<DoctorResult> SaveAsync(Guid doctorId, IReadOnlyList<ScheduleDay> days, string? dailyPatientLimit, CancellationToken ct = default);
    /// <summary>The doctor's non-cancelled bookings on that date (web warning before blocking).</summary>
    Task<int> CountAffectedBookingsAsync(Guid doctorId, DateOnly date, CancellationToken ct = default);
    Task<DoctorResult> AddBlockedDateAsync(Guid doctorId, DateOnly date, string? reason, CancellationToken ct = default);
    Task<DoctorResult> RemoveBlockedDateAsync(Guid doctorId, Guid blockedDateId, CancellationToken ct = default);
}

public sealed class DoctorScheduleService : IDoctorScheduleService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly ISyncBridge? _sync;

    public DoctorScheduleService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, ISyncBridge? sync = null)
    {
        _factory = factory;
        _outbox = outbox;
        _sync = sync;
    }

    public async Task<DoctorAvailability?> GetAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var doctor = await db.Doctors.AsNoTracking().SingleOrDefaultAsync(d => d.DoctorId == doctorId, ct);
        if (doctor is null) return null;
        var rows = await db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == doctorId).ToListAsync(ct);
        // Web defaultSchedule(): all seven days inactive with blank times until rows exist.
        var days = Enumerable.Range(0, 7).Select(i =>
        {
            var r = rows.FirstOrDefault(x => x.DayOfWeek == i);
            return r is null ? new ScheduleDay((short)i, false, default, default) : new ScheduleDay((short)i, r.IsActive, r.StartTime, r.EndTime);
        }).ToList();
        var blocked = await db.DoctorBlockedDates.AsNoTracking().Where(b => b.DoctorId == doctorId).ToListAsync(ct);
        return new DoctorAvailability(days, doctor.DailyPatientLimit,
            blocked.OrderBy(b => b.BlockedDate).Select(b => new BlockedDateRow(b.Id, b.BlockedDate, b.Reason ?? "")).ToList());
    }

    public async Task<DoctorResult> SaveAsync(Guid doctorId, IReadOnlyList<ScheduleDay> days, string? dailyPatientLimit, CancellationToken ct = default)
    {
        if (DoctorExtraRules.ScheduleError(days) is { } bad) return DoctorResult.Fail(bad);
        var limit = DoctorExtraRules.ParseDailyLimit(dailyPatientLimit);
        if (!limit.Ok) return DoctorResult.Fail(limit.Error!);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.DoctorId == doctorId, ct);
        if (doctor is null) return DoctorResult.Fail("Your availability could not be fully saved. Review the hours and try again.");
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        doctor.DailyPatientLimit = limit.Value;
        doctor.UpdatedAt = now;
        var existing = await db.DoctorSchedules.Where(s => s.DoctorId == doctorId).ToListAsync(ct);
        foreach (var d in days)
        {
            var row = existing.FirstOrDefault(s => s.DayOfWeek == d.DayOfWeek);
            if (row is null)
                db.DoctorSchedules.Add(new DoctorSchedule
                {
                    Id = Guid.NewGuid(), DoctorId = doctorId, DayOfWeek = d.DayOfWeek, IsActive = d.IsActive,
                    StartTime = d.StartTime, EndTime = d.EndTime, CreatedAt = now, UpdatedAt = now,
                });
            else
            {
                row.IsActive = d.IsActive;
                row.StartTime = d.StartTime;
                row.EndTime = d.EndTime;
                row.UpdatedAt = now;
            }
        }
        await Enqueue(db, "doctor_schedule", doctorId, "update", $$"""{"doctorId":"{{doctorId}}","dailyPatientLimit":{{(limit.Value?.ToString() ?? "null")}}}""", now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _sync?.RequestSync();
        return DoctorResult.Ok(doctorId);
    }

    public async Task<int> CountAffectedBookingsAsync(Guid doctorId, DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Bookings.AsNoTracking().CountAsync(b => b.DoctorId == doctorId && b.AppointmentDate == date && b.Status != BookingStatus.Cancelled, ct);
    }

    public async Task<DoctorResult> AddBlockedDateAsync(Guid doctorId, DateOnly date, string? reason, CancellationToken ct = default)
    {
        if (date == default) return DoctorResult.Fail("This date could not be blocked. Please try again.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (!await db.Doctors.AnyAsync(d => d.DoctorId == doctorId, ct)) return DoctorResult.Fail("This date could not be blocked. Please try again.");
        var now = DateTimeOffset.UtcNow;
        var row = new DoctorBlockedDate
        {
            Id = Guid.NewGuid(), DoctorId = doctorId, BlockedDate = date,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), CreatedAt = now,
        };
        db.DoctorBlockedDates.Add(row);
        await Enqueue(db, "doctor_blocked_date", row.Id, "create", $$"""{"doctorId":"{{doctorId}}","date":"{{date:yyyy-MM-dd}}"}""", now, ct);
        await db.SaveChangesAsync(ct);
        _sync?.RequestSync();
        return DoctorResult.Ok(row.Id);
    }

    public async Task<DoctorResult> RemoveBlockedDateAsync(Guid doctorId, Guid blockedDateId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DoctorBlockedDates.SingleOrDefaultAsync(b => b.Id == blockedDateId, ct);
        // DoctorsController.RemoveBlockedDate: NotFound / Forbid when it is another doctor's.
        if (row is null || row.DoctorId != doctorId) return DoctorResult.Fail("The blocked date could not be removed. Please try again.");
        db.DoctorBlockedDates.Remove(row);
        await Enqueue(db, "doctor_blocked_date", row.Id, "delete", $$"""{"doctorId":"{{doctorId}}","date":"{{row.BlockedDate:yyyy-MM-dd}}"}""", DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        _sync?.RequestSync();
        return DoctorResult.Ok(row.Id);
    }

    Task Enqueue(ClinicAppDbContext db, string aggregate, Guid entityId, string op, string payload, DateTimeOffset now, CancellationToken ct) =>
        _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(), Aggregate = aggregate, EntityId = entityId, OperationType = op, PayloadJson = payload, CreatedAt = now,
        }, ct);
}
//JJQR - END
