//JJQR - AI (21 Nov 2026) - prescription editor rules, copied from Project02-fe: lib/clinical/drug-safety.ts (checkDrugSafety, verbatim lists and notes),
// lib/rx-structured.ts (slotsToTiming / timingToSlots), components/doctor/PrescriptionForm.tsx (directionsLine, duplicate + save messages) and
// lib/clinical/rx-template-apply.ts (planTemplateApply / describeTemplateApply).
//JJQR - AI (21 Nov 2026) - Rx form parity: pregnancy lists + checkDrugSafety({ pregnant, trimester }), allergyBlockReason / reportAllergyConflict, the
// planTemplateApply allergy step, suggestFromSig / prefillItem (lib/rx-structured.ts, via the existing ClinicNode.Core.Printing.RxSig port of rx-sig-parse.ts)
// and itemPrintSigComplete (PrescriptionForm.tsx print-ready sig check).
//JJQR - END
using System.Text.RegularExpressions;
using ClinicNode.Core.Printing;

namespace ClinicNode.Core.DoctorExtra;

public sealed record RxSafetyHit(string Level, string Message); // Level: "warn" | "block"

public sealed record RxApplyPlan(IReadOnlyList<TplRxLine> ToAdd, IReadOnlyList<(string GenericName, string Reason, string Message)> Skipped, IReadOnlyList<string> Warnings);

public static class DoctorRxRules
{
    public const string ItemExists = "Item already exist.";
    public const string NothingSelected = "Select Prescriptions to continue this process!";
    public const string TemplateTitleRequired = "Template title required!";
    public const string SaveFailed = "Could not save the prescription. Try again.";
    public const string NotInCatalog = "Not in catalog — prefer picking a generic from the list (RA 6675).";

    public static readonly string[] SlotLabels = ["Breakfast", "Lunch", "Dinner", "Bedtime"];

    public static readonly string[][] DuplicateTherapyGroups =
    [
        ["losartan", "telmisartan", "valsartan", "candesartan", "olmesartan"],
        ["amlodipine", "nifedipine", "felodipine"],
        ["simvastatin", "atorvastatin", "rosuvastatin", "pravastatin"],
        ["ibuprofen", "mefenamic", "naproxen", "diclofenac"],
        ["metformin", "glipizide", "glibenclamide"],
    ];

    public static readonly (string A, string B, string Note)[] InteractionPairs =
    [
        ("warfarin", "ibuprofen", "Warfarin + NSAID: increased bleeding risk — monitor INR, consider gastroprotection."),
        ("warfarin", "naproxen", "Warfarin + NSAID: increased bleeding risk — monitor INR."),
        ("warfarin", "mefenamic", "Warfarin + NSAID: increased bleeding risk — monitor INR."),
        ("lisinopril", "potassium", "ACE inhibitor + potassium supplement: hyperkalemia risk."),
        ("enalapril", "potassium", "ACE inhibitor + potassium supplement: hyperkalemia risk."),
        ("losartan", "potassium", "ARB + potassium supplement: hyperkalemia risk."),
        ("metformin", "contrast", "Metformin + iodinated contrast: consider holding metformin per local renal protocol."),
        ("simvastatin", "clarithromycin", "Statin + macrolide: myopathy/rhabdo risk — prefer azithromycin or pause statin briefly."),
        ("atorvastatin", "erythromycin", "Statin + macrolide: myopathy risk — use caution or alternative antibiotic."),
        ("lithium", "ibuprofen", "Lithium + NSAID: raised lithium levels — monitor."),
        ("methotrexate", "trimethoprim", "Methotrexate + trimethoprim: bone marrow suppression risk."),
    ];

