//JJQR - AI (21 Nov 2026) - Api implementations for the chart prescriptions and the doctor printouts (the templates workspace service moved out: allen1 owns that screen;
// ApiTplWire keeps the template wire DTOs because the prescription form reads favorites / Rx sets / medicines too) (same endpoints as the web,
// Project02-fe lib/data/clinical.ts + lookups.ts; backend Project02-be Soap/Prescriptions/MedicalCertificateTemplates/Templates controllers):
//   GET|POST /api/soap-templates?doctorId, PUT|DELETE /api/soap-templates/{id}
//   GET|POST /api/soap-phrases?doctorId, PUT|DELETE /api/soap-phrases/{id}, PUT /api/soap-phrases/order?doctorId { ids }
//   GET /api/prescription-templates?doctorId, POST /api/prescription-templates, PUT|DELETE /api/prescription-templates/{id}
//   GET|POST /api/doctor-favorite-medicines?doctorId, PUT|DELETE /api/doctor-favorite-medicines/{id}, PUT /api/doctor-favorite-medicines/order?doctorId { ids }
//   GET|POST /api/medical-certificate-templates?doctorId, PUT|DELETE /api/medical-certificate-templates/{id}
//   POST /api/templates/restore-built-ins?doctorId=&kind=   (the built-ins come from the backend catalog; nothing is hard-coded here)
//   GET /api/medicines
//   GET /api/prescription-groups/{id}, PUT /api/prescription-groups/by-booking/{bookingId}, DELETE /api/prescription-groups/{id}
//   GET /api/patients/{id}/card, GET /api/bookings/{id}, GET /api/prescription-groups?bookingId=, GET /api/lab-orders/by-booking/{id}
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Printing;
using static ClinicNode.Api.Services.ApiDoctorExtra;

namespace ClinicNode.Api.Services;

internal static class ApiTplWire
{
    internal sealed record SoapDto(Guid Id, string? Title, bool IsSystemTemplate, string? ChiefComplaint, string? Subjective, string? Objective, string? Assessment, string? Plan);
    internal sealed record PhraseDto(Guid Id, SoapField Field, string? Label, string? Body, int SortOrder);
    internal sealed record RxTemplateDto(Guid TemplateId, string? Title, bool IsSystemTemplate, List<RxItemDto>? PrescriptionTemplateItems);
    internal sealed record FavoriteDto(Guid Id, Guid MedicineId, string? GenericName, string? Dosage, string? Quantity, string? Instruction, int SortOrder);
    internal sealed record CertDto(Guid Id, string? Title, bool IsSystemTemplate, string? DiagnosisText, string? Recommendations, string? PurposeException);
    internal sealed record MedicineDto(Guid MedicineId, string? GenericName);
    internal sealed record RestoreDto(int Restored);

    internal static TplRxSet Set(RxTemplateDto t) => new(t.TemplateId, t.Title ?? "", t.IsSystemTemplate, (t.PrescriptionTemplateItems ?? []).Select(Line).ToList());
    internal static TplFavorite Fav(FavoriteDto f) => new(f.Id, f.MedicineId, f.GenericName ?? "", f.Dosage ?? "", f.Quantity ?? "", f.Instruction, f.SortOrder);

    internal static async Task<IReadOnlyList<TplMedicine>> MedicinesAsync(IApiClient api, CancellationToken ct) =>
        (await api.GetAsync<List<MedicineDto>>("/api/medicines", null, ct) ?? [])
            .Select(m => new TplMedicine(m.MedicineId, m.GenericName ?? "")).OrderBy(m => m.GenericName, StringComparer.CurrentCulture).ToList();

    internal static async Task<IReadOnlyList<TplFavorite>> FavoritesAsync(IApiClient api, Guid doctorId, CancellationToken ct) =>
        (await api.GetAsync<List<FavoriteDto>>("/api/doctor-favorite-medicines", Q(("doctorId", Id(doctorId))), ct) ?? [])
            .OrderBy(f => f.SortOrder).ThenBy(f => f.GenericName, StringComparer.CurrentCulture).Select(Fav).ToList();

    internal static async Task<IReadOnlyList<TplRxSet>> RxSetsAsync(IApiClient api, Guid doctorId, CancellationToken ct) =>
        (await api.GetAsync<List<RxTemplateDto>>("/api/prescription-templates", Q(("doctorId", Id(doctorId))), ct) ?? [])
            .Select(Set).OrderBy(t => t.Title, StringComparer.CurrentCulture).ToList();

    internal static object FavoriteBody(Guid medicineId, string generic, string dosage, string quantity, string? instruction) => new
    {
        medicine_id = medicineId,
        generic_name = generic.Trim(),
        dosage = dosage.Trim(),
        quantity = quantity.Trim(),
        instruction = string.IsNullOrWhiteSpace(instruction) ? null : instruction.Trim(),
    };

