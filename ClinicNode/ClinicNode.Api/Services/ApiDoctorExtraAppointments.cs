//JJQR - AI (21 Nov 2026) - Api implementations of IDoctorAppointmentsService and IDoctorPatientsService (same endpoints as the web):
//   list   : GET /api/bookings/doctor/today (Today) | GET /api/bookings?doctorId= (All)  - web queryDoctorBookings
//   detail : GET /api/bookings/{id}, GET /api/consultations/by-booking/{id}, GET /api/consultations?patientId=, GET /api/prescription-groups?patientId=
//   chart  : GET /api/patients/{id}, /api/bookings?patientId=&doctorId=, /api/consultations?patientId=&doctorId=, /api/vital-field-templates,
//            /api/vitals?patientId=, /api/prescription-groups?patientId=&doctorId=, /api/patient-lab-results?patientId=, /api/patient-documents?patientId=,
//            /api/patient-vaccinations?patientId=
// The server already limits a Doctor to their own bookings. Shaping and ordering reuse DoctorExtraRules (same as the Local services).
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.DoctorExtra;
using static ClinicNode.Api.Services.ApiDoctorExtra;

namespace ClinicNode.Api.Services;

public sealed class ApiDoctorAppointmentsService(IApiClient api) : IDoctorAppointmentsService
{
    public async Task<IReadOnlyList<AppointmentRow>> ListAsync(Guid doctorId, AppointmentScope scope, CancellationToken ct = default)
    {
        var rows = scope == AppointmentScope.Today
            ? await api.GetAsync<List<BookingDto>>("/api/bookings/doctor/today", null, ct)
            : await api.GetAsync<List<BookingDto>>("/api/bookings", Q(("doctorId", Id(doctorId))), ct);
        return DoctorExtraRules.Order((rows ?? []).Where(b => b.DoctorId == doctorId).Select(b => new AppointmentRow(
            b.BookingId, DoctorExtraRules.PatientName(b.Patients?.FirstName, b.Patients?.LastName), b.Patients?.PatientCode,
            b.AppointmentDate, b.QueueNumber, b.Status, DoctorExtraRules.ListPaymentLabel(b.Status, b.Payments?.Status),
            DoctorExtraRules.VisitTypeLabel(b.VisitType ?? VisitType.New))), scope);
    }

    public async Task<AppointmentDetail?> GetAsync(Guid doctorId, Guid bookingId, CancellationToken ct = default)
    {
        BookingDto? b;
        try { b = await api.GetAsync<BookingDto>($"/api/bookings/{Id(bookingId)}", null, ct); }
        catch (ApiNotFoundException) { return null; }
        catch (ApiForbiddenException) { return null; }
        if (b is null || b.DoctorId != doctorId) return null;
        ConsultationDto? consult = null;
        try { consult = await api.GetAsync<ConsultationDto>($"/api/consultations/by-booking/{Id(bookingId)}", null, ct); }
        catch (ApiNotFoundException) { }
        var prior = await PreviousAsync(api, b.PatientId, bookingId, ct);
        var slot = (b.SlotStartTime ?? TimeOnly.MinValue).ToString("HH:mm");
        return new AppointmentDetail(
            b.BookingId, b.PatientId,
            b.Patients is { } p ? $"{p.FirstName} {p.LastName}" : "Unknown patient",
            b.Patients?.ContactNumber ?? "", b.Patients?.Email ?? "",
            b.AppointmentDate, slot == "00:00" ? "" : slot, b.Status,
            DoctorExtraRules.DetailPaymentLabel(b.Status, b.Payments?.Status), b.QueueNumber, b.TotalFee ?? 0m, b.AmountDue ?? 0m,
            consult?.ChiefComplaint, consult?.Assessment, consult?.Plan, consult is not null, prior);
    }

