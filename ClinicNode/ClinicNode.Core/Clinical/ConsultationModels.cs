// JJQR - AI (Nov 21, 2026) - consultation workspace DTOs used by the MAUI page and the service.
using ClinicApp.Domain.Enums;

namespace ClinicNode.Core.Clinical;

public sealed class ConsultationDraft
{
    public Guid? ConsultationId { get; set; }
    public Guid BookingId { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public ConsultationStatus Status { get; set; } = ConsultationStatus.Draft;

    public string? ChiefComplaint { get; set; }
    public string? Subjective { get; set; }
    public string? Objective { get; set; }
    public string? Assessment { get; set; }
    public string? Plan { get; set; }
    public string? DoctorNotes { get; set; }

    public VisitType VisitType { get; set; } = VisitType.New;
    public string? DiscountCategory { get; set; }
    public bool MedCertRequested { get; set; }

    public string? PfDecision { get; set; } // Charge | Waive | null
    public decimal? PfAmount { get; set; }
    public string? PfWaiveReason { get; set; }

    public List<DiagnosisLine> Diagnoses { get; set; } = [];
    public List<VitalLine> Vitals { get; set; } = [];
    public List<RxLine> Prescriptions { get; set; } = [];
    public bool NoMedsConfirmed { get; set; }
    public List<LabLine> LabOrders { get; set; } = [];
    public List<VaccinationLine> Vaccinations { get; set; } = [];

    public FollowUpMode FollowUpMode { get; set; } = FollowUpMode.Dated;
    public DateOnly? FollowUpDate { get; set; }
    public string? FollowUpReason { get; set; }
    public string? FollowUpInstructions { get; set; }
    public bool FollowUpReminder { get; set; }

    public bool CertificateExists { get; set; }
    public string? McAddress { get; set; }
    public string? McExaminedAt { get; set; }
    public DateOnly? McExamFrom { get; set; }
    public DateOnly? McExamTo { get; set; }
    public string? McDiagnosis { get; set; }
    public string? McRecommendations { get; set; }
    public string? McPurposeException { get; set; }
    public DateOnly? McComeBackOn { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class DiagnosisLine
{
    public Guid? Id { get; set; }
    public string? Icd10Code { get; set; }
    public string? CustomDescription { get; set; }
    public DiagnosisType Type { get; set; } = DiagnosisType.Primary;
}

public sealed class VitalLine
{
    public Guid TemplateId { get; set; }
    public string FormKey { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public string? Value { get; set; }
}

public sealed class RxLine
{
    public Guid? Id { get; set; }
    public Guid? MedicineId { get; set; }
    public string GenericName { get; set; } = "";
    public string Dosage { get; set; } = "";
    public string Quantity { get; set; } = "";
    public string? Instruction { get; set; }
    public string? Timing { get; set; }
    public string? MealRelation { get; set; }
    public string? DurationKind { get; set; }
    public int? DurationValue { get; set; }
    public string? Indication { get; set; }
    public string? Route { get; set; }
    public bool? AsNeeded { get; set; }
    public bool IsControlledSubstance { get; set; }
}

public sealed class LabLine
{
    public Guid? LabOrderId { get; set; }
    public Guid? LabTestId { get; set; }
    public string TestName { get; set; } = "";
    public string? Reason { get; set; }
    public string? ClinicalIndication { get; set; }
    public string? SpecimenType { get; set; }
    public string? Notes { get; set; }
}

public sealed class VaccinationLine
{
    public Guid? Id { get; set; }
    public string VaccineName { get; set; } = "";
    public short? DoseNumber { get; set; }
    public string? Notes { get; set; }
}

public sealed record ConsultationWorkspace
{
    public required ConsultationDraft Draft { get; init; }
    public required string PatientName { get; init; }
    public required string PatientCode { get; init; }
    public DateOnly? PatientDateOfBirth { get; init; }
    public string? PatientSex { get; init; }
    public required string DoctorName { get; init; }
    public DateOnly AppointmentDate { get; init; }
    public string? QueueNumber { get; init; }
    public bool IsWalkIn { get; init; }
    public decimal ComputedFee { get; init; }
    public string FeeSummary { get; init; } = "";
    public IReadOnlyList<VitalLine> VitalTemplates { get; init; } = [];
}

public sealed class ConsultSaveResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public ConsultationDraft? Draft { get; init; }
    public IReadOnlyList<string> MissingRequired { get; init; } = [];

    public static ConsultSaveResult Fail(string error, IReadOnlyList<string>? missing = null) =>
        new() { Succeeded = false, Error = error, MissingRequired = missing ?? [] };

    public static ConsultSaveResult Ok(ConsultationDraft draft) =>
        new() { Succeeded = true, Draft = draft };
}
// JJQR - END
