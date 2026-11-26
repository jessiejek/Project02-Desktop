//JJQR - AI (21 Nov 2026) - Api implementation of IAdminPatientService (web /admin/patients, /admin/patients/[id], /admin/data-requests):
//   GET /api/patients/search?q&page&pageSize&sort · GET /api/patients/{id} · POST /api/patients (guest row) · PUT /api/patients/{id} (full row: GET + patch)
//   GET /api/bookings?patientId · GET /api/consultations?patientId · GET /api/audit-logs/patient/{id}/access?take
//   GET /api/privacy/data-requests?status · PUT /api/privacy/data-requests/{id} { status, note }
// The server records the "Patient record" access itself when the record is opened; nothing about the patient is logged here.
using ClinicApp.Domain.Entities;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminPatientService(IApiClient api) : IAdminPatientService
{
    private static AdminPatientListItem Map(Patient p) => new(p.PatientId, p.PatientCode, p.FirstName, p.LastName, p.MiddleName,
        p.DateOfBirth, p.Sex, p.ContactNumber, p.Email, p.IsGuest, p.CreatedAt, p.UserId);

    private static Patient Apply(Patient p, PatientEdit e)
    {
        p.FirstName = e.FirstName.Trim();
        p.LastName = e.LastName.Trim();
        p.MiddleName = ApiAdmin.Blank(e.MiddleName);
        p.DateOfBirth = e.DateOfBirth;
        p.Sex = e.Sex;
        p.ContactNumber = ApiAdmin.Blank(e.ContactNumber);
        p.Email = ApiAdmin.Blank(e.Email) ?? "";
        p.Address = ApiAdmin.Blank(e.Address);
        return p;
    }

    private static string? Check(PatientEdit e)
    {
        if (string.IsNullOrWhiteSpace(e.FirstName)) return "First name is required.";
        if (string.IsNullOrWhiteSpace(e.LastName)) return "Last name is required.";
        if (e.DateOfBirth > DateOnly.FromDateTime(DateTime.Today)) return "Date of birth cannot be in the future.";
        return null;
    }

    public async Task<AdminPage<AdminPatientListItem>> SearchAsync(string? q, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default)
    {
        var res = await api.GetAsync<ApiPaged<Patient>>("/api/patients/search",
            ApiAdmin.Q(("q", q), ("page", page), ("pageSize", pageSize), ("sort", sort)), ct);
        var items = (res?.Items ?? []).Select(Map).ToList();
        return new AdminPage<AdminPatientListItem>(items, res?.TotalCount ?? items.Count, res?.Page ?? page, res?.PageSize ?? pageSize);
    }

    public async Task<AdminResult<AdminPatientDetail>> GetAsync(Guid patientId, CancellationToken ct = default)
    {
        Patient? p;
        try { p = await api.GetAsync<Patient>($"/api/patients/{patientId:D}", null, ct); }
        catch (ApiNotFoundException) { p = null; }
        if (p is null) return AdminResult<AdminPatientDetail>.Fail("Patient not found.");

        var bookingsTask = api.GetAsync<List<Booking>>("/api/bookings", ApiAdmin.Q(("patientId", patientId)), ct);
        var consultsTask = api.GetAsync<List<Consultation>>("/api/consultations", ApiAdmin.Q(("patientId", patientId)), ct);
        //JJQR - AI (21 Nov 2026) - web: the access log is its own panel; its failure must not blank the whole patient page
        var accessTask = AccessOrNullAsync(patientId, ct);
        //JJQR - END
        await Task.WhenAll(bookingsTask, consultsTask, accessTask);

        var bookings = (bookingsTask.Result ?? []).OrderByDescending(b => b.AppointmentDate).ThenByDescending(b => b.SlotStartTime).ToList();
        var byBooking = bookings.ToDictionary(b => b.BookingId);
        var bookingItems = bookings.Select(b =>
        {
            b.Patient ??= p;
            return ApiAdmin.ToListItem(b);
        }).ToList();
        var consultItems = (consultsTask.Result ?? []).OrderByDescending(c => c.CreatedAt).Select(c =>
        {
            var bk = c.Booking ?? (byBooking.TryGetValue(c.BookingId, out var x) ? x : null);
            var doctor = c.Doctor?.StaffAccount?.FullName ?? bk?.Doctor?.StaffAccount?.FullName ?? "";
            return new PatientConsultSummary(c.ConsultationId, c.BookingId, bk?.AppointmentDate ?? DateOnly.FromDateTime(c.CreatedAt.LocalDateTime), doctor, c.Status, c.ChiefComplaint);
        }).ToList();

        //JJQR - AI (21 Nov 2026)
        return AdminResult<AdminPatientDetail>.Ok(new AdminPatientDetail(Map(p), p.Address, bookingItems, consultItems, accessTask.Result ?? [], accessTask.Result is null));
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - null = the access log could not be read (cancellation still propagates)
    async Task<IReadOnlyList<AccessLogEntry>?> AccessOrNullAsync(Guid patientId, CancellationToken ct)
    {
        try { return await GetAccessLogAsync(patientId, 200, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }
    //JJQR - END

    public async Task<AdminResult<AdminPatientListItem>> CreateAsync(PatientEdit edit, CancellationToken ct = default)
    {
        if (Check(edit) is { } bad) return AdminResult<AdminPatientListItem>.Fail(bad);
        try
        {
            var body = new
            {
                first_name = edit.FirstName.Trim(),
                middle_name = ApiAdmin.Blank(edit.MiddleName),
                last_name = edit.LastName.Trim(),
                date_of_birth = edit.DateOfBirth,
                sex = edit.Sex,
                contact_number = ApiAdmin.Blank(edit.ContactNumber),
                address = ApiAdmin.Blank(edit.Address),
                email = ApiAdmin.Blank(edit.Email) ?? "",
                is_guest = true,
                user_id = (Guid?)null,
            };
            var row = await api.SendAsync<Patient>(HttpMethod.Post, "/api/patients", body, null, ct);
            //JJQR - AI (Nov 23, 2026) - Data Privacy Act (PH): record that the privacy notice was shown at the front desk (audit entry; best effort)
            if (row is not null && edit.PrivacyNoticeShown)
            {
                try { await api.SendAsync(HttpMethod.Post, $"/api/patients/{row.PatientId:D}/privacy-notice-shown", null, null, ct); }
                catch (Exception) { /* the patient is added; the audit entry is best effort */ }
            }
            //JJQR - END
            return row is null ? AdminResult<AdminPatientListItem>.Fail("The new patient could not be read back.") : AdminResult<AdminPatientListItem>.Ok(Map(row));
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<AdminPatientListItem>.Fail(ex.Message); }
    }

    public async Task<AdminResult<AdminPatientListItem>> UpdateAsync(Guid patientId, PatientEdit edit, CancellationToken ct = default)
    {
        if (Check(edit) is { } bad) return AdminResult<AdminPatientListItem>.Fail(bad);
        try
        {
            var row = await api.GetAsync<Patient>($"/api/patients/{patientId:D}", null, ct);
            if (row is null) return AdminResult<AdminPatientListItem>.Fail("Patient not found.");
            Apply(row, edit);
            await api.SendAsync(HttpMethod.Put, $"/api/patients/{patientId:D}", row, null, ct);
            return AdminResult<AdminPatientListItem>.Ok(Map(row));
        }
        catch (ApiNotFoundException) { return AdminResult<AdminPatientListItem>.Fail("Patient not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<AdminPatientListItem>.Fail(ex.Message); }
    }

    //JJQR - AI (2026-11-26) - GAV-10: the doctor's "Edit details" dialog, online. Fetch-merge-put like UpdateAsync / the web's updatePatient: the full
    // row is read, only the dialog's fields change, and the PUT sends it back (fields the dialog does not show keep their values).
    public async Task<AdminResult<PatientDetails>> GetDetailsAsync(Guid patientId, CancellationToken ct = default)
    {
        try
        {
            var row = await api.GetAsync<Patient>($"/api/patients/{patientId:D}", null, ct);
            return row is null ? AdminResult<PatientDetails>.Fail("Patient not found.") : AdminResult<PatientDetails>.Ok(PatientDetailsRules.From(row));
        }
        catch (ApiNotFoundException) { return AdminResult<PatientDetails>.Fail("Patient not found."); }
    }

    public async Task<AdminResult<PatientDetails>> UpdateDetailsAsync(Guid patientId, PatientDetails details, CancellationToken ct = default)
    {
        if (PatientDetailsRules.Problem(details, ClinicApp.Domain.ClinicClock.Today) is { } bad) return AdminResult<PatientDetails>.Fail(bad);
        try
        {
            var row = await api.GetAsync<Patient>($"/api/patients/{patientId:D}", null, ct);
            if (row is null) return AdminResult<PatientDetails>.Fail("Patient not found.");
            if (PatientDetailsRules.ApplyTo(row, details).Count == 0) return AdminResult<PatientDetails>.Ok(PatientDetailsRules.From(row));
            var saved = await api.SendAsync<Patient>(HttpMethod.Put, $"/api/patients/{patientId:D}", row, null, ct);
            return AdminResult<PatientDetails>.Ok(PatientDetailsRules.From(saved ?? row));
        }
        catch (ApiNotFoundException) { return AdminResult<PatientDetails>.Fail("Patient not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<PatientDetails>.Fail(ex.Message); }
    }
    //JJQR - END

    private sealed class AccessRow
    {
        public DateTimeOffset At { get; set; }
        public string? Who { get; set; }
        public string? Role { get; set; }
        public string? Area { get; set; }
    }

    public async Task<IReadOnlyList<AccessLogEntry>> GetAccessLogAsync(Guid patientId, int take = 200, CancellationToken ct = default) =>
        (await api.GetAsync<List<AccessRow>>($"/api/audit-logs/patient/{patientId:D}/access", ApiAdmin.Q(("take", take)), ct) ?? [])
            .Select(r => new AccessLogEntry(r.At, r.Who ?? "Unknown user", r.Role ?? "", r.Area ?? "")).ToList();

    private sealed class DataRequestRow
    {
        public Guid Id { get; set; }
        public Guid PatientId { get; set; }
        public string? PatientName { get; set; }
        public string? PatientCode { get; set; }
        public string? Kind { get; set; }
        public string? Details { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        public DateOnly? RetainUntil { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
        public string? ResolutionNote { get; set; }

        public DataRequestItem ToItem() => new(Id, PatientId, PatientName ?? "", PatientCode ?? "", Kind ?? "", Details ?? "", Status ?? "",
            RequestedAt, RetainUntil, ResolvedAt, ResolutionNote);
    }

    public async Task<IReadOnlyList<DataRequestItem>> ListDataRequestsAsync(string? status, CancellationToken ct = default) =>
        (await api.GetAsync<List<DataRequestRow>>("/api/privacy/data-requests", ApiAdmin.Q(("status", status)), ct) ?? [])
            .OrderByDescending(r => r.RequestedAt).Select(r => r.ToItem()).ToList();

    public async Task<AdminResult<DataRequestItem>> ResolveDataRequestAsync(Guid id, string status, string? note, CancellationToken ct = default)
    {
        try
        {
            await api.SendAsync(HttpMethod.Put, $"/api/privacy/data-requests/{id:D}", new { status, note = ApiAdmin.Blank(note) }, null, ct);
            var all = await api.GetAsync<List<DataRequestRow>>("/api/privacy/data-requests", null, ct) ?? [];
            var row = all.FirstOrDefault(r => r.Id == id);
            return row is null ? AdminResult<DataRequestItem>.Fail("Request not found.") : AdminResult<DataRequestItem>.Ok(row.ToItem());
        }
        catch (ApiNotFoundException) { return AdminResult<DataRequestItem>.Fail("Request not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<DataRequestItem>.Fail(ex.Message); }
    }
}
//JJQR - END
