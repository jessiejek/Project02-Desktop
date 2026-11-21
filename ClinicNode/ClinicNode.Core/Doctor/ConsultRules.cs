//JJQR - AI (21 Nov 2026) - pure ports of the web consultation2 rules (page.tsx, lib/clinical/soap-merge.ts, med-cert-fields.ts,
// preview-fee.ts, pf-default.ts, lib/rx-structured.ts). No I/O. Used by ConsultationPage and the online-first services.
using System.Globalization;
using System.Text.RegularExpressions;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Clinical;

namespace ClinicNode.Core.Doctor;

public enum SoapApplyMode { Replace, Append }
public enum SoapFieldOutcome { Filled, Replaced, Appended, KeptTemplateBlank, Unchanged }

/// <summary>The five SOAP strings in web order (chiefComplaint, subjective, objective, assessment, plan).</summary>
public sealed record SoapValues(string ChiefComplaint, string Subjective, string Objective, string Assessment, string Plan)
{
    public static SoapValues Empty { get; } = new("", "", "", "", "");
    public string this[int i] => i switch { 0 => ChiefComplaint, 1 => Subjective, 2 => Objective, 3 => Assessment, _ => Plan };
    public bool HasContent => Enumerable.Range(0, 5).Any(i => !string.IsNullOrWhiteSpace(this[i]));
    public static SoapValues From(string? cc, string? s, string? o, string? a, string? p) => new(cc ?? "", s ?? "", o ?? "", a ?? "", p ?? "");
}

public static class ConsultRules
{
    // ── sections (web SECTION_ORDER = [0,1,2,3,4,6,7,5,8]; Diagnosis hidden; Vaccinations behind More) ──────────
    public static readonly int[] SectionOrder = [0, 1, 2, 3, 4, 6, 7, 5, 8];
    public static readonly string[] SoapLabels = ["Chief complaint", "Subjective", "Objective", "Assessment", "Plan"];
    public static readonly SoapField[] SoapFields = [SoapField.ChiefComplaint, SoapField.Subjective, SoapField.Objective, SoapField.Assessment, SoapField.Plan];
    public static readonly string[] SoapHints =
    [
        "Reason for this visit, in the patient’s own words.",
        "Symptoms, relevant history and patient-reported concerns.",
        "Examination findings and observed measurements.",
        "Clinical impression and diagnostic reasoning.",
        "Management, counselling and next steps.",
    ];

    /// <summary>MC_EXAMINED_AT_DEFAULT from the web page.</summary>
    public const string McExaminedAtDefault = "GRACE MEDICAL CLINIC, 3ML Quezon National Highway, Buaya, Lapu Lapu City";
    public const string PrnReason = "PRN / return if needed";
    public const string PrnInstructions = "PRN — return if symptoms recur";
    public const int WaiveReasonMinLength = 5;
    public const int RxInstructionMax = 200;
    public static readonly string[] RxSlots = ["Breakfast", "Lunch", "Dinner", "Bedtime"];

    /// <summary>Visible steps in nav order: [0,1,3,4,6,7,(5 when More is open or you are on it),8].</summary>
    public static IReadOnlyList<int> VisibleOrder(bool showMore, int openSection) =>
        SectionOrder.Where(i => (ConsultSteps.ShowDiagnosisStep || i != ConsultSteps.DiagnosisSectionIndex)
                                && (i != ConsultSteps.VaccinationsSectionIndex || showMore || openSection == i)).ToList();

    public static IReadOnlyList<int> RequiredSections => ConsultSteps.EffectiveRequiredSections;

    // ── fee (web §16.6) ──────────────────────────────────────────────────────────────────────────
    /// <summary>web pfSatisfied: Charge needs 0 &lt; amount ≤ 100,000; Waive needs a reason of at least 5 characters.</summary>
    public static bool FeeSatisfied(string? decision, decimal? shownAmount, string? waiveReason) => decision switch
    {
        "Charge" => shownAmount is { } a && FeeCalculator.IsValidChargeAmount(a),
        "Waive" => (waiveReason ?? "").Trim().Length >= WaiveReasonMinLength,
        _ => false,
    };

