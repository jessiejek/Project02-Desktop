// JJQR - AI (Nov 21, 2026) - consultation section order, hidden Diagnosis flag, and completion gates
// mirrored from Project02-fe consultation2/page.tsx and consult-steps.ts.
using ClinicApp.Domain.Enums;

namespace ClinicNode.Core;

public enum FollowUpMode
{
    Dated,
    Prn,
    None
}

public enum StepIndicator
{
    Complete,
    Warning,
    Partial,
    Empty
}

public static class ConsultSteps
{
    public static readonly string[] Sections =
    [
        "Clinical notes",
        "Vital Signs",
        "Diagnosis",
        "Prescription",
        "Lab Orders",
        "Vaccinations",
        "Follow-up",
        "Medical Certificate",
        "Professional Fee Decision",
    ];

    /// <summary>Clinical notes, vitals, diagnosis, prescription, follow-up, professional fee.</summary>
    public static readonly int[] RequiredSections = [0, 1, 2, 3, 6, 8];

    public const int FeeSectionIndex = 8;
    public const int DiagnosisSectionIndex = 2;
    public const int PrescriptionSectionIndex = 3;
    public const int FollowUpSectionIndex = 6;
    public const int VaccinationsSectionIndex = 5;

    /// <summary>
    /// When false, Diagnosis stays at index 2 for saves but is hidden from nav and completion gates.
    /// Flip this to restore the step without a rewrite.
    /// </summary>
    public const bool ShowDiagnosisStep = false;

    public const bool ShowRequiredLeftBanner = false;

    public static IReadOnlyList<int> EffectiveRequiredSections =>
        ShowDiagnosisStep
            ? RequiredSections
            : RequiredSections.Where(i => i != DiagnosisSectionIndex).ToArray();

    /// <summary>Vaccinations stay behind "More". The fee step is always visible and last.</summary>
    public static IReadOnlyList<int> OccasionalSections { get; } = [VaccinationsSectionIndex];

    public static IReadOnlyList<int> VisibleSectionOrder
    {
        get
        {
            var all = Enumerable.Range(0, Sections.Length)
                .Where(i => ShowDiagnosisStep || i != DiagnosisSectionIndex)
                .Where(i => !OccasionalSections.Contains(i))
                .ToList();
            // Fee is already last (index 8) once diagnosis/vaccinations are removed from the main rail.
            return all;
        }
    }

    public static StepIndicator Indicator(bool satisfied, bool required, bool hasDraft, bool attemptedComplete)
    {
        if (satisfied) return StepIndicator.Complete;
        if (required) return attemptedComplete ? StepIndicator.Warning : StepIndicator.Empty;
        return hasDraft ? StepIndicator.Partial : StepIndicator.Empty;
    }

    public static int? AdjacentSection(IReadOnlyList<int> order, int current, int direction)
    {
        var pos = order.ToList().IndexOf(current);
        if (pos < 0) return null;
        var target = pos + direction;
        return target >= 0 && target < order.Count ? order[target] : null;
    }

    public static bool FollowUpSatisfied(FollowUpMode mode, DateOnly? followUpDate) =>
        mode != FollowUpMode.Dated || followUpDate is not null;

    public static bool ClinicalNotesSatisfied(string? chiefComplaint) =>
        !string.IsNullOrWhiteSpace(chiefComplaint);

    public static bool VitalsSatisfied(IEnumerable<(string FormKey, string? Value)> readings)
    {
        var list = readings.ToList();
        var required = list.Where(r => r.FormKey is "blood_pressure" or "pulse_rate").ToList();
        if (required.Count > 0)
            return required.All(r => !string.IsNullOrWhiteSpace(r.Value));
        return list.Count > 0 && list.All(r => !string.IsNullOrWhiteSpace(r.Value));
    }

    public static bool PrescriptionSatisfied(int itemCount, bool noMedsConfirmed) =>
        itemCount > 0 || noMedsConfirmed;

    public static bool FeeSatisfied(string? pfDecision, decimal? pfAmount, string? waiveReason)
    {
        if (string.Equals(pfDecision, "Charge", StringComparison.OrdinalIgnoreCase))
            return pfAmount is { } a && FeeCalculator.IsValidChargeAmount(a);
        if (string.Equals(pfDecision, "Waive", StringComparison.OrdinalIgnoreCase))
            return !string.IsNullOrWhiteSpace(waiveReason);
        return false;
    }
}
// JJQR - END