    /// <summary>Same shaping as DoctorAppointmentsService.PreviousAsync (web: consultations of the patient minus this booking, newest first).</summary>
    internal static async Task<IReadOnlyList<PreviousConsultation>> PreviousAsync(IApiClient api, Guid patientId, Guid? excludeBookingId, CancellationToken ct)
    {
        var consults = (await Optional(() => api.GetAsync<List<ConsultationDto>>("/api/consultations", Q(("patientId", Id(patientId))), ct), null) ?? [])
            .Where(c => excludeBookingId == null || c.BookingId != excludeBookingId).ToList();
        if (consults.Count == 0) return [];
        var rx = await Optional(() => api.GetAsync<List<RxGroupDto>>("/api/prescription-groups", Q(("patientId", Id(patientId))), ct), null) ?? [];
        return consults.Select(c =>
        {
            var date = c.Bookings?.AppointmentDate ?? ClinicClock.DateOf(c.CreatedAt ?? DateTimeOffset.UtcNow);
            var summary = string.Join(" · ", new[] { c.ChiefComplaint, c.Assessment }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var names = rx.Where(g => g.BookingId == c.BookingId).SelectMany(g => g.PrescriptionLineItems ?? []).Select(i => i.GenericName ?? "").ToList();
            return new PreviousConsultation(c.ConsultationId, c.BookingId, date,
                c.Doctors?.StaffAccounts?.FullName ?? "—",
                summary.Length > 0 ? summary : "—",
                (c.ConsultationDiagnoses ?? []).Select(x => DoctorExtraRules.DiagnosisLabel(x.Icd10Code, x.CustomDescription)).Where(x => x.Length > 0).ToList(),
                DoctorExtraRules.ShortRxSummary(names), c.FollowUps?.FollowUpDate, c.FollowUps?.Instructions);
        }).OrderByDescending(x => x.Date).ToList();
    }
}

public sealed class ApiDoctorPatientsService(IApiClient api) : IDoctorPatientsService
{
    sealed record PatientDto(Guid PatientId, string? FirstName, string? LastName, string? PatientCode, SexType Sex, DateOnly DateOfBirth, string? ContactNumber);
    sealed record VitalTemplateDto(Guid TemplateId, string? Description, string? Unit, bool IsDefault, DateTimeOffset? CreatedAt);
    sealed record VitalReadingDto(Guid BookingId, Guid TemplateId, string? Value, DateOnly RecordedAt, DateTimeOffset? UpdatedAt);
    sealed record LabResultDto(Guid Id, string? ResultTitle, DateTimeOffset UploadedAt);
    sealed record DocumentDto(Guid Id, string? Title, string? FileName, DateTimeOffset UploadedAt);
    sealed record LabOrderDto(Guid ConsultationId, string? TestName);
    sealed record FollowUpRowDto(Guid ConsultationId, DateOnly? FollowUpDate, string? Reason, string? Instructions);
    sealed record CertRowDto(Guid ConsultationId, string? DiagnosisText, DateOnly? IssueDate);
    sealed record VaccinationDto(Guid Id, string? VaccineName, short? DoseNumber, DateOnly? AdministeredDate, DateOnly? NextDoseDate, VaccinationStatus Status);

    public async Task<IReadOnlyList<DoctorPatientRow>> ListAsync(Guid doctorId, CancellationToken ct = default)
    {
        var bookings = await api.GetAsync<List<BookingDto>>("/api/bookings", Q(("doctorId", Id(doctorId))), ct) ?? [];
        // Newest appointment first (then newest created), one row per patient - doctor/patients/page.tsx.
        var byPatient = new Dictionary<Guid, DoctorPatientRow>();
        foreach (var b in bookings.Where(b => b.DoctorId == doctorId).OrderByDescending(b => b.AppointmentDate).ThenByDescending(b => b.CreatedAt))
        {
            if (b.Patients is null || byPatient.ContainsKey(b.PatientId)) continue;
            byPatient[b.PatientId] = new DoctorPatientRow(b.PatientId, DoctorExtraRules.PatientName(b.Patients.FirstName, b.Patients.LastName),
                b.Patients.PatientCode ?? "", b.AppointmentDate, b.BookingId,
                b.Patients.Sex?.ToString(), b.Patients.DateOfBirth is { } dob ? DoctorExtraRules.AgeOn(dob, ClinicClock.Today) : null, b.Patients.ContactNumber,
                bookings.Count(x => x.DoctorId == doctorId && x.PatientId == b.PatientId));
        }
        return byPatient.Values.ToList();
    }

