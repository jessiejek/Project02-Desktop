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

    //JJQR - AI (Nov 24, 2026) - owner: the Admin chooses what is required to complete a visit (Admin > Settings > Consultation, clinic_settings.consult_required).
    // Keys: notes (Clinical notes), vitals (Vital Signs), prescription (Prescription). Null = default: Clinical notes only. The Professional Fee Decision is ALWAYS required.
    // Follow-up is never required any more: a visit with no follow-up date simply has no follow-up. Diagnosis stays hidden.
    public static readonly (string Key, int Section, string Label)[] ConfigurableRequired =
        [("notes", 0, "Clinical notes"), ("vitals", 1, "Vital signs"), ("prescription", 3, "Prescription")];
    public const string DefaultRequired = "notes";

    /// <summary>The clinic's choice, set whenever the clinic settings are read (online or from this computer's copy). Null = the default.</summary>
    public static volatile string? ConfiguredRequired;

    /// <summary>Sections required to complete a visit for a consult_required value (fee always included).</summary>
    public static IReadOnlyList<int> RequiredFrom(string? consultRequired)
    {
        var keys = (consultRequired ?? DefaultRequired).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant()).ToHashSet();
        return ConfigurableRequired.Where(c => keys.Contains(c.Key)).Select(c => c.Section).Append(FeeSectionIndex).ToArray();
    }

    public static IReadOnlyList<int> EffectiveRequiredSections => RequiredFrom(ConfiguredRequired);
    //JJQR - END

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