    internal static object RxTemplateBody(Guid doctorId, string title, IReadOnlyList<TplRxLine> lines) => new
    {
        doctor_id = doctorId,
        title = title.Trim(),
        is_system_template = false,
        items = lines.Where(l => !string.IsNullOrWhiteSpace(l.GenericName)).Select(Item).ToList(),
    };
}

public sealed class ApiDoctorPrescriptionsService(IApiClient api) : IDoctorPrescriptionsService
{
    public async Task<RxGroupDetail?> GetGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        RxGroupDto? g;
        try { g = await api.GetAsync<RxGroupDto>($"/api/prescription-groups/{Id(groupId)}", null, ct); }
        catch (ApiNotFoundException) { return null; }
        return g is null ? null : new RxGroupDetail(g.GroupId, g.PatientId, g.DoctorId, g.BookingId, g.CreatedAt, (g.PrescriptionLineItems ?? []).Select(Line).ToList());
    }

    public Task<DoctorResult> SaveByBookingAsync(Guid bookingId, Guid patientId, Guid doctorId, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default) =>
        Write(async () => (await api.SendAsync<RxGroupDto>(HttpMethod.Put, $"/api/prescription-groups/by-booking/{Id(bookingId)}", new
        {
            patient_id = patientId,
            doctor_id = doctorId,
            booking_id = bookingId,
            items = lines.Select(Item).ToList(),
        }, null, ct))?.GroupId, "Could not save the prescription. Try again.");

    public Task<DoctorResult> DeleteGroupAsync(Guid groupId, CancellationToken ct = default) =>
        Write(() => api.SendAsync(HttpMethod.Delete, $"/api/prescription-groups/{Id(groupId)}", null, null, ct), groupId, "The prescription could not be deleted. Try again.");

    public Task<DoctorResult> AddFavoriteAsync(Guid doctorId, TplRxLine line, CancellationToken ct = default) =>
        Write(async () => (await api.SendAsync<ApiTplWire.FavoriteDto>(HttpMethod.Post, "/api/doctor-favorite-medicines",
            ApiTplWire.FavoriteBody(line.MedicineId, line.GenericName, line.Dosage, line.Quantity, line.Instruction), Q(("doctorId", Id(doctorId))), ct))?.Id,
            "Favorite was not saved.");

    public Task<DoctorResult> CreateRxSetAsync(Guid doctorId, string title, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default) =>
        Write(async () => (await api.SendAsync<ApiTplWire.RxTemplateDto>(HttpMethod.Post, "/api/prescription-templates",
            ApiTplWire.RxTemplateBody(doctorId, title, lines), null, ct))?.TemplateId, "The template was not saved. Your edits are still here, please try again.");

    public Task<IReadOnlyList<TplFavorite>> ListFavoritesAsync(Guid doctorId, CancellationToken ct = default) => ApiTplWire.FavoritesAsync(api, doctorId, ct);
    public Task<IReadOnlyList<TplRxSet>> ListRxSetsAsync(Guid doctorId, CancellationToken ct = default) => ApiTplWire.RxSetsAsync(api, doctorId, ct);
    public Task<IReadOnlyList<TplMedicine>> ListMedicinesAsync(CancellationToken ct = default) => ApiTplWire.MedicinesAsync(api, ct);

    //JJQR - AI (21 Nov 2026) - web PrescriptionForm TemplateModal / "Delete Template?": PUT|DELETE /api/prescription-templates/{id};
    // allergies + pregnancy: GET /api/patients/{id}/allergies and GET /api/patients/{id} (is_pregnant)
    public Task<DoctorResult> UpdateRxSetAsync(Guid templateId, Guid doctorId, string title, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default) =>
        Write(async () => (await api.SendAsync<ApiTplWire.RxTemplateDto>(HttpMethod.Put, $"/api/prescription-templates/{Id(templateId)}",
            ApiTplWire.RxTemplateBody(doctorId, title, lines), null, ct))?.TemplateId ?? templateId,
            "The template was not saved. Your edits are still here, please try again.");

    public Task<DoctorResult> DeleteRxSetAsync(Guid templateId, CancellationToken ct = default) =>
        Write(() => api.SendAsync(HttpMethod.Delete, $"/api/prescription-templates/{Id(templateId)}", null, null, ct), templateId,
            "The template could not be deleted. Try again.");

    private sealed record AllergyDto(string? Allergen, bool IsNkda);
    private sealed record PregnancyDto(bool? IsPregnant);

    public async Task<RxPatientSafety> GetPatientSafetyAsync(Guid patientId, CancellationToken ct = default)
    {
        var allergies = await api.GetAsync<List<AllergyDto>>($"/api/patients/{Id(patientId)}/allergies", null, ct) ?? [];
        var patient = await api.GetAsync<PregnancyDto>($"/api/patients/{Id(patientId)}", null, ct);
        return new RxPatientSafety(
            allergies.Where(a => !a.IsNkda && !string.IsNullOrWhiteSpace(a.Allergen)).Select(a => a.Allergen!).ToList(),
            patient?.IsPregnant == true);
    }
    //JJQR - END
}