    //JJQR - AI (21 Nov 2026) - drug-safety.ts PREGNANCY_UNSAFE / PREGNANCY_BLOCKS (verbatim)
    public static readonly (string Match, string Note, string? Trimester)[] PregnancyUnsafe =
    [
        ("losartan", "ACEi/ARB contraindicated in pregnancy — switch antihypertensive.", null),
        ("telmisartan", "ACEi/ARB contraindicated in pregnancy.", null),
        ("enalapril", "ACE inhibitor contraindicated in pregnancy.", null),
        ("ramipril", "ACE inhibitor contraindicated in pregnancy.", null),
        ("lisinopril", "ACE inhibitor contraindicated in pregnancy.", null),
        ("simvastatin", "Statin contraindicated in pregnancy.", null),
        ("atorvastatin", "Statin contraindicated in pregnancy.", null),
        ("warfarin", "Warfarin teratogenic — avoid unless specialist plan.", null),
        ("doxycycline", "Tetracycline class — avoid in pregnancy.", null),
        ("tetracycline", "Tetracycline class — avoid in pregnancy.", null),
        ("isotretinoin", "Isotretinoin: absolute teratogen — do not prescribe.", null),
        ("methotrexate", "Methotrexate teratogenic — contraindicated.", null),
        ("misoprostol", "Misoprostol abortifacient — contraindicated.", null),
        ("valproate", "Valproate: neural tube defect risk — avoid if possible.", null),
        ("ibuprofen", "NSAID — avoid especially 3rd trimester (ductus closure).", "3"),
        ("mefenamic", "NSAID — avoid especially 3rd trimester.", "3"),
        ("naproxen", "NSAID — avoid especially 3rd trimester.", "3"),
    ];

    public static readonly string[] PregnancyBlocks = ["isotretinoin", "methotrexate", "warfarin", "misoprostol"];

    /// <summary>PrescriptionForm allergyBlockReason: the matching allergen (as typed) or null. NKDA rows must already be filtered out.</summary>
    public static string? AllergyBlockReason(IReadOnlyList<string>? allergies, string genericName)
    {
        if (allergies is null || allergies.Count == 0) return null;
        var g = Norm(genericName);
        if (g.Length == 0) return null;
        foreach (var raw in allergies)
        {
            var al = Norm(raw);
            if (al.Length == 0) continue;
            if (g.Contains(al) || al.Contains(g)) return raw.Trim();
        }
        return null;
    }

    public static string AllergyConflict(string genericName, string allergen) => $"Allergy conflict: {genericName} ↔ {allergen}";

    public const string PrintSigIncomplete = "Complete dose, route, frequency, and duration (sig) before printing.";

    static readonly Regex FrequencyLike = new(@"\b(bd|bid|tid|qid|qd|q\d+h|daily|every|\d+\s*x|\d+x|times?\s*(a|per)\s*day|once|twice|thrice|hourly|weekly|prn)\b", RegexOptions.ECMAScript | RegexOptions.CultureInvariant);
    static readonly Regex DurationLike = new(@"\b(day|days|week|weeks|month|months|ongoing|maintenance|maintain)\b", RegexOptions.ECMAScript | RegexOptions.CultureInvariant);

    /// <summary>PrescriptionForm itemPrintSigComplete: dose, route, a frequency (timing or a frequency word) and a duration.</summary>
    public static bool PrintSigComplete(TplRxLine l)
    {
        if (string.IsNullOrWhiteSpace(l.Dosage)) return false;
        if (string.IsNullOrWhiteSpace(l.Route)) return false;
        var instruction = (l.Instruction ?? "").ToLowerInvariant();
        var hasFreq = !string.IsNullOrWhiteSpace(l.Timing) || FrequencyLike.IsMatch(instruction);
        if (!hasFreq) return false;
        return l.DurationKind == "Maintain"
            || (l.DurationKind is "Days" or "Weeks" && l.DurationValue is { } v && v != 0)
            || DurationLike.IsMatch(instruction);
    }

    /// <summary>rx-structured.ts suggestFromSig: meal ("Before" / "After" / null), slot labels, as-needed.</summary>
    public static (string? Meal, IReadOnlyList<string> Slots, bool AsNeeded) SuggestFromSig(string? instruction, string? route)
    {
        var parsed = RxSig.Parse(instruction, route);
        var meal = parsed.Meal == "before" ? "Before" : parsed.Meal == "after" ? "After" : null;
        var slots = parsed.Slots.Select(s => SlotLabels.FirstOrDefault(l => string.Equals(l, s, StringComparison.OrdinalIgnoreCase)))
            .Where(l => l is not null).Select(l => l!).ToList();
        return (meal, slots, parsed.AsNeeded);
    }

