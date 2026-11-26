//JJQR - AI (Nov 21, 2026) - staff Patients: paged search (name, code, phone, email; newest first), quick register with node-issued MF- codes, read-only patient detail (overview / bookings / medical records).
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Patients;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Staff;

public sealed record StaffPatientRow(Guid PatientId, string PatientCode, string FullName, SexType Sex, DateOnly DateOfBirth, string ContactNumber, string Email, string AccountStatus);

public sealed record PatientPage(IReadOnlyList<StaffPatientRow> Items, int TotalCount, int Page, int PageSize)
{
    public int PageCount => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public int RangeStart => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int RangeEnd => Math.Min(Page * PageSize, TotalCount);
}

/// <summary>Web <c>BLANK_QUICK_REGISTER</c> fields (Register a new patient dialog).</summary>
public sealed record QuickRegisterForm(string FirstName, string? MiddleName, string LastName, DateOnly? DateOfBirth, SexType? Sex, string? ContactNumber, string? Address,
    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.3: made by the screen once per registration attempt
    Guid? PatientId = null,
    //JJQR - AI (Nov 23, 2026) - Data Privacy Act (PH): the front desk ticked "I showed the patient the privacy notice"; it is recorded in the audit log (never as the patient's consent)
    bool PrivacyNoticeShown = false);

public sealed record PatientBookingRow(Guid BookingId, string DoctorName, DateOnly AppointmentDate, BookingStatus Status);
public sealed record PatientRecordRow(Guid ConsultationId, DateOnly? AppointmentDate, string ChiefComplaint, IReadOnlyList<string> Diagnoses);

public sealed record StaffPatientDetail(StaffPatientRow Patient, IReadOnlyList<PatientBookingRow> Bookings, IReadOnlyList<PatientRecordRow> Records);

public interface IStaffPatientsService
{
    Task<PatientPage> SearchAsync(string? query, int page = 1, int pageSize = 25, CancellationToken ct = default);
    string? ValidateQuickRegister(QuickRegisterForm form);
    bool CanQuickRegister(QuickRegisterForm form);
    Task<(StaffResult Result, StaffPatientRow? Patient)> QuickRegisterAsync(QuickRegisterForm form, CancellationToken ct = default);
    Task<StaffPatientDetail?> GetDetailAsync(Guid patientId, CancellationToken ct = default);
}

