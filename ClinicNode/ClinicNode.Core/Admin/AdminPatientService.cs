// JJQR - AI (Nov 21, 2026) - ported from PatientsController + PrivacyController (admin list/create/update, access log, data requests).
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Events;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core.Sync;
// JJQR - END

namespace ClinicNode.Core.Admin;

public sealed record AdminPatientListItem(
    Guid PatientId,
    string PatientCode,
    string FirstName,
    string LastName,
    string? MiddleName,
    DateOnly DateOfBirth,
    SexType Sex,
    string? ContactNumber,
    string Email,
    bool IsGuest,
    DateTimeOffset CreatedAt,
    //JJQR - AI (21 Nov 2026) - web accountStatus(user_id, is_guest): Linked account / No account / Account unknown
    Guid? UserId = null);
    //JJQR - END

public sealed record AdminPatientDetail(
    AdminPatientListItem Patient,
    string? Address,
    IReadOnlyList<AdminBookingListItem> Bookings,
    IReadOnlyList<PatientConsultSummary> Consultations,
    IReadOnlyList<AccessLogEntry> AccessLog,
    //JJQR - AI (21 Nov 2026) - web AccessLogPanel loads on its own: a failed access-log read shows "The access log could not be loaded." in that tab only
    bool AccessLogFailed = false);
    //JJQR - END

public sealed record PatientConsultSummary(Guid ConsultationId, Guid BookingId, DateOnly Date, string DoctorName, ConsultationStatus Status,
    //JJQR - AI (21 Nov 2026) - web patient page lists date + chief complaint
    string? ChiefComplaint = null);
    //JJQR - END

public sealed record AccessLogEntry(DateTimeOffset At, string Who, string Role, string Area);

public sealed record PatientEdit(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    SexType Sex,
    string? MiddleName = null,
    string? ContactNumber = null,
    string? Email = null,
    string? Address = null,
    //JJQR - AI (Nov 23, 2026) - Data Privacy Act (PH): "I showed the patient the privacy notice" ticked when ADDING a patient; recorded in the audit log (never as the patient's consent)
    bool PrivacyNoticeShown = false);

public sealed record DataRequestItem(
    Guid Id,
    Guid PatientId,
    string PatientName,
    string PatientCode,
    string Kind,
    string Details,
    string Status,
    DateTimeOffset RequestedAt,
    DateOnly? RetainUntil,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote);

public interface IAdminPatientService
{
    Task<AdminPage<AdminPatientListItem>> SearchAsync(string? q, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default);
    Task<AdminResult<AdminPatientDetail>> GetAsync(Guid patientId, CancellationToken ct = default);
    Task<AdminResult<AdminPatientListItem>> CreateAsync(PatientEdit edit, CancellationToken ct = default);
    Task<AdminResult<AdminPatientListItem>> UpdateAsync(Guid patientId, PatientEdit edit, CancellationToken ct = default);
    Task<IReadOnlyList<AccessLogEntry>> GetAccessLogAsync(Guid patientId, int take = 200, CancellationToken ct = default);
    Task<IReadOnlyList<DataRequestItem>> ListDataRequestsAsync(string? status, CancellationToken ct = default);
    Task<AdminResult<DataRequestItem>> ResolveDataRequestAsync(Guid id, string status, string? note, CancellationToken ct = default);
}

public sealed class AdminPatientService : IAdminPatientService
{
    public const int RetentionYears = 7;

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;
    private readonly IPatientCodeAllocator _codes;
    private readonly IClinicEventBus _events;

    // JJQR - AI (Nov 21, 2026)
    private readonly ISyncBridge? _sync;
    // JJQR - END