    /// <summary>rx-structured.ts prefillItem: suggestions only fill what the line does not already say.</summary>
    public static TplRxLine Prefill(TplRxLine item)
    {
        var s = SuggestFromSig(item.Instruction, item.Route);
        var meal = item.MealRelation ?? s.Meal;
        var asNeeded = item.AsNeeded ?? s.AsNeeded;
        var timing = item.Timing;
        if (string.IsNullOrWhiteSpace(timing) && asNeeded != true) timing = SlotsToTiming(s.Slots);
        return item with { MealRelation = meal, AsNeeded = asNeeded, Timing = timing };
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - lib/clinical/peds-dosing.ts (verbatim) + JS parseFloat for the pediatric dose helper inputs
    public static (double Mg, bool Capped) MgPerKgDose(double mgPerKg, double weightKg, double? maxMg = null)
    {
        if (!double.IsFinite(mgPerKg) || !double.IsFinite(weightKg) || weightKg <= 0) return (0, false);
        var mg = mgPerKg * weightKg;
        var capped = false;
        if (maxMg is { } max && double.IsFinite(max) && mg > max) { mg = max; capped = true; }
        return (JsRound1(mg), capped);
    }

    public static double MlFromConcentration(double mgNeeded, double mgPerMl)
    {
        if (!double.IsFinite(mgNeeded) || !double.IsFinite(mgPerMl) || mgPerMl <= 0) return 0;
        return JsRound1(mgNeeded / mgPerMl);
    }

    /// <summary>Math.round(x * 10) / 10 (JS rounds .5 up).</summary>
    static double JsRound1(double x) => Math.Floor(x * 10 + 0.5) / 10;

    static readonly Regex LeadingFloat = new(@"^\s*[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?", RegexOptions.CultureInvariant);

    /// <summary>JS Number.parseFloat: the leading number, or null (NaN).</summary>
    public static double? ParseFloat(string? s)
    {
        var m = LeadingFloat.Match(s ?? "");
        return m.Success && double.TryParse(m.Value.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>JS String(number) for the helper's "≈ n mg" line.</summary>
    public static string JsNumber(double v) => v.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
    //JJQR - END

    static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();

    static bool DrugMatches(string name, string token)
    {
        var n = Norm(name);
        var t = Norm(token);
        if (n.Length == 0 || t.Length == 0) return false;
        return n.Contains(t) || t.Contains(n);
    }

    /// <summary>drug-safety.ts checkDrugSafety(names, { pregnant, trimester }). //JJQR - AI (21 Nov 2026) pregnancy options added //JJQR - END</summary>
    public static IReadOnlyList<RxSafetyHit> CheckDrugSafety(IEnumerable<string> genericNames, bool pregnant = false, int? trimester = null)
    {
        var names = genericNames.Select(Norm).Where(n => n.Length > 0).ToList();
        var output = new List<RxSafetyHit>();
        var seen = new HashSet<string>();
        void Push(string level, string message) { if (seen.Add(level + ":" + message)) output.Add(new RxSafetyHit(level, message)); }
        foreach (var group in DuplicateTherapyGroups)
        {
            var hits = names.Where(n => group.Any(g => DrugMatches(n, g))).ToList();
            if (hits.Count >= 2) Push("warn", $"Duplicate therapy ({string.Join("/", group.Take(3))}…): {string.Join(", ", hits.Distinct())}");
        }
        foreach (var (a, b, note) in InteractionPairs)
            if (names.Any(n => DrugMatches(n, a)) && names.Any(n => DrugMatches(n, b))) Push("warn", note);
        //JJQR - AI (21 Nov 2026) - pregnancy checks (block the teratogens, warn the rest)
        if (pregnant)
        {
            foreach (var n in names)
                if (PregnancyBlocks.Any(b => DrugMatches(n, b))) Push("block", $"Pregnancy: {n} is contraindicated (teratogen/high risk).");
            foreach (var (match, note, tri) in PregnancyUnsafe)
            {
                if (tri == "3" && trimester is { } t && t < 3) continue;
                var hit = names.FirstOrDefault(n => DrugMatches(n, match));
                if (hit is null) continue;
                if (PregnancyBlocks.Any(b => DrugMatches(hit, b))) continue;
                Push("warn", $"Pregnancy: {note}");
            }
        }
        //JJQR - END
        return output;
    }

    /// <summary>F-019: same generic regardless of dose.</summary>
    public static bool IsDuplicate(IEnumerable<string> existing, string genericName) =>
        existing.Any(n => string.Equals(n, genericName, StringComparison.OrdinalIgnoreCase));

    public static string? SlotsToTiming(IEnumerable<string> slots)
    {
        var set = slots.ToHashSet();
        var ordered = SlotLabels.Where(set.Contains).ToList();
        return ordered.Count > 0 ? string.Join(", ", ordered) : null;
    }

    public static IReadOnlyList<string> TimingToSlots(string? timing)
    {
        var parts = (timing ?? "").Split(',').Select(p => p.Trim().ToLowerInvariant()).ToHashSet();
        return SlotLabels.Where(l => parts.Contains(l.ToLowerInvariant())).ToList();
    }

    static string SlotList(string? timing)
    {
        var slots = (timing ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToList();
        if (slots.Count == 0) return "";
        if (slots.Count == 1) return slots[0];
        return $"{string.Join(", ", slots.Take(slots.Count - 1))} & {slots[^1]}";
    }

    static string DurationPhrase(string? kind, int? value)
    {
        if (kind == "Maintain") return "maintenance";
        if ((kind == "Days" || kind == "Weeks") && value is { } v && v != 0)
            return $"for {v} {(kind == "Days" ? "day" : "week")}{(v == 1 ? "" : "s")}";
        return "";
    }

    static string WhenPhrase(string? meal, string? timing)
    {
        var slots = SlotList(timing);
        var m = string.IsNullOrEmpty(meal) ? "" : meal.ToLowerInvariant();
        if (m.Length > 0 && slots.Length > 0) return $"{m} {slots}";
        if (m.Length > 0) return $"{m} meals";
        if (slots.Length > 0) return $"at {slots}";
        return "";
    }

    /// <summary>"After lunch, maintenance." / "Before breakfast &amp; dinner, for 5 days."</summary>
    public static string DirectionsLine(TplRxLine l)
    {
        var parts = new[] { WhenPhrase(l.MealRelation, l.Timing), DurationPhrase(l.DurationKind, l.DurationValue) }.Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return "";
        var s = string.Join(", ", parts);
        return char.ToUpperInvariant(s[0]) + s[1..] + ".";
    }

    //JJQR - AI (21 Nov 2026) - allergy step + pregnancy-aware safety (web opts.allergyConflict / opts.safety)
    public static RxApplyPlan PlanTemplateApply(IReadOnlyList<TplRxLine> templateItems, IEnumerable<string> existingGenericNames,
        Func<string, string?>? allergyConflict = null, bool pregnant = false)
    //JJQR - END
    {
        var names = existingGenericNames.ToList();
        var toAdd = new List<TplRxLine>();
        var skipped = new List<(string, string, string)>();
        var warnings = new List<string>();
        foreach (var item in templateItems)
        {
            var key = item.GenericName.Trim().ToLowerInvariant();
            //JJQR - AI (21 Nov 2026)
            if (allergyConflict?.Invoke(item.GenericName) is { } allergen) { skipped.Add((item.GenericName, "allergy", AllergyConflict(item.GenericName, allergen))); continue; }
            var alerts = CheckDrugSafety(names.Append(item.GenericName), pregnant);
            //JJQR - END
            if (alerts.FirstOrDefault(a => a.Level == "block") is { } block) { skipped.Add((item.GenericName, "safety", block.Message)); continue; }
            if (names.Any(n => n.Trim().ToLowerInvariant() == key)) { skipped.Add((item.GenericName, "duplicate", $"{item.GenericName} is already on the list")); continue; }
            if (alerts.FirstOrDefault(a => a.Level == "warn") is { } warn) warnings.Add(warn.Message);
            toAdd.Add(item with { Route = item.Route ?? "PO" });
            names.Add(item.GenericName);
        }
        return new RxApplyPlan(toAdd, skipped, warnings);
    }

    /// <summary>(variant: success | warning | error, message).</summary>
    public static (string Variant, string Message) DescribeTemplateApply(string templateTitle, int templateItemCount, RxApplyPlan plan)
    {
        var n = plan.ToAdd.Count;
        if (templateItemCount == 0) return ("warning", $"\"{templateTitle}\" has no medicines yet. Edit the template to add some.");
        static string Med(int c) => $"{c} medicine{(c == 1 ? "" : "s")}";
        if (n > 0)
        {
            var skippedBits = plan.Skipped.Count > 0
                ? $"; skipped {plan.Skipped.Count} ({string.Join("; ", plan.Skipped.Select(s => $"{s.GenericName}: {(s.Reason == "duplicate" ? "already on the list" : s.Message)}"))})"
                : "";
            return (plan.Skipped.Count > 0 ? "warning" : "success", $"Added {Med(n)} from {templateTitle}{skippedBits}");
        }
        if (plan.Skipped.All(s => s.Reason == "duplicate")) return ("warning", $"Nothing added: all {Med(plan.Skipped.Count)} from {templateTitle} are already on the list.");
        return ("error", $"Nothing added from {templateTitle}. {string.Join("; ", plan.Skipped.Select(s => s.Message))}");
    }
}
//JJQR - END
