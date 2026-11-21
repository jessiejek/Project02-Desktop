//JJQR - AI (21 Nov 2026) - Api implementation of IConsultationLibraryService (same endpoints as the web consultation page):
//   GET|POST /api/soap-templates?doctorId= (the server copies the built-ins first) · GET|POST /api/soap-phrases?doctorId=
//   GET|POST /api/medical-certificate-templates?doctorId= · GET /api/doctor-diagnosis-templates?doctorId= · GET /api/prescription-templates?doctorId=
//   GET /api/medicines · GET /api/lab-test-catalog · GET /api/settings · GET /api/doctors/{id} · GET /api/patients/{id}
//   GET|PUT /api/patients/{id}/allergies
// Patient address update is not converted (owned by the patients lane): ApiNotSupportedException → local fallback (best-effort no-op).
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Doctor;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Practice;

namespace ClinicNode.Api.Services;

public sealed class ApiConsultationLibraryService(IApiClient api) : IConsultationLibraryService
{
    private static Dictionary<string, string?> Doc(Guid doctorId) => new() { ["doctorId"] = doctorId.ToString("D") };

    public async Task<IReadOnlyList<SoapTemplateItem>> ListSoapTemplatesAsync(Guid doctorId, CancellationToken ct = default) =>
        (await api.GetAsync<List<SoapTemplate>>("/api/soap-templates", Doc(doctorId), ct) ?? [])
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new SoapTemplateItem(t.Id, t.Title, t.IsSystemTemplate, t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan, t.Category)).ToList();

    public async Task<IReadOnlyList<SoapPhraseItem>> ListSoapPhrasesAsync(Guid doctorId, CancellationToken ct = default) =>
        (await api.GetAsync<List<SoapPhrase>>("/api/soap-phrases", Doc(doctorId), ct) ?? [])
            .OrderBy(p => p.Field).ThenBy(p => p.SortOrder).ThenBy(p => p.Label)
            .Select(p => new SoapPhraseItem(p.Id, p.Field, p.Label, p.Body)).ToList();

    public async Task<IReadOnlyList<CertTemplateItem>> ListCertTemplatesAsync(Guid doctorId, CancellationToken ct = default) =>
        (await api.GetAsync<List<MedicalCertificateTemplate>>("/api/medical-certificate-templates", Doc(doctorId), ct) ?? [])
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new CertTemplateItem(t.Id, t.Title, t.IsSystemTemplate, t.DiagnosisText, t.Recommendations, t.PurposeException)).ToList();

    public async Task<IReadOnlyList<DiagnosisTemplateItem>> ListDiagnosisTemplatesAsync(Guid doctorId, CancellationToken ct = default) =>
        (await api.GetAsync<List<DoctorDiagnosisTemplate>>("/api/doctor-diagnosis-templates", Doc(doctorId), ct) ?? [])
            .OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase)
            .Select(t => new DiagnosisTemplateItem(t.Id, t.Label, t.Body, t.Icd10Code)).ToList();

    public async Task<IReadOnlyList<RxTemplateItem>> ListRxTemplatesAsync(Guid doctorId, CancellationToken ct = default) =>
        (await api.GetAsync<List<PrescriptionTemplate>>("/api/prescription-templates", Doc(doctorId), ct) ?? [])
            .OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
            .Select(t => new RxTemplateItem(t.TemplateId, t.Title, t.IsSystemTemplate, t.Items.OrderBy(i => i.GenericName).Select(i => new RxLine
            {
                MedicineId = i.MedicineId == Guid.Empty ? null : i.MedicineId, GenericName = i.GenericName, Dosage = i.Dosage, Quantity = i.Quantity,
                Instruction = i.Instruction, Timing = i.Timing, MealRelation = i.MealRelation, DurationKind = i.DurationKind, DurationValue = i.DurationValue,
                Indication = i.Indication, Route = i.Route, AsNeeded = i.AsNeeded, IsControlledSubstance = i.IsControlledSubstance,
            }).ToList())).ToList();

    public async Task<IReadOnlyList<MedicineHit>> ListMedicinesAsync(CancellationToken ct = default) =>
        (await api.GetAsync<List<Medicine>>("/api/medicines", null, ct) ?? []).Select(m => new MedicineHit(m.MedicineId, m.GenericName)).ToList();

    public async Task<IReadOnlyList<LabTestItem>> ListLabTestsAsync(CancellationToken ct = default) =>
        (await api.GetAsync<List<LabTestCatalog>>("/api/lab-test-catalog", null, ct) ?? [])
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).Select(t => new LabTestItem(t.LabTestId, t.Name, t.IsDefault, t.SortOrder)).ToList();

    public async Task<ClinicSetting> GetClinicSettingsAsync(CancellationToken ct = default)
    {
        try { return await api.GetAsync<ClinicSetting>("/api/settings", null, ct) ?? new ClinicSetting(); }
        catch (ApiNotFoundException) { return new ClinicSetting(); }
    }

    public async Task<DoctorCredentials?> GetDoctorCredentialsAsync(Guid doctorId, CancellationToken ct = default)
    {
        try
        {
            var d = await api.GetAsync<ClinicApp.Domain.Entities.Doctor>($"/api/doctors/{doctorId:D}", null, ct); //JJQR - AI (21 Nov 2026) - qualified: bare Doctor collides with the ClinicNode.Core.Doctor namespace //JJQR - END
            return d is null ? null : new DoctorCredentials(d.StaffAccount?.FullName ?? "", d.LicenseNumber, d.PtrNumber);
        }
        catch (ApiNotFoundException) { return null; }
    }

    public async Task<PatientAddress?> GetPatientAddressAsync(Guid patientId, CancellationToken ct = default)
    {
        try
        {
            var p = await api.GetAsync<Patient>($"/api/patients/{patientId:D}", null, ct);
            return p is null ? null : new PatientAddress(p.Address, p.City);
        }
        catch (ApiNotFoundException) { return null; }
    }

    public async Task<IReadOnlyList<PatientAllergy>> ListAllergiesAsync(Guid patientId, CancellationToken ct = default) =>
        await api.GetAsync<List<PatientAllergy>>($"/api/patients/{patientId:D}/allergies", null, ct) ?? [];

    public async Task<SoapTemplateItem> SaveSoapTemplateAsync(Guid doctorId, string title, SoapValues v, CancellationToken ct = default)
    {
        var t = await api.SendAsync<SoapTemplate>(HttpMethod.Post, "/api/soap-templates", new
        {
            Title = title.Trim(), IsSystemTemplate = false, ChiefComplaint = Nb(v.ChiefComplaint), Subjective = Nb(v.Subjective),
            Objective = Nb(v.Objective), Assessment = Nb(v.Assessment), Plan = Nb(v.Plan),
        }, Doc(doctorId), ct) ?? throw new ApiServerException(500, "The server did not return the template.");
        return new SoapTemplateItem(t.Id, t.Title, false, t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan);
    }

    public async Task<SoapPhraseItem> SaveSoapPhraseAsync(Guid doctorId, SoapField field, string label, string body, CancellationToken ct = default)
    {
        var p = await api.SendAsync<SoapPhrase>(HttpMethod.Post, "/api/soap-phrases", new { Field = field, Label = label.Trim(), Body = body.Trim() }, Doc(doctorId), ct)
                ?? throw new ApiServerException(500, "The server did not return the phrase.");
        return new SoapPhraseItem(p.Id, p.Field, p.Label, p.Body);
    }

    public async Task<CertTemplateItem> SaveCertTemplateAsync(Guid doctorId, string title, string? diagnosisText, string? recommendations, string? purposeException, CancellationToken ct = default)
    {
        var t = await api.SendAsync<MedicalCertificateTemplate>(HttpMethod.Post, "/api/medical-certificate-templates", new
        {
            Title = title.Trim(), IsSystemTemplate = false, DiagnosisText = Nb(diagnosisText), Recommendations = Nb(recommendations), PurposeException = Nb(purposeException),
        }, Doc(doctorId), ct) ?? throw new ApiServerException(500, "The server did not return the template.");
        return new CertTemplateItem(t.Id, t.Title, false, t.DiagnosisText, t.Recommendations, t.PurposeException);
    }

    public async Task<IReadOnlyList<PatientAllergy>?> ReplaceAllergiesAsync(Guid patientId, IReadOnlyList<AllergyInput> allergies, CancellationToken ct = default)
    {
        var nkda = allergies.Any(a => a.IsNkda);
        object body = nkda
            ? new[] { new { Allergen = "NKDA", Reaction = (string?)null, Severity = (string?)null, IsNkda = true, Id = (Guid?)null } }
            : allergies.Select(a => new { Allergen = a.Allergen.Trim(), Reaction = Nb(a.Reaction), Severity = Nb(a.Severity), IsNkda = false, Id = a.Id }).ToArray();
        return await api.SendAsync<List<PatientAllergy>>(HttpMethod.Put, $"/api/patients/{patientId:D}/allergies", body, null, ct);
    }

    //JJQR - AI (21 Nov 2026) - 1:1 with the web consult (page.tsx ~2089: persist a typed address when none was on file) via the web's
    // updatePatient (lib/data/patients.ts): GET /api/patients/{id} → drop PATIENT_PUT_READONLY_KEYS → set address → PUT /api/patients/{id}.
    // Same route/body shape the web sends; no new endpoint. Server refusals return false (best-effort, the certificate is saved regardless).
    private static readonly string[] PatientPutReadonlyKeys =
    [
        "patient_allergies", "allergies", "patient_id", "user_id", "patient_code", "created_at", "updated_at",
        "is_guest", "is_email_verified", "consented_at", "consent_version", "email",
    ];

    public async Task<bool> UpdatePatientAddressAsync(Guid patientId, string address, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        try
        {
            var current = await api.GetAsync<System.Text.Json.Nodes.JsonObject>($"/api/patients/{patientId:D}", null, ct);
            if (current is null) return false;
            //JJQR - AI (21 Nov 2026) - Jessie (E1): write ONLY when the profile has no address; never overwrite one already on file
            // (re-checked on the fresh GET so an address staff added since the page loaded is kept)
            var onFile = current["address"] is System.Text.Json.Nodes.JsonValue v && v.TryGetValue<string>(out var a) ? a : null;
            if (!string.IsNullOrWhiteSpace(onFile)) return false;
            //JJQR - END
            foreach (var key in PatientPutReadonlyKeys) current.Remove(key);
            current["address"] = address.Trim();
            await api.SendAsync(HttpMethod.Put, $"/api/patients/{patientId:D}", current, null, ct);
            return true;
        }
        //JJQR - AI (21 Nov 2026) - best-effort: any profile-write failure returns false, never fails the certificate save
        catch (Exception ex) when (ex is not OperationCanceledException) { return false; }
        //JJQR - END
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - GET|POST /api/doctor-favorite-medicines?doctorId= · GET /api/patient-vaccinations?patientId= · GET /api/patients/{id}
    // · GET /api/audit-logs?entityType=Consultation&entityId=&take=100 (the web Amendment History source)
    public async Task<IReadOnlyList<FavoriteMedicineItem>> ListFavoriteMedicinesAsync(Guid doctorId, CancellationToken ct = default) =>
        (await api.GetAsync<List<DoctorFavoriteMedicine>>("/api/doctor-favorite-medicines", Doc(doctorId), ct) ?? [])
            .Select(ToItem).ToList();

    public async Task<FavoriteMedicineItem> AddFavoriteMedicineAsync(Guid doctorId, FavoriteMedicineItem item, CancellationToken ct = default)
    {
        var f = await api.SendAsync<DoctorFavoriteMedicine>(HttpMethod.Post, "/api/doctor-favorite-medicines", new
        {
            MedicineId = item.MedicineId ?? Guid.Empty, GenericName = item.GenericName.Trim(), Dosage = item.Dosage ?? "", Quantity = item.Quantity ?? "", Instruction = Nb(item.Instruction),
        }, Doc(doctorId), ct) ?? throw new ApiServerException(500, "The server did not return the favorite.");
        return ToItem(f);
    }

    public async Task<IReadOnlyList<PatientVaccination>> ListPatientVaccinationsAsync(Guid patientId, CancellationToken ct = default) =>
        await api.GetAsync<List<PatientVaccination>>("/api/patient-vaccinations", new Dictionary<string, string?> { ["patientId"] = patientId.ToString("D") }, ct) ?? [];

    //JJQR - AI (21 Nov 2026) - ALL-1: web queryBookingById → GET /api/bookings/{id} (embeds payments) → booking.paymentStatus
    public async Task<string?> GetBookingPaymentDisplayAsync(Guid bookingId, CancellationToken ct = default) =>
        await api.GetAsync<Booking>($"/api/bookings/{bookingId:D}", null, ct) is { } b ? ConsultPaymentDisplay.For(b.Status, b.Payment?.Status) : null;
    //JJQR - END

    public async Task<PatientClinicalFlags?> GetPatientClinicalFlagsAsync(Guid patientId, CancellationToken ct = default) =>
        //JJQR - AI (21 Nov 2026) - + renal fields for the renal cue
        await api.GetAsync<Patient>($"/api/patients/{patientId:D}", null, ct) is { } p ? new PatientClinicalFlags(p.IsPregnant == true, p.LastCreatinineMgDl, p.LastEgfr, p.LastRenalLabsDate,
            p.LmpDate, p.Gravida, p.Para, p.RhFactor, p.GuardianName, p.GuardianRelationship) : null; //JJQR - AI (21 Nov 2026) - ALL-1 banner chips (same GET /api/patients/{id} as the web patientDetail) //JJQR - END
        //JJQR - END

    //JJQR - AI (21 Nov 2026) - Refill last visit Rx / SOAP specialty default (existing routes only)
    public async Task<ConsultPriorVisit?> GetPriorVisitAsync(Guid patientId, Guid currentBookingId, CancellationToken ct = default)
    {
        var consults = await api.GetAsync<List<Consultation>>("/api/consultations", new Dictionary<string, string?> { ["patientId"] = patientId.ToString("D") }, ct) ?? [];
        var prior = consults.Where(c => c.BookingId != currentBookingId && c.Booking is not null)
            .OrderByDescending(c => c.Booking!.AppointmentDate).FirstOrDefault();
        if (prior is null) return null;
        var groups = await api.GetAsync<List<PrescriptionGroup>>("/api/prescription-groups", new Dictionary<string, string?> { ["bookingId"] = prior.BookingId.ToString("D") }, ct) ?? [];
        var items = groups.FirstOrDefault()?.LineItems.OrderBy(i => i.CreatedAt).Select(ToRxLine).ToList() ?? [];
        return new ConsultPriorVisit(prior.ConsultationId, prior.BookingId, prior.Booking!.AppointmentDate, items);
    }

    public async Task<string?> GetDoctorSpecializationAsync(Guid doctorId, CancellationToken ct = default) =>
        (await api.GetAsync<ClinicApp.Domain.Entities.Doctor>($"/api/doctors/{doctorId:D}", null, ct))?.Specialization;

    internal static RxLine ToRxLine(PrescriptionLineItem i) => new()
    {
        MedicineId = i.MedicineId == Guid.Empty ? null : i.MedicineId, GenericName = i.GenericName, Dosage = i.Dosage, Quantity = i.Quantity,
        Instruction = i.Instruction, Timing = i.Timing, MealRelation = i.MealRelation, DurationKind = i.DurationKind, DurationValue = i.DurationValue,
        Indication = i.Indication, Route = i.Route, AsNeeded = i.AsNeeded, IsControlledSubstance = i.IsControlledSubstance,
    };
    //JJQR - END

    public async Task<IReadOnlyList<AmendmentEntry>> ListAmendmentHistoryAsync(Guid consultationId, CancellationToken ct = default) =>
        (await api.GetAsync<List<AuditLog>>("/api/audit-logs", new Dictionary<string, string?>
        {
            ["entityType"] = nameof(AuditEntityType.Consultation), ["entityId"] = consultationId.ToString("D"), ["take"] = "100",
        }, ct) ?? []).OrderByDescending(a => a.PerformedAt).Select(a => new AmendmentEntry(a.PerformedAt, a.Details)).ToList();

    private static FavoriteMedicineItem ToItem(DoctorFavoriteMedicine f) =>
        new(f.Id, f.MedicineId == Guid.Empty ? null : f.MedicineId, f.GenericName, f.Dosage, f.Quantity, f.Instruction);
    //JJQR - END

    private static string? Nb(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
//JJQR - END