    /// <summary>medCertFeeAddOn: an issued certificate, or any of recommendations / purpose exception / come-back date filled.</summary>
    public static bool MedCertFeeAddOn(bool certExists, string? recommendations, string? purposeException, DateOnly? comeBackOn) =>
        certExists || !string.IsNullOrWhiteSpace(recommendations) || !string.IsNullOrWhiteSpace(purposeException) || comeBackOn is not null;

    /// <summary>medCertRequested = medCertManual ?? add-on.</summary>
    public static bool MedCertRequested(bool? manual, bool addOn) => manual ?? addOn;

    public static ClinicFeePreview PreviewFee(ClinicSetting settings, VisitType visitType, string? discount, bool medCert) =>
        FeeCalculator.Compute(settings, visitType, discount, medCert);

    /// <summary>pfAmountShown: Charge and not typed → the computed total; otherwise what the doctor typed.</summary>
    public static decimal? PfAmountShown(string? decision, bool edited, decimal? typed, decimal previewTotal) =>
        decision == "Charge" && !edited ? previewTotal : typed;

    /// <summary>Banner sentence under "Final step — decide the professional fee for this visit."</summary>
    public static string FeeBannerLine(string? decision, decimal? shown, string? waiveReason) => decision switch
    {
        "Charge" => shown is null ? "This patient will be charged — enter the amount below."
            : !FeeCalculator.IsValidChargeAmount(shown.Value) ? "Enter an amount above ₱0 and up to ₱100,000 (or choose Waive)." //JJQR - AI (Nov 21, 2026) - never say "charged ₱0" //JJQR - END
            : $"This patient will be charged {FeeCalculator.FormatPeso(shown.Value)}.",
        "Waive" => (waiveReason ?? "").Trim().Length >= WaiveReasonMinLength
            ? $"The professional fee will be waived: {waiveReason!.Trim()}"
            : "The professional fee will be waived — add a reason (at least 5 characters).",
        _ => "Choose Charge or Waive.",
    };

    // ── required-section gate (web canComplete) ───────────────────────────────────────────────
    public static bool SectionSatisfied(int section, ConsultationDraft d, decimal? pfShown) => section switch
    {
        0 => ConsultSteps.ClinicalNotesSatisfied(d.ChiefComplaint),
        1 => ConsultSteps.VitalsSatisfied(d.Vitals.Select(v => (v.FormKey, v.Value))),
        2 => d.Diagnoses.Count > 0,
        3 => ConsultSteps.PrescriptionSatisfied(d.Prescriptions.Count, d.NoMedsConfirmed),
        6 => ConsultSteps.FollowUpSatisfied(d.FollowUpMode, d.FollowUpDate),
        8 => FeeSatisfied(d.PfDecision, pfShown, d.PfWaiveReason),
        _ => true,
    };

    public static IReadOnlyList<int> MissingRequired(ConsultationDraft d, decimal? pfShown) =>
        RequiredSections.Where(i => !SectionSatisfied(i, d, pfShown)).ToList();

    // ── SOAP merge (soap-merge.ts) ───────────────────────────────────────────────────────────
    public static (string Value, SoapFieldOutcome Outcome) MergeSoapField(string? current, string? templateText, SoapApplyMode mode)
    {
        current ??= "";
        if (string.IsNullOrWhiteSpace(templateText)) return (current, SoapFieldOutcome.KeptTemplateBlank);
        var tpl = templateText.Trim();
        if (string.IsNullOrWhiteSpace(current)) return (tpl, SoapFieldOutcome.Filled);
        if (mode == SoapApplyMode.Replace)
            return current.Trim() == tpl ? (current, SoapFieldOutcome.Unchanged) : (tpl, SoapFieldOutcome.Replaced);
        if (current.Contains(tpl, StringComparison.Ordinal)) return (current, SoapFieldOutcome.Unchanged);
        return (current.TrimEnd() + "\n" + tpl, SoapFieldOutcome.Appended);
    }

    public static (SoapValues Values, SoapFieldOutcome[] Outcomes) MergeSoapTemplate(SoapValues current, SoapValues template, SoapApplyMode mode)
    {
        var v = new string[5];
        var o = new SoapFieldOutcome[5];
        for (var i = 0; i < 5; i++) (v[i], o[i]) = MergeSoapField(current[i], template[i], mode);
        return (new SoapValues(v[0], v[1], v[2], v[3], v[4]), o);
    }

