//JJQR - AI (21 Nov 2026) - Api implementation of IStaffPatientsService, the same calls the web /staff/patients, /staff/patients/[id] and the walk-in wizard make:
//   GET  /api/patients/search?q=&page=&pageSize=&sort=-created     (25 per page, newest first)
//   POST /api/patients { first_name, middle_name, last_name, date_of_birth, sex, contact_number, address, email:"", is_guest:true, user_id:null }
//        (quick register; the server issues the MF- patient code)
//   GET  /api/patients/{id} · GET /api/bookings?patientId= · GET /api/consultations?patientId=   (Overview / Bookings (n) / Medical records (n))
using System.Text.RegularExpressions;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Staff;
using static ClinicNode.Api.Services.ApiStaff;

namespace ClinicNode.Api.Services;

public sealed partial class ApiStaffPatientsService(IApiClient api) : IStaffPatientsService
{
    internal sealed record PatientDto(Guid PatientId, string? PatientCode, string? FirstName, string? LastName, SexType? Sex, DateOnly? DateOfBirth,
        string? ContactNumber, string? Email, Guid? UserId, bool? IsGuest);
    internal sealed record DiagnosisDto(string? CustomDescription);
    internal sealed record ConsultBookingDto(DateOnly? AppointmentDate);
    internal sealed record ConsultationDto(Guid ConsultationId, string? ChiefComplaint, ConsultBookingDto? Bookings, List<DiagnosisDto>? ConsultationDiagnoses);

    public async Task<PatientPage> SearchAsync(string? query, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var q = query?.Trim();
        var res = await api.GetAsync<Paged<PatientDto>>("/api/patients/search",
            Q(("q", string.IsNullOrEmpty(q) ? null : q), ("page", page.ToString()), ("pageSize", pageSize.ToString()), ("sort", "-created")), ct);
        var items = (res?.Items ?? []).Select(ToRow).ToList();
        return new PatientPage(items, res?.TotalCount ?? items.Count, page, pageSize);
    }

    /// <summary>Same checks and order as StaffPatientsService (the wizard's quickRegisterProblem, then the API's required checks).</summary>
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

    public bool CanQuickRegister(QuickRegisterForm form) => ValidateQuickRegister(form) is null;

    public async Task<(StaffResult Result, StaffPatientRow? Patient)> QuickRegisterAsync(QuickRegisterForm f, CancellationToken ct = default)
    {
        if (ValidateQuickRegister(f) is { } problem) return (StaffResult.Fail(problem), null);
        PatientDto? created;
        try
        {
            created = await api.SendAsync<PatientDto>(HttpMethod.Post, "/api/patients", new
            {
                patient_id = f.PatientId ?? Guid.Empty, //JJQR - AI (Nov 22, 2026) - step 0.3: the desktop's own id (Guid.Empty = the server makes one, as before) //JJQR - END
                first_name = TitleCase(f.FirstName.Trim()),
                middle_name = string.IsNullOrWhiteSpace(f.MiddleName) ? null : TitleCase(f.MiddleName.Trim()),
                last_name = TitleCase(f.LastName.Trim()),
                date_of_birth = Day(f.DateOfBirth!.Value),
                sex = f.Sex!.Value.ToString(),
                contact_number = string.IsNullOrWhiteSpace(f.ContactNumber) ? null : f.ContactNumber.Trim(),
                address = string.IsNullOrWhiteSpace(f.Address) ? null : f.Address.Trim(),
                email = "",
                is_guest = true,
                user_id = (Guid?)null,
            }, null, ct);
        }
        catch (ApiValidationException ex) { return (StaffResult.Fail(ex.Message), null); }
        catch (Exception ex) when (IsServerAnswer(ex)) { return (StaffResult.Fail("Could not register this patient. Try again."), null); }
        if (created is null) return (StaffResult.Fail("Could not register this patient. Try again."), null);
        //JJQR - AI (Nov 23, 2026) - Data Privacy Act (PH): record that the privacy notice was shown at the front desk (audit entry; best effort, the patient is already registered)
        if (f.PrivacyNoticeShown)
        {
            try { await api.SendAsync(HttpMethod.Post, $"/api/patients/{created.PatientId:D}/privacy-notice-shown", null, null, ct); }
            catch (Exception) { /* the audit entry is best effort */ }
        }
        //JJQR - END
        return (StaffResult.Ok(created.PatientId), ToRow(created));
    }

    public async Task<StaffPatientDetail?> GetDetailAsync(Guid patientId, CancellationToken ct = default)
    {
        //JJQR - AI (Nov 22, 2026) - patient, bookings and visits are asked for AT THE SAME TIME
        var tPatient = api.GetAsync<PatientDto>($"/api/patients/{patientId:D}", null, ct);
        var tBookings = api.GetAsync<List<BookingDto>>("/api/bookings", Q(("patientId", patientId.ToString("D"))), ct);
        var tConsults = api.GetAsync<List<ConsultationDto>>("/api/consultations", Q(("patientId", patientId.ToString("D"))), ct);
        PatientDto? p;
        try { p = await tPatient; }
        catch (ApiNotFoundException) { _ = Task.WhenAll(tBookings, tConsults).ContinueWith(t => _ = t.Exception, TaskScheduler.Default); return null; }
        if (p is null) return null;
        var bookings = await tBookings ?? [];
        var consults = await tConsults ?? [];
        //JJQR - END
        return new StaffPatientDetail(
            ToRow(p),
            bookings.Select(b => new PatientBookingRow(b.BookingId, b.Doctors?.StaffAccounts?.FullName ?? "", b.AppointmentDate, b.Status)).ToList(),
            consults.Select(c => new PatientRecordRow(c.ConsultationId, c.Bookings?.AppointmentDate, c.ChiefComplaint ?? "",
                (c.ConsultationDiagnoses ?? []).Select(d => d.CustomDescription ?? "").Where(d => d.Length > 0).ToList())).ToList());
    }

    static StaffPatientRow ToRow(PatientDto p) => new(
        p.PatientId, p.PatientCode ?? "", $"{p.FirstName} {p.LastName}", p.Sex ?? SexType.Male, p.DateOfBirth ?? DateOnly.MinValue,
        p.ContactNumber ?? "", p.Email ?? "", StaffRules.AccountStatus(p.UserId, p.IsGuest ?? false));

    /// <summary>Web toTitleCase: "rubina QUiajda" → "Rubina Quiajda"; splits on spaces, hyphens and apostrophes.</summary>
    internal static string TitleCase(string name) =>
        WordRx().Replace(name, m => char.ToUpperInvariant(m.Value[0]) + m.Value[1..].ToLowerInvariant());

    [GeneratedRegex(@"[^\s'-]+")]
    private static partial Regex WordRx();
}
//JJQR - END
