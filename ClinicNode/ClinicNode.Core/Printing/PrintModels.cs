//JJQR - AI (Nov 21, 2026) - print data models mirroring Project02-fe print-forms.ts / print-forms-html.ts (PrintDoctor, PrintPatient, RxLine, MedCertData) plus the queue slip ticket.
namespace ClinicNode.Core.Printing;

/// <summary>Same fields as the web <c>PrintDoctor</c> / <c>HtmlDoctor</c>.</summary>
public sealed record PrintDoctor(string FullName, string? LicenseNumber, string? PtrNumber, string? S2Number = null);

/// <summary>Same fields as the web <c>PrintPatient</c> / <c>HtmlPatient</c>. Dates are ISO <c>yyyy-MM-dd</c> strings like the API.</summary>
public sealed record PrintPatient(string FullName, string? PatientCode = null, string? DateOfBirth = null, string? Sex = null, string? Address = null);

/// <summary>Same fields as the web <c>RxLine</c> / <c>HtmlRxLine</c>.</summary>
public sealed record RxLine
{
    public string GenericName { get; init; } = "";
    public string Dosage { get; init; } = "";
    public string Quantity { get; init; } = "";
    public string? Instruction { get; init; }
    public string? Timing { get; init; }
    public string? MealRelation { get; init; }
    public bool? AsNeeded { get; init; }
    public string? DurationKind { get; init; }
    public int? DurationValue { get; init; }
    public string? Indication { get; init; }
    public bool IsControlledSubstance { get; init; }
    public string? Route { get; init; }
}

/// <summary>Inputs of <c>buildPrescriptionHtml</c> (RxHtmlOpts). Dates/age already formatted.</summary>
public sealed record RxHtmlOptions(
    string Origin,
    string PageSize,
    PrintDoctor Doctor,
    PrintPatient Patient,
    string Age,
    string Date,
    IReadOnlyList<RxLine> Items,
    string? NextAppointment = null);

/// <summary>Inputs of <c>buildMedicalCertificateHtml</c> (McHtmlOpts).</summary>
public sealed record MedCertHtmlOptions(
    string Origin,
    string PageSize,
    PrintDoctor Doctor,
    PrintPatient Patient,
    string IssueDate,
    string Address,
    string ExaminedAt,
    string ExamFrom,
    string ExamTo,
    string Diagnosis,
    string Recommendations,
    string PurposeException,
    string ComeBackOn);

/// <summary>Inputs of <c>buildLabRequestHtml</c> (LabHtmlOpts).</summary>
public sealed record LabHtmlOptions(
    string Origin,
    string PageSize,
    PrintDoctor Doctor,
    PrintPatient Patient,
    string Age,
    string Date,
    IReadOnlyList<string> Tests,
    IReadOnlyList<string>? Diagnoses = null,
    string? ClinicalNotes = null);

/// <summary>Raw medical certificate values (web <c>MedCertData</c>); ISO date strings.</summary>
public sealed record MedCertData
{
    public string? IssueDate { get; init; }
    public string? PatientAddressSnapshot { get; init; }
    public string? ExaminedAt { get; init; }
    public string? ExaminationDateFrom { get; init; }
    public string? ExaminationDateTo { get; init; }
    public string? DiagnosisText { get; init; }
    public string? Recommendations { get; init; }
    public string? PurposeException { get; init; }
    public string? ComeBackOn { get; init; }
}

/// <summary>Walk-in queue ticket (web WalkInWizard <c>ticket</c>, from QueueController).</summary>
public sealed record QueueSlipData
{
    public string ClinicName { get; init; } = "";
    public string ClinicAddress { get; init; } = "";
    public string QueueNumber { get; init; } = "";
    public bool IsPriority { get; init; }
    public string PatientName { get; init; } = "";
    public string PatientCode { get; init; } = "";
    public string DoctorName { get; init; } = "";
    /// <summary>"New" or "FollowUp" (VisitType enum name).</summary>
    public string VisitType { get; init; } = "New";
    public decimal ProvisionalFee { get; init; }
    public DateTimeOffset IssuedAt { get; init; }
}
//JJQR - END