    /// <summary>Synchronous: always served by the Local service (same filter).</summary>
    public IReadOnlyList<DoctorPatientRow> Filter(IEnumerable<DoctorPatientRow> rows, string? search)
    {
        var q = (search ?? "").Trim().ToLowerInvariant();
        return rows.Where(r => $"{r.FullName} {r.PatientCode}".ToLowerInvariant().Contains(q)).ToList();
    }

    public async Task<PatientChart?> GetChartAsync(Guid doctorId, Guid patientId, CancellationToken ct = default)
    {
        //JJQR - AI (Nov 22, 2026) - owner: the chart was blank for seconds. It asked the server 12 questions one after another; they do not depend on each other, so all are sent AT THE SAME TIME (one wait instead of twelve).
        var pid = Id(patientId);
        var tPatient = api.GetAsync<PatientDto>($"/api/patients/{pid}", null, ct);
        var tBookings = api.GetAsync<List<BookingDto>>("/api/bookings", Q(("patientId", pid), ("doctorId", Id(doctorId))), ct);
        var tConsults = Optional(() => api.GetAsync<List<ConsultationDto>>("/api/consultations", Q(("patientId", pid), ("doctorId", Id(doctorId))), ct), null);
        var tTemplates = Optional(() => api.GetAsync<List<VitalTemplateDto>>("/api/vital-field-templates", null, ct), null);
        var tReadings = Optional(() => api.GetAsync<List<VitalReadingDto>>("/api/vitals", Q(("patientId", pid)), ct), null);
        var tGroups = Optional(() => api.GetAsync<List<RxGroupDto>>("/api/prescription-groups", Q(("patientId", pid), ("doctorId", Id(doctorId))), ct), null);
        var tLabs = Optional(() => api.GetAsync<List<LabResultDto>>("/api/patient-lab-results", Q(("patientId", pid)), ct), null);
        var tDocs = Optional(() => api.GetAsync<List<DocumentDto>>("/api/patient-documents", Q(("patientId", pid)), ct), null);
        var tVax = Optional(() => api.GetAsync<List<VaccinationDto>>("/api/patient-vaccinations", Q(("patientId", pid)), ct), null);
        var tLabOrders = Optional(() => api.GetAsync<List<LabOrderDto>>("/api/lab-orders", Q(("patientId", pid)), ct), null);
        var tFollowUps = Optional(() => api.GetAsync<List<FollowUpRowDto>>("/api/follow-ups", Q(("patientId", pid)), ct), null);
        var tCerts = Optional(() => api.GetAsync<List<CertRowDto>>("/api/medical-certificates", Q(("patientId", pid)), ct), null);
        PatientDto? p;
        try { p = await tPatient; }
        catch (ApiNotFoundException) { _ = Task.WhenAll(tBookings, tConsults, tTemplates, tReadings, tGroups, tLabs, tDocs, tVax, tLabOrders, tFollowUps, tCerts).ContinueWith(t => _ = t.Exception, TaskScheduler.Default); return null; }
        if (p is null) return null;
        //JJQR - END
        var bookings = (await tBookings ?? [])
            .Where(b => b.DoctorId == doctorId).ToList();
        var chartBookings = bookings.OrderByDescending(b => b.AppointmentDate).ThenByDescending(b => b.CreatedAt)
            .Select(b => new ChartBooking(b.BookingId, b.AppointmentDate,
                (b.BookingServices ?? []).Select(s => s.Services?.Name ?? "").Where(n => n.Length > 0).ToList(), b.Status, b.QueueNumber)).ToList();
        var dates = bookings.ToDictionary(b => b.BookingId, b => b.AppointmentDate);

        var consults = (await tConsults ?? [])
            .Where(c => c.DoctorId == doctorId).ToList();
        var chartConsults = consults.Select(c => new ChartConsultation(c.ConsultationId, c.BookingId,
                c.Bookings?.AppointmentDate ?? (dates.TryGetValue(c.BookingId, out var d) ? d : ClinicClock.DateOf(c.CreatedAt ?? DateTimeOffset.UtcNow)), c.Status,
                c.ChiefComplaint, c.Subjective, c.Objective, c.Assessment, c.Plan,
                (c.ConsultationDiagnoses ?? []).Select(x => x.CustomDescription ?? "").Where(x => x.Length > 0).ToList()))
            .OrderByDescending(c => c.AppointmentDate).ToList();

        var templates = (await tTemplates ?? [])
            .OrderBy(t => t.CreatedAt).ToList();
        var readings = await tReadings ?? [];
        var visits = readings.GroupBy(r => r.BookingId)
            .Select(g => new ChartVitalVisit(g.Key, dates.TryGetValue(g.Key, out var vd) ? vd : g.Max(r => r.RecordedAt),
                g.GroupBy(r => r.TemplateId).ToDictionary(x => x.Key, x => x.OrderByDescending(r => r.UpdatedAt).First().Value ?? "")))
            .OrderByDescending(v => v.Date).ToList();

        var groups = (await tGroups ?? [])
            .Where(g => g.DoctorId == doctorId).ToList();
        var rx = groups.OrderByDescending(g => g.CreatedAt).Select(g => new ChartRxGroup(g.GroupId, g.BookingId, g.CreatedAt,
            (g.PrescriptionLineItems ?? []).Select(i => new ChartRxLine(i.GenericName ?? "", i.Dosage ?? "", i.Quantity ?? "", i.Instruction)).ToList())).ToList();

        var labs = await tLabs ?? [];
        var docs = await tDocs ?? [];
        var vax = await tVax ?? [];

        //JJQR - AI (Nov 21, 2026) - per-visit extras: lab orders, follow-up and certificate (each optional: the chart still loads without them)
        var labOrders = await tLabOrders ?? [];
        var followUps = await tFollowUps ?? [];
        var certs = await tCerts ?? [];
        var extras = bookings.Select(b =>
        {
            var cid = consults.FirstOrDefault(c => c.BookingId == b.BookingId)?.ConsultationId ?? Guid.Empty;
            var fu = cid == Guid.Empty ? null : followUps.FirstOrDefault(f => f.ConsultationId == cid);
            var mc = cid == Guid.Empty ? null : certs.FirstOrDefault(m => m.ConsultationId == cid);
            return new ChartVisitExtras(b.BookingId, (b.VisitType ?? VisitType.New).ToString(), b.DiscountCategory, b.TotalFee, b.MedCertRequested ?? false,
                cid == Guid.Empty ? [] : labOrders.Where(l => l.ConsultationId == cid).Select(l => l.TestName ?? "").Where(n => n.Length > 0).ToList(),
                fu?.FollowUpDate, fu?.Reason, fu?.Instructions, mc is not null, mc?.DiagnosisText, mc?.IssueDate);
        }).ToList();
        //JJQR - END
        return new PatientChart(
            new ChartPatient(p.PatientId, $"{p.FirstName} {p.LastName}".Trim(), p.PatientCode ?? "", p.Sex, p.DateOfBirth, DoctorExtraRules.AgeOn(p.DateOfBirth, ClinicClock.Today), p.ContactNumber),
            chartBookings, chartConsults,
            templates.Select(t => new ChartVitalTemplate(t.TemplateId, t.Description ?? "", t.Unit ?? "", t.IsDefault)).ToList(),
            visits, rx,
            labs.OrderByDescending(l => l.UploadedAt).Select(l => new ChartFile(l.Id, string.IsNullOrWhiteSpace(l.ResultTitle) ? "Lab result" : l.ResultTitle!, l.UploadedAt)).ToList(),
            docs.OrderByDescending(d => d.UploadedAt).Select(d => new ChartFile(d.Id, string.IsNullOrWhiteSpace(d.Title) ? d.FileName ?? "" : d.Title!, d.UploadedAt)).ToList(),
            vax.OrderByDescending(v => v.AdministeredDate ?? v.NextDoseDate).Select(v => new ChartVaccination(v.Id, v.VaccineName ?? "", v.DoseNumber, v.AdministeredDate ?? v.NextDoseDate, v.Status)).ToList(),
            extras);
    }
}
//JJQR - END