public sealed class StaffPatientsService : IStaffPatientsService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IPatientService _patients;

    public StaffPatientsService(IDbContextFactory<ClinicAppDbContext> factory, IPatientService patients)
    {
        _factory = factory;
        _patients = patients;
    }

    public async Task<PatientPage> SearchAsync(string? query, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = db.Patients.AsNoTracking().Where(p => p.MergedIntoPatientId == null).AsQueryable() /*JJQR - AI (2026-11-26) - GAV-12 a merged-away patient kept here is never listed //JJQR - END*/;
        var q = (query ?? "").Trim().ToLower();
        if (q.Length > 0)
        {
            rows = rows.Where(p =>
                (p.FirstName + " " + p.LastName).ToLower().Contains(q)
                || p.FirstName.ToLower().Contains(q)
                || p.LastName.ToLower().Contains(q)
                || p.PatientCode.ToLower().Contains(q)
                || (p.ContactNumber != null && p.ContactNumber.ToLower().Contains(q))
                || p.Email.ToLower().Contains(q));
        }
        // SQLite cannot ORDER BY a DateTimeOffset column, so "-created" is applied in memory (same result as the API).
        var all = await rows.ToListAsync(ct);
        var total = all.Count;
        var list = all.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.PatientCode)
            .Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PatientPage(list.Select(ToRow).ToList(), total, page, pageSize);
    }

    /// <summary>Same order as the wizard's <c>quickRegisterProblem</c>, then the API's required checks.</summary>
    public string? ValidateQuickRegister(QuickRegisterForm f) =>
        StaffRules.NotFuture(f.DateOfBirth, ClinicClock.Today)
        ?? StaffRules.Length(f.FirstName, 100, "First name")
        ?? StaffRules.Length(f.MiddleName, 100, "Middle name")
        ?? StaffRules.Length(f.LastName, 100, "Last name")
        ?? StaffRules.Length(f.Address, 300, "Address")
        ?? StaffRules.Phone(f.ContactNumber)
        ?? StaffRules.NotBefore(f.DateOfBirth)
        ?? StaffRules.Required(f.FirstName, 100, "First name")
        ?? StaffRules.Required(f.LastName, 100, "Last name")
        ?? (f.DateOfBirth is null ? "Date of birth is required." : null)
        ?? (f.Sex is null ? "Sex is required." : null);

    public bool CanQuickRegister(QuickRegisterForm f) => ValidateQuickRegister(f) is null;

    public async Task<(StaffResult Result, StaffPatientRow? Patient)> QuickRegisterAsync(QuickRegisterForm f, CancellationToken ct = default)
    {
        if (ValidateQuickRegister(f) is { } problem) return (StaffResult.Fail(problem), null);
        try
        {
            var created = await _patients.CreateAsync(new NewPatientRequest(
                f.FirstName.Trim(), f.LastName.Trim(), f.DateOfBirth!.Value, f.Sex!.Value,
                f.MiddleName, f.ContactNumber, f.Address, PatientId: f.PatientId), ct); //JJQR - AI (Nov 22, 2026) - step 0.3 //JJQR - END
            //JJQR - AI (Nov 23, 2026) - audit entry "privacy notice shown at the front desk" (syncs to the server like every audit row)
            if (f.PrivacyNoticeShown)
            {
                try
                {
                    await using var adb = await _factory.CreateDbContextAsync(ct);
                    adb.AuditLogs.Add(new ClinicApp.Domain.Entities.AuditLog
                    {
                        Id = Guid.NewGuid(), EntityType = ClinicApp.Domain.Enums.AuditEntityType.Patient, EntityId = created.PatientId,
                        Action = "PrivacyNoticeShown", Details = "Privacy notice shown at the front desk", PerformedAt = DateTimeOffset.UtcNow,
                    });
                    await adb.SaveChangesAsync(ct);
                }
                catch (Exception) { /* the patient is registered; the audit row is best effort */ }
            }
            //JJQR - END
            return (StaffResult.Ok(created.PatientId), ToRow(created));
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("register-patient (local)", ex); //JJQR - AI (Nov 23, 2026) - leave evidence //JJQR - END
            return (StaffResult.Fail(ClinicNode.Core.Diagnostics.ClinicErrorLog.Friendly(ex, "Could not register this patient. Try again.")), null);
        }
    }

    public async Task<StaffPatientDetail?> GetDetailAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var p = await db.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.PatientId == patientId, ct);
        if (p is null) return null;
        var bookings = await db.Bookings.AsNoTracking()
            .Include(b => b.Doctor).ThenInclude(d => d.StaffAccount)
            .Where(b => b.PatientId == patientId)
            .ToListAsync(ct);
        var consults = await db.Consultations.AsNoTracking()
            .Include(c => c.ConsultationDiagnoses)
            .Where(c => c.PatientId == patientId)
            .ToListAsync(ct);
        var dates = bookings.ToDictionary(b => b.BookingId, b => b.AppointmentDate);
        return new StaffPatientDetail(
            ToRow(p),
            bookings.OrderByDescending(b => b.AppointmentDate).ThenByDescending(b => b.CreatedAt)
                .Select(b => new PatientBookingRow(b.BookingId, b.Doctor?.StaffAccount?.FullName ?? "", b.AppointmentDate, b.Status)).ToList(),
            consults.Select(c => new PatientRecordRow(
                    c.ConsultationId,
                    dates.TryGetValue(c.BookingId, out var d) ? d : null,
                    c.ChiefComplaint ?? "",
                    c.ConsultationDiagnoses.Select(x => x.CustomDescription ?? "").Where(x => x.Length > 0).ToList()))
                .OrderByDescending(r => r.AppointmentDate).ToList());
    }

    static StaffPatientRow ToRow(ClinicApp.Domain.Entities.Patient p) => new(
        p.PatientId, p.PatientCode, $"{p.FirstName} {p.LastName}", p.Sex, p.DateOfBirth,
        p.ContactNumber ?? "", p.Email, StaffRules.AccountStatus(p.UserId, p.IsGuest));
}
//JJQR - END
