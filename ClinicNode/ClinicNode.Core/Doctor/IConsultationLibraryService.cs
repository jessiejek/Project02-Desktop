//JJQR - AI (21 Nov 2026) - everything the consultation screen reads/writes besides the consultation itself: SOAP templates + phrases,
// certificate / diagnosis / Rx templates, medicines, lab catalogue, clinic fee schedule, doctor licence/PTR, patient address, allergies.
// Online-first: ApiConsultationLibraryService (ClinicNode.Api) when online, ConsultationLibraryService (local SQLite) otherwise.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Practice;

namespace ClinicNode.Core.Doctor;

public sealed record CertTemplateItem(Guid Id, string Title, bool IsSystem, string? DiagnosisText, string? Recommendations, string? PurposeException);
public sealed record LabTestItem(Guid Id, string Name, bool IsDefault, int SortOrder);
public sealed record DoctorCredentials(string FullName, string? LicenseNumber, string? PtrNumber);
public sealed record PatientAddress(string? Address, string? City);
//JJQR - AI (21 Nov 2026) - gap close: Rx Favorites tab, NIP history, ANC presets (pregnancy flag), Amendment History drawer
public sealed record FavoriteMedicineItem(Guid Id, Guid? MedicineId, string GenericName, string Dosage, string Quantity, string? Instruction);
//JJQR - AI (21 Nov 2026) - renal cue: the web's patientDetail.last_creatinine_mg_dl / last_egfr / last_renal_labs_date (optional, defaults keep callers compiling)
public sealed record PatientClinicalFlags(bool IsPregnant, decimal? LastCreatinineMgDl = null, decimal? LastEgfr = null, DateOnly? LastRenalLabsDate = null,
    //JJQR - AI (21 Nov 2026) - ALL-1 banner chips: the web clinicalBannerProps (patientDetail lmp_date / gravida / para / rh_factor / guardian_name / guardian_relationship)
    DateOnly? LmpDate = null, int? Gravida = null, int? Para = null, string? RhFactor = null, string? GuardianName = null, string? GuardianRelationship = null);

/// <summary>ALL-1: web consultation2 booking.paymentStatus — void bookings (Cancelled / NoShow / Expired) show "—",
/// otherwise the booking's payment status, or "Not recorded" when there is no payment row.</summary>
public static class ConsultPaymentDisplay
{
    public static string For(ClinicApp.Domain.Enums.BookingStatus bookingStatus, ClinicApp.Domain.Enums.PaymentStatus? paymentStatus) =>
        bookingStatus is ClinicApp.Domain.Enums.BookingStatus.Cancelled or ClinicApp.Domain.Enums.BookingStatus.NoShow or ClinicApp.Domain.Enums.BookingStatus.Expired
            ? "—"
            : paymentStatus?.ToString() ?? "Not recorded";
}
//JJQR - END
// "Refill last visit Rx": the patient's most recent OTHER consultation (by appointment date) and its prescription lines
public sealed record ConsultPriorVisit(Guid ConsultationId, Guid BookingId, DateOnly AppointmentDate, IReadOnlyList<ClinicNode.Core.Clinical.RxLine> RxItems);
//JJQR - END
public sealed record AmendmentEntry(DateTimeOffset At, string? Details);
//JJQR - END

public interface IConsultationLibraryService
{
    Task<IReadOnlyList<SoapTemplateItem>> ListSoapTemplatesAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<SoapPhraseItem>> ListSoapPhrasesAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<CertTemplateItem>> ListCertTemplatesAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<DiagnosisTemplateItem>> ListDiagnosisTemplatesAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<RxTemplateItem>> ListRxTemplatesAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<MedicineHit>> ListMedicinesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LabTestItem>> ListLabTestsAsync(CancellationToken ct = default);
    Task<ClinicSetting> GetClinicSettingsAsync(CancellationToken ct = default);
    Task<DoctorCredentials?> GetDoctorCredentialsAsync(Guid doctorId, CancellationToken ct = default);
    Task<PatientAddress?> GetPatientAddressAsync(Guid patientId, CancellationToken ct = default);
    Task<IReadOnlyList<PatientAllergy>> ListAllergiesAsync(Guid patientId, CancellationToken ct = default);

    Task<SoapTemplateItem> SaveSoapTemplateAsync(Guid doctorId, string title, SoapValues values, CancellationToken ct = default);
    Task<SoapPhraseItem> SaveSoapPhraseAsync(Guid doctorId, SoapField field, string label, string body, CancellationToken ct = default);
    Task<CertTemplateItem> SaveCertTemplateAsync(Guid doctorId, string title, string? diagnosisText, string? recommendations, string? purposeException, CancellationToken ct = default);
    /// <summary>Replace-all (web replacePatientAllergies). NKDA is sent as [{allergen: "NKDA", is_nkda: true}].</summary>
    Task<IReadOnlyList<PatientAllergy>?> ReplaceAllergiesAsync(Guid patientId, IReadOnlyList<AllergyInput> allergies, CancellationToken ct = default);
    /// <summary>Best-effort: a typed certificate address goes onto the patient profile when none was on file.</summary>
    Task<bool> UpdatePatientAddressAsync(Guid patientId, string address, CancellationToken ct = default);

    //JJQR - AI (21 Nov 2026) - web PrescriptionForm Favorites tab / "Add to Favorites", NIP status (patient vaccination history),
    // ANC presets (patient is_pregnant), Amendment History (audit_logs for the consultation)
    Task<IReadOnlyList<FavoriteMedicineItem>> ListFavoriteMedicinesAsync(Guid doctorId, CancellationToken ct = default);
    Task<FavoriteMedicineItem> AddFavoriteMedicineAsync(Guid doctorId, FavoriteMedicineItem item, CancellationToken ct = default);
    Task<IReadOnlyList<PatientVaccination>> ListPatientVaccinationsAsync(Guid patientId, CancellationToken ct = default);
    Task<PatientClinicalFlags?> GetPatientClinicalFlagsAsync(Guid patientId, CancellationToken ct = default);
    //JJQR - AI (21 Nov 2026) - ALL-1: banner "Payment: …" (web: GET /api/bookings/{id} → payments.status; local: Bookings + Payment)
    Task<string?> GetBookingPaymentDisplayAsync(Guid bookingId, CancellationToken ct = default);
    //JJQR - END
    Task<IReadOnlyList<AmendmentEntry>> ListAmendmentHistoryAsync(Guid consultationId, CancellationToken ct = default);
    //JJQR - END
    //JJQR - AI (21 Nov 2026) - Refill last visit Rx (web: GET /api/consultations?patientId= minus this booking, newest appointment_date,
    // then GET /api/prescription-groups?bookingId=) · SOAP specialty filter default (web: GET /api/doctors/{id} → specialization)
    Task<ConsultPriorVisit?> GetPriorVisitAsync(Guid patientId, Guid currentBookingId, CancellationToken ct = default);
    Task<string?> GetDoctorSpecializationAsync(Guid doctorId, CancellationToken ct = default);
    //JJQR - END
}
//JJQR - END