    /// <summary>"Apply SOAP template" preview line per field (web outcomeText).</summary>
    public static string OutcomeLabel(SoapFieldOutcome o) => o switch
    {
        SoapFieldOutcome.Filled => "Will be filled",
        SoapFieldOutcome.Replaced => "Will be replaced",
        SoapFieldOutcome.Appended => "Will be added on a new line",
        SoapFieldOutcome.KeptTemplateBlank => "Template is blank here — your text is kept",
        _ => "No change",
    };

    /// <summary>SoapFieldToolbar insert: a phrase goes on a new line.</summary>
    public static string InsertPhrase(string? current, string body) =>
        string.IsNullOrWhiteSpace(current) ? body : current.TrimEnd() + "\n" + body;

    /// <summary>"Assessment does not mention the primary diagnosis." hint.</summary>
    public static bool AssessmentMissesPrimaryDx(string? assessment, string? primaryDxText)
    {
        if (string.IsNullOrWhiteSpace(assessment) || string.IsNullOrWhiteSpace(primaryDxText)) return false;
        return !assessment.Contains(primaryDxText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // ── medical certificate (med-cert-fields.ts) ────────────────────────────────────────────
    public static string JoinNonBlank(IEnumerable<string?> parts, string sep = ", ") =>
        string.Join(sep, parts.Select(p => (p ?? "").Trim()).Where(p => p.Length > 0));

    public static string PatientAddressFromProfile(string? address, string? city) => JoinNonBlank([address, city]);
    public static string ExaminedAtFromClinic(string? name, string? address) => JoinNonBlank([name, address]);

    public static string DiagnosisTextFromConsult(IEnumerable<string?> descriptions, string? assessment)
    {
        var fromDx = string.Join("; ", descriptions.Select(d => (d ?? "").Trim()).Where(d => d.Length > 0));
        return fromDx.Length > 0 ? fromDx : (assessment ?? "").Trim();
    }

    public static bool ShouldApplyTemplateDiagnosis(IEnumerable<string?> descriptions, string? assessment) =>
        DiagnosisTextFromConsult(descriptions, assessment).Length == 0;

    public static string CleanMedCertAddress(string? addr)
    {
        var s = (addr ?? "").Trim();
        s = Regex.Replace(s, "^(residing at\\s+)+", "", RegexOptions.IgnoreCase).Trim();
        s = Regex.Replace(s, "[,\\s]+$", "").Trim();
        s = Regex.Replace(s, ",\\s*,+", ", ");
        return s;
    }

    public static bool ComeBackBeforeUntil(DateOnly? comeBack, DateOnly? examTo) => comeBack is { } c && examTo is { } t && c < t;

    // ── follow-up (pf-default.ts) ───────────────────────────────────────────────────────────
    public static bool IsPrnFollowUp(string? reason, string? instructions) =>
        Regex.IsMatch(reason ?? "", "PRN", RegexOptions.IgnoreCase) || Regex.IsMatch(instructions ?? "", "PRN", RegexOptions.IgnoreCase);

    // ── Rx (rx-structured.ts / PrescriptionForm) ────────────────────────────────────────────
    public static string? SlotsToTiming(IEnumerable<string> slots)
    {
        var set = slots.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ordered = RxSlots.Where(set.Contains).ToList();
        return ordered.Count > 0 ? string.Join(", ", ordered) : null;
    }

    public static IReadOnlyList<string> TimingToSlots(string? timing)
    {
        var parts = (timing ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return RxSlots.Where(s => parts.Any(p => p.Equals(s, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>PrescriptionForm canAdd: a medicine name and a quantity (dosage is optional).</summary>
    public static bool RxCanAdd(string? name, string? quantity) => !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(quantity);

    /// <summary>directionsLine: "After lunch, maintenance." style summary of the structured fields.</summary>
    public static string DirectionsLine(RxLine rx)
    {
        var bits = new List<string>();
        var slots = TimingToSlots(rx.Timing);
        if (rx.MealRelation is { Length: > 0 } meal && slots.Count > 0)
            bits.Add($"{meal} {string.Join(", ", slots.Select(s => s.ToLowerInvariant()))}");
        else if (rx.MealRelation is { Length: > 0 } meal2) bits.Add($"{meal2} meals");
        else if (slots.Count > 0) bits.Add(string.Join(", ", slots));
        if (rx.AsNeeded == true) bits.Add("as needed");
        if (string.Equals(rx.DurationKind, "Maintain", StringComparison.OrdinalIgnoreCase)) bits.Add("maintenance");
        else if (rx.DurationKind is { Length: > 0 } k && rx.DurationValue is { } v) bits.Add($"for {v} {k.ToLowerInvariant()}");
        if (bits.Count == 0) return "";
        var line = string.Join(", ", bits);
        return char.ToUpperInvariant(line[0]) + line[1..] + ".";
    }

    // ── completion checklist (page.tsx sectionSummary) ───────────────────────────────────────
    public static string ClipText(string? s, int n = 120)
    {
        var t = Regex.Replace(s ?? "", "\\s+", " ").Trim();
        return t.Length > n ? t[..(n - 1)] + "…" : t;
    }

    public static string SectionSummary(int i, ConsultationDraft d, decimal? pfShown)
    {
        switch (i)
        {
            case 0:
                var soap = new[]
                {
                    Pref("Chief complaint", d.ChiefComplaint), Pref("Subjective", d.Subjective), Pref("Objective", d.Objective),
                    Pref("Assessment", d.Assessment), Pref("Plan", d.Plan),
                }.Where(x => x.Length > 0).ToList();
                return soap.Count > 0 ? string.Join("\n", soap) : "Nothing entered.";
            case 1:
                var vit = d.Vitals.Where(v => !string.IsNullOrWhiteSpace(v.Value))
                    .Select(v => $"{v.Description} {v.Value!.Trim()}{(string.IsNullOrEmpty(v.Unit) ? "" : " " + v.Unit)}").ToList();
                return vit.Count > 0 ? string.Join(" · ", vit) : "Nothing recorded.";
            case 2:
                return d.Diagnoses.Count > 0 ? string.Join("; ", d.Diagnoses.Select(x => x.CustomDescription ?? x.Icd10Code)) : "Nothing entered.";
            case 3:
                return d.Prescriptions.Count > 0
                    ? string.Join("\n", d.Prescriptions.Select(rx => $"{rx.GenericName}{(string.IsNullOrWhiteSpace(rx.Dosage) ? "" : " " + rx.Dosage)}{(string.IsNullOrWhiteSpace(rx.Quantity) ? "" : " #" + rx.Quantity)}"))
                    : d.NoMedsConfirmed ? "No medicines this visit." : "Nothing entered.";
            case 4:
                var labs = d.LabOrders.Select(l => l.TestName.Trim()).Where(x => x.Length > 0).ToList();
                return labs.Count > 0 ? string.Join(", ", labs) : "None ordered.";
            case 5:
                var vax = d.Vaccinations.Where(v => v.VaccineName.Trim().Length > 0)
                    .Select(v => $"{v.VaccineName.Trim()}{(v.DoseNumber is { } n ? $" (dose {n})" : "")}").ToList();
                return vax.Count > 0 ? string.Join(", ", vax) : "None recorded.";
            case 6:
                return d.FollowUpMode switch
                {
                    FollowUpMode.None => "No follow-up.",
                    FollowUpMode.Prn => "PRN — return if needed.",
                    _ => d.FollowUpDate is { } f
                        ? $"{f.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}{(string.IsNullOrWhiteSpace(d.FollowUpReason) ? "" : " — " + ClipText(d.FollowUpReason, 80))}"
                        : "No date chosen yet.",
                };
            case 7:
                return d.CertificateExists ? $"Certificate prepared{(string.IsNullOrWhiteSpace(d.McDiagnosis) ? "" : ": " + ClipText(d.McDiagnosis, 80))}." : "No certificate.";
            case 8:
                return d.PfDecision switch
                {
                    "Charge" => $"Charge {FeeCalculator.FormatPeso(pfShown ?? 0)}",
                    "Waive" => $"Waive — {(ClipText(d.PfWaiveReason, 80) is { Length: > 0 } r ? r : "no reason given")}",
                    _ => "Not decided yet.",
                };
            default:
                return "";
        }
    }

    private static string Pref(string label, string? v) => string.IsNullOrWhiteSpace(v) ? "" : $"{label}: {ClipText(v)}";

    public const string ChecklistRxWarning = "Add at least one medicine, or check “No medicines this visit”.";
    public const string ChecklistFollowUpWarning = "Complete follow-up (schedule a date, choose PRN, or choose No follow-up) before Complete — required for Grace clinic.";
}
//JJQR - END