public sealed class ApiDoctorPrintDataService(IApiClient api) : IDoctorPrintDataService
{
    //JJQR - AI (21 Nov 2026) - web handlePrint: GET /api/settings (fallback "Grace Medical Clinic"), /api/doctors/{id}, /api/patients/{id}
    sealed record SettingsDto(string? ClinicName, string? Address, string? ContactNumber);
    sealed record RxPatientDto(string? FirstName, string? LastName, string? PatientCode, DateOnly? DateOfBirth, SexType? Sex, string? Address);

    public async Task<RxPrintHeader?> GetRxPrintHeaderAsync(Guid doctorId, Guid patientId, CancellationToken ct = default)
    {
        SettingsDto? settings = null;
        try { settings = await api.GetAsync<SettingsDto>("/api/settings", null, ct); }
        catch (Exception ex) when (ex is not (ApiOfflineException or ApiUnauthorizedException or OperationCanceledException)) { }
        var doctor = await api.GetAsync<DoctorEmbed>($"/api/doctors/{Id(doctorId)}", null, ct);
        var patient = await api.GetAsync<RxPatientDto>($"/api/patients/{Id(patientId)}", null, ct);
        if (doctor is null || patient is null) return null;
        var clinic = settings is null ? new PrintClinic("Grace Medical Clinic", "", null) : new PrintClinic(settings.ClinicName ?? "", settings.Address ?? "", settings.ContactNumber);
        return new RxPrintHeader(clinic,
            new PrintDoctor(doctor.StaffAccounts?.FullName ?? "", doctor.LicenseNumber, doctor.PtrNumber, doctor.S2Number),
            new PrintPatient($"{patient.FirstName} {patient.LastName}".Trim(), patient.PatientCode,
                patient.DateOfBirth is { } dob ? ClinicPrintForms.Iso(dob) : null, patient.Sex?.ToString(), patient.Address));
    }
    //JJQR - END

    sealed record EmergencyDto(string? Name, string? Phone, string? Relationship);
    sealed record CardDto(Guid PatientId, string? PatientCode, string? FullName, string? BirthDate, string? Sex, string? BloodType, List<string>? Allergies,
        EmergencyDto? EmergencyContact, string? ClinicName, string? ClinicPhone, DateTimeOffset IssuedAt);
    sealed record LabOrderDto(string? TestName);

    public async Task<PatientCardData?> GetPatientCardAsync(Guid patientId, CancellationToken ct = default)
    {
        CardDto? c;
        try { c = await api.GetAsync<CardDto>($"/api/patients/{Id(patientId)}/card", null, ct); }
        catch (ApiNotFoundException) { return null; }
        if (c is null) return null;
        return new PatientCardData(c.PatientId, c.PatientCode ?? "", c.FullName ?? "", c.BirthDate ?? "", c.Sex ?? "", c.BloodType, c.Allergies ?? [],
            c.EmergencyContact?.Name, c.EmergencyContact?.Phone, c.EmergencyContact?.Relationship, c.ClinicName ?? "", c.ClinicPhone, c.IssuedAt);
    }

    public async Task<AppointmentSummaryData?> GetAppointmentSummaryAsync(Guid bookingId, CancellationToken ct = default)
    {
        BookingDto? b;
        try { b = await api.GetAsync<BookingDto>($"/api/bookings/{Id(bookingId)}", null, ct); }
        catch (ApiNotFoundException) { return null; }
        if (b is null) return null;
        return new AppointmentSummaryData(b.BookingId, b.AppointmentDate, b.Status.ToString(),
            b.Patients is { } p ? $"{p.FirstName} {p.LastName}" : "", b.Patients?.PatientCode ?? "",
            b.Doctors?.StaffAccounts?.FullName ?? "", b.Doctors?.Specialization ?? "", b.AmountDue ?? 0m,
            b.Payments?.Status?.ToString() ?? "Unpaid", b.Payments?.OrNumber);
    }

    public async Task<BookingPrintAvailability> GetPrintAvailabilityAsync(Guid bookingId, CancellationToken ct = default)
    {
        var groups = await Optional(() => api.GetAsync<List<RxGroupDto>>("/api/prescription-groups", Q(("bookingId", Id(bookingId))), ct), null) ?? [];
        var labs = await Optional(() => api.GetAsync<List<LabOrderDto>>($"/api/lab-orders/by-booking/{Id(bookingId)}", null, ct), null) ?? [];
        return new BookingPrintAvailability(
            (groups.FirstOrDefault()?.PrescriptionLineItems?.Count ?? 0) > 0,
            labs.Any(l => !string.IsNullOrWhiteSpace(l.TestName)));
    }
}
//JJQR - END