    public AdminPatientService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth, IPatientCodeAllocator codes, IClinicEventBus events, ISyncBridge? sync = null)
    {
        // JJQR - AI (Nov 21, 2026)
        _sync = sync;
        // JJQR - END
        _factory = factory;
        _outbox = outbox;
        _auth = auth;
        _codes = codes;
        _events = events;
    }

    public async Task<AdminPage<AdminPatientListItem>> SearchAsync(string? q, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.Patients.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p =>
                p.FirstName.Contains(s) || p.LastName.Contains(s) ||
                p.PatientCode.Contains(s) || p.Email.Contains(s) ||
                (p.ContactNumber != null && p.ContactNumber.Contains(s)));
        }
        var desc = sort is not null && sort.StartsWith('-');
        var key = (sort ?? "name").TrimStart('-');
        // Web copy: "newest first" on the patients page (created desc); search default in the API is name.
        query = (key, desc) switch
        {
            ("code", false) => query.OrderBy(p => p.PatientCode),
            ("code", true) => query.OrderByDescending(p => p.PatientCode),
            ("created", false) => query.OrderBy(p => p.CreatedAt),
            ("created", true) => query.OrderByDescending(p => p.CreatedAt),
            ("name", true) => query.OrderByDescending(p => p.LastName).ThenByDescending(p => p.FirstName),
            ("name", false) => query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName),
            _ => query.OrderByDescending(p => p.CreatedAt),
        };
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).Select(p => Map(p)).ToListAsync(ct);
        return new AdminPage<AdminPatientListItem>(items, total, page, pageSize);
    }

    public async Task<AdminResult<AdminPatientDetail>> GetAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var p = await db.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.PatientId == patientId, ct);
        if (p is null) return AdminResult<AdminPatientDetail>.Fail("Patient not found.");

        if (_auth.Current is { } actor)
        {
            AdminAudit.RecordAccess(db, actor.UserId, patientId, "Patient record");
            await db.SaveChangesAsync(ct);
        }

        var bookings = await db.Bookings.AsNoTracking()
            .Include(b => b.Patient)
            .Include(b => b.Doctor)!.ThenInclude(d => d.StaffAccount)
            .Include(b => b.Payment)
            .Where(b => b.PatientId == patientId)
            .OrderByDescending(b => b.AppointmentDate)
            .ToListAsync(ct);

        var consults = await db.Consultations.AsNoTracking()
            .Include(c => c.Booking)!.ThenInclude(b => b!.Doctor)!.ThenInclude(d => d!.StaffAccount)
            .Where(c => c.PatientId == patientId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var access = await LoadAccessAsync(db, patientId, 200, ct);

        var bookingItems = bookings.Select(b => new AdminBookingListItem(
            b.BookingId, b.PatientId, $"{p.FirstName} {p.LastName}".Trim(), p.PatientCode,
            b.DoctorId, b.Doctor?.StaffAccount?.FullName ?? "", b.AppointmentDate, b.SlotStartTime, b.SlotEndTime,
            b.Status, b.Payment?.Status, b.QueueNumber, b.IsWalkIn)).ToList();

        var consultItems = consults.Select(c => new PatientConsultSummary(
            c.ConsultationId, c.BookingId, c.Booking?.AppointmentDate ?? default,
            c.Booking?.Doctor?.StaffAccount?.FullName ?? "", c.Status,
            //JJQR - AI (21 Nov 2026)
            c.ChiefComplaint)).ToList();
            //JJQR - END

        return AdminResult<AdminPatientDetail>.Ok(new AdminPatientDetail(Map(p) with { }, p.Address, bookingItems, consultItems, access));
    }

    public async Task<AdminResult<AdminPatientListItem>> CreateAsync(PatientEdit edit, CancellationToken ct = default)
    {
        if (_auth.Current is null) return AdminResult<AdminPatientListItem>.Fail("Not signed in.");
        var proto = Apply(new Patient { Email = edit.Email ?? "" }, edit);
        var check = AdminCheck.Patient(proto);
        if (check.HasErrors) return AdminResult<AdminPatientListItem>.Fail(check.Message!);

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        proto.PatientId = Guid.NewGuid();
        proto.UserId = null;
        proto.IsGuest = true;
        proto.CreatedAt = now;
        proto.UpdatedAt = now;
        proto.PatientCode = await _codes.NextAsync(db, ct);
        db.Patients.Add(proto);
        AdminAudit.Add(db, AuditEntityType.Patient, proto.PatientId, "Created", _auth.Current.UserId, $"Code: {proto.PatientCode}");
        if (edit.PrivacyNoticeShown) AdminAudit.Add(db, AuditEntityType.Patient, proto.PatientId, "PrivacyNoticeShown", _auth.Current.UserId, "Privacy notice shown at the front desk"); //JJQR - AI (Nov 23, 2026) //JJQR - END
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "patient",
            EntityId = proto.PatientId,
            OperationType = "create",
            PayloadJson = $$"""{"patientId":"{{proto.PatientId}}","patientCode":"{{proto.PatientCode}}"}""",
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.PatientsChanged, PatientId: proto.PatientId));
        return AdminResult<AdminPatientListItem>.Ok(Map(proto));
    }

    public async Task<AdminResult<AdminPatientListItem>> UpdateAsync(Guid patientId, PatientEdit edit, CancellationToken ct = default)
    {
        if (_auth.Current is null) return AdminResult<AdminPatientListItem>.Fail("Not signed in.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Patients.SingleOrDefaultAsync(p => p.PatientId == patientId, ct);
        if (row is null) return AdminResult<AdminPatientListItem>.Fail("Patient not found.");
        var proposed = Apply(CloneScalar(row), edit);
        var check = AdminCheck.Patient(proposed);
        if (check.HasErrors) return AdminResult<AdminPatientListItem>.Fail(check.Message!);

        // JJQR - AI (Nov 21, 2026) - queue an update op with changed_fields like PatientService.UpdateAsync; skip when nothing changed.
        var details = AdminAudit.ChangedFields(
            ("First name", row.FirstName, proposed.FirstName),
            ("Last name", row.LastName, proposed.LastName),
            ("Middle name", row.MiddleName, proposed.MiddleName),
            ("Date of birth", row.DateOfBirth, proposed.DateOfBirth),
            ("Sex", row.Sex, proposed.Sex),
            ("Contact number", row.ContactNumber, proposed.ContactNumber),
            ("Email", row.Email, proposed.Email),
            ("Address", row.Address, proposed.Address));
        var changed = new List<string>();
        if (proposed.FirstName != row.FirstName) { row.FirstName = proposed.FirstName; changed.Add("first_name"); }
        if (proposed.LastName != row.LastName) { row.LastName = proposed.LastName; changed.Add("last_name"); }
        if (proposed.MiddleName != row.MiddleName) { row.MiddleName = proposed.MiddleName; changed.Add("middle_name"); }
        if (proposed.DateOfBirth != row.DateOfBirth) { row.DateOfBirth = proposed.DateOfBirth; changed.Add("date_of_birth"); }
        if (proposed.Sex != row.Sex) { row.Sex = proposed.Sex; changed.Add("sex"); }
        if (proposed.ContactNumber != row.ContactNumber) { row.ContactNumber = proposed.ContactNumber; changed.Add("contact_number"); }
        if (proposed.Address != row.Address) { row.Address = proposed.Address; changed.Add("address"); }
        if (proposed.Email != row.Email) { row.Email = proposed.Email; changed.Add("email"); }
        if (changed.Count == 0) return AdminResult<AdminPatientListItem>.Ok(Map(row));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        row.UpdatedAt = now;
        if (details is not null)
            AdminAudit.Add(db, AuditEntityType.Patient, row.PatientId, "Updated", _auth.Current.UserId, details);
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "patient",
            EntityId = row.PatientId,
            OperationType = "update",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { patientId = row.PatientId, changed_fields = changed }),
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _sync?.RequestSync();
        // JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.PatientsChanged, PatientId: row.PatientId));
        return AdminResult<AdminPatientListItem>.Ok(Map(row));
    }

    public async Task<IReadOnlyList<AccessLogEntry>> GetAccessLogAsync(Guid patientId, int take = 200, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await LoadAccessAsync(db, patientId, take, ct);
    }

    public async Task<IReadOnlyList<DataRequestItem>> ListDataRequestsAsync(string? status, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.DataRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
        var rows = await (from r in q
                          join p in db.Patients.AsNoTracking() on r.PatientId equals p.PatientId
                          orderby r.RequestedAt descending
                          select new DataRequestItem(r.Id, r.PatientId, p.FirstName + " " + p.LastName, p.PatientCode, r.Kind, r.Details, r.Status, r.RequestedAt, r.RetainUntil, r.ResolvedAt, r.ResolutionNote))
            .Take(500).ToListAsync(ct);
        return rows;
    }

    public async Task<AdminResult<DataRequestItem>> ResolveDataRequestAsync(Guid id, string status, string? note, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult<DataRequestItem>.Fail("Only an administrator can resolve a data request.");
        var next = DataRequestStatus.All.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
        if (next is null) return AdminResult<DataRequestItem>.Fail("Status must be Pending, Scheduled, Completed or Rejected.");
        var check = new AdminCheck().Optional("Note", note, 1000);
        if (check.HasErrors) return AdminResult<DataRequestItem>.Fail(check.Message!);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var req = await db.DataRequests.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (req is null) return AdminResult<DataRequestItem>.Fail("Request not found.");
        if (next == DataRequestStatus.Completed && req.Kind == DataRequestKind.Deletion && req.RetainUntil is { } until && until > DateOnly.FromDateTime(DateTime.UtcNow))
            return AdminResult<DataRequestItem>.Fail($"The medical record must be kept until {until:yyyy-MM-dd} (7-year retention). Keep the request Scheduled until then.");

        req.Status = next;
        req.ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var done = next is DataRequestStatus.Completed or DataRequestStatus.Rejected;
        req.ResolvedAt = done ? DateTimeOffset.UtcNow : null;
        req.ResolvedByUserId = done ? _auth.Current.UserId : null;
        AdminAudit.Add(db, AuditEntityType.DataRequest, req.Id, "Resolved", _auth.Current.UserId, $"{req.Kind}; now {req.Status}");
        await db.SaveChangesAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END

        var patient = await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == req.PatientId, ct);
        return AdminResult<DataRequestItem>.Ok(new DataRequestItem(req.Id, req.PatientId, patient.FirstName + " " + patient.LastName, patient.PatientCode, req.Kind, req.Details, req.Status, req.RequestedAt, req.RetainUntil, req.ResolvedAt, req.ResolutionNote));
    }

    static async Task<IReadOnlyList<AccessLogEntry>> LoadAccessAsync(ClinicAppDbContext db, Guid patientId, int take, CancellationToken ct)
    {
        var rows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == AuditEntityType.Access && a.EntityId == patientId)
            .OrderByDescending(a => a.PerformedAt).Take(take).ToListAsync(ct);
        var ids = rows.Select(r => r.PerformedByUserId).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        var staff = await db.StaffAccounts.AsNoTracking().Where(s => ids.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, ct);
        return rows.Select(r =>
        {
            var s = r.PerformedByUserId is { } u && staff.TryGetValue(u, out var st) ? st : null;
            return new AccessLogEntry(r.PerformedAt, s?.FullName ?? "Unknown user", s?.Role.ToString() ?? "", r.Details ?? "");
        }).ToList();
    }

    static AdminPatientListItem Map(Patient p) => new(
        p.PatientId, p.PatientCode, p.FirstName, p.LastName, p.MiddleName, p.DateOfBirth, p.Sex,
        p.ContactNumber, p.Email, p.IsGuest, p.CreatedAt,
        //JJQR - AI (21 Nov 2026)
        p.UserId);
        //JJQR - END

    static Patient Apply(Patient p, PatientEdit e)
    {
        p.FirstName = e.FirstName.Trim();
        p.LastName = e.LastName.Trim();
        p.MiddleName = string.IsNullOrWhiteSpace(e.MiddleName) ? null : e.MiddleName.Trim();
        p.DateOfBirth = e.DateOfBirth;
        p.Sex = e.Sex;
        p.ContactNumber = string.IsNullOrWhiteSpace(e.ContactNumber) ? null : e.ContactNumber.Trim();
        p.Email = string.IsNullOrWhiteSpace(e.Email) ? "" : e.Email.Trim();
        p.Address = string.IsNullOrWhiteSpace(e.Address) ? null : e.Address.Trim();
        return p;
    }

    static Patient CloneScalar(Patient p) => new()
    {
        FirstName = p.FirstName, LastName = p.LastName, MiddleName = p.MiddleName,
        DateOfBirth = p.DateOfBirth, Sex = p.Sex, ContactNumber = p.ContactNumber,
        Email = p.Email, Address = p.Address,
    };
}
// JJQR - END
