//JJQR - AI (21 Nov 2026) - pure ports of the web consultation aids (copied, not invented):
//   lib/clinical/nip-schedule.ts (NIP / EPI schedule + status), lib/clinical/anc-labs.ts (ANC lab presets),
//   lib/clinical/growth.ts (rough WHO-ish centiles 0–60 mo), lib/clinical/peds-dosing.ts (mg/kg helper),
//   consultation2 page helpers patientAgeMonths / vitalFormValue / pedsVitalRangeNote / growthPercentileNotes,
//   lib/pf-default.ts followUpViewLine and the page's formatProfessionalFeeLine.
using System.Globalization;
using System.Text.RegularExpressions;
using ClinicNode.Core.Clinical;

namespace ClinicNode.Core.Doctor;

public sealed record NipScheduleRow(string AgeLabel, double AgeMonthsMin, IReadOnlyList<string> Vaccines);
public sealed record NipStatusRow(string Key, string Label, double AgeMonthsDue, string Status); // given | due | overdue | upcoming | catch-up
public sealed record AncPreset(string Id, string Label, IReadOnlyList<string> RequiredNames, IReadOnlyList<string> CatalogPatterns, IReadOnlyList<string> OptionalCatalogPatterns);
public sealed record GrowthCurvePoint(double AgeMonths, double P3, double P15, double P50, double P85, double P97);

public static class ConsultClinicalAids
{
    // ---------- NIP / EPI (nip-schedule.ts) ----------
    public static readonly IReadOnlyList<NipScheduleRow> NipSchedule =
    [
        new("At birth", 0, ["BCG", "Hepatitis B (birth dose)"]),
        new("6 weeks", 1.5, ["Pentavalent (DPT-Hib-HepB) dose 1", "OPV dose 1", "PCV dose 1"]),
        new("10 weeks", 2.5, ["Pentavalent dose 2", "OPV dose 2", "PCV dose 2"]),
        new("14 weeks", 3.5, ["Pentavalent dose 3", "OPV dose 3", "PCV dose 3", "IPV"]),
        new("9 months", 9, ["Measles-rubella (MR) dose 1"]),
        new("12 months", 12, ["MR dose 2 (catch-up window)", "PCV booster (if indicated)"]),
        new("15 months", 15, ["Hepatitis A (if in local program)", "Varicella (optional)"]),
        new("4–5 years", 48, ["DTaP booster", "OPV booster", "MMR catch-up"]),
    ];

    private static readonly (string Key, string[] Tokens)[] KeyRules =
    [
        ("bcg", ["bcg"]),
        ("hepatitis b birth", ["hepatitis b", "hep b", "hepb"]),
        ("pentavalent", ["pentavalent", "dpt-hib", "dpt hib"]),
        ("opv", ["opv", "oral polio"]),
        ("pcv", ["pcv", "pneumococcal"]),
        ("ipv", ["ipv", "inactivated polio"]),
        ("measles", ["measles"]),
        ("mr", ["mr", "measles-rubella", "measles rubella"]),
        ("mmr", ["mmr"]),
    ];

    static string NormalizeVaxName(string name) =>
        Regex.Replace(Regex.Replace(name.ToLowerInvariant(), @"\bdose\s*\d+\b", ""), @"\s+", " ").Trim();

    static string InferKey(string label)
    {
        var n = NormalizeVaxName(label);
        foreach (var (key, tokens) in KeyRules) if (tokens.Any(n.Contains)) return key;
        return n.Length == 0 ? "vaccine" : n[..Math.Min(24, n.Length)];
    }

    static readonly IReadOnlyList<(string Key, string Label, double AgeMonthsDue)> NipVaccines =
        NipSchedule.SelectMany(r => r.Vaccines.Select(l => (InferKey(l), l, r.AgeMonthsMin))).ToList();

    static bool IsGivenForKey(string key, IEnumerable<string> givenNames)
    {
        var tokens = KeyRules.FirstOrDefault(r => r.Key == key).Tokens ?? [key];
        return givenNames.Any(raw => { var n = NormalizeVaxName(raw ?? ""); return tokens.Any(n.Contains); });
    }

    /// <summary>Schedule rows due at or before the child's age (rough PH NIP/EPI checklist).</summary>
    public static IReadOnlyList<NipScheduleRow> NipDueForAgeMonths(int ageMonths) =>
        ageMonths < 0 ? [] : NipSchedule.Where(r => r.AgeMonthsMin <= ageMonths).ToList();

    public static IReadOnlyList<NipStatusRow> ComputeNipStatus(int ageMonths, IReadOnlyList<string> givenNames)
    {
        if (ageMonths < 0) return [];
        var sorted = NipVaccines.OrderBy(v => v.AgeMonthsDue).ThenBy(v => v.Label, StringComparer.CurrentCulture).ToList();
        var rows = new List<NipStatusRow>();
        for (var idx = 0; idx < sorted.Count; idx++)
        {
            var e = sorted[idx];
            string status;
            if (IsGivenForKey(e.Key, givenNames)) status = "given";
            else
            {
                double? nextMilestone = sorted.Skip(idx + 1).Where(v => v.AgeMonthsDue > e.AgeMonthsDue).Select(v => (double?)v.AgeMonthsDue).FirstOrDefault();
                if (ageMonths < e.AgeMonthsDue) status = "upcoming";
                else if (ageMonths < e.AgeMonthsDue + 1) status = "due";
                else if (nextMilestone is { } next && ageMonths >= next) status = "catch-up";
                else status = "overdue";
            }
            rows.Add(new NipStatusRow(e.Key, e.Label, e.AgeMonthsDue, status));
        }
        return rows;
    }

    public static short InferDoseNumberFromLabel(string label)
    {
        var m = Regex.Match(label, @"\bdose\s*(\d+)", RegexOptions.IgnoreCase);
        return m.Success && short.TryParse(m.Groups[1].Value, out var d) ? d : (short)1;
    }

    // ---------- ANC lab presets (anc-labs.ts) ----------
    public static readonly IReadOnlyList<AncPreset> AncPresets =
    [
        new("baseline", "ANC Baseline / 1st visit", ["CBC", "Blood type", "Urinalysis", "HBsAg", "HIV", "Syphilis", "FBS"],
            ["CBC", "BLOOD TYPE", "URINALYSIS", "HBsAg", "HIV", "SYPHILIS", "FBS"], ["OGTT"]),
        new("24-28", "ANC 24–28 weeks", ["75g OGTT", "CBC"], ["75g OGTT", "OGTT", "CBC"], []),
        new("36", "ANC 36 weeks", ["CBC", "Urinalysis", "GBS"], ["CBC", "URINALYSIS", "GBS"], []),
    ];

    public static (IReadOnlyList<LabTestItem> CatalogMatches, IReadOnlyList<string> MissingNames) MatchLabTests(IReadOnlyList<LabTestItem> catalog, string presetId)
    {
        var preset = AncPresets.FirstOrDefault(p => p.Id == presetId);
        if (preset is null) return ([], []);
        var matches = new List<LabTestItem>();
        var seen = new HashSet<Guid>();
        var matchedPatterns = new HashSet<string>();
        bool TryPattern(string pattern)
        {
            var p = pattern.ToLowerInvariant();
            var hit = catalog.FirstOrDefault(t => t.Name.ToLowerInvariant().Contains(p));
            if (hit is not null && seen.Add(hit.Id)) { matches.Add(hit); matchedPatterns.Add(p); return true; }
            return false;
        }
        for (var i = 0; i < preset.CatalogPatterns.Count; i++)
        {
            if (preset.Id == "24-28" && i == 0) { if (!TryPattern("75g OGTT")) TryPattern("OGTT"); continue; }
            TryPattern(preset.CatalogPatterns[i]);
        }
        foreach (var p in preset.OptionalCatalogPatterns) TryPattern(p);
        var missing = new List<string>();
        for (var i = 0; i < preset.RequiredNames.Count; i++)
        {
            var name = preset.RequiredNames[i];
            string[] patterns = preset.Id == "24-28" && i == 0 ? ["75g ogtt", "ogtt"]
                : preset.Id == "24-28" && i == 1 ? ["cbc"]
                : [(i < preset.CatalogPatterns.Count ? preset.CatalogPatterns[i] : name).ToLowerInvariant()];
            var covered = patterns.Any(p => matchedPatterns.Contains(p) || matches.Any(t => t.Name.ToLowerInvariant().Contains(p)));
            if (!covered) missing.Add(name);
        }
        return (matches, missing);
    }

    // ---------- growth centiles (growth.ts) ----------
    private sealed record Curve(double AgeMonths, double P3, double P50, double P97);

    private static readonly Curve[] WeightBoys = [new(0, 2.5, 3.3, 4.4), new(6, 6.4, 7.9, 9.8), new(12, 8.0, 9.6, 11.8), new(24, 9.7, 12.2, 15.3), new(36, 11.3, 14.3, 18.0), new(48, 12.7, 16.3, 21.0), new(60, 14.0, 18.3, 24.0)];
    private static readonly Curve[] WeightGirls = [new(0, 2.4, 3.2, 4.2), new(6, 5.8, 7.3, 9.1), new(12, 7.4, 8.9, 11.0), new(24, 9.0, 11.5, 14.8), new(36, 10.5, 13.9, 18.1), new(48, 12.0, 16.1, 21.5), new(60, 13.2, 18.1, 24.8)];
    private static readonly Curve[] HeightBoys = [new(0, 46.3, 49.9, 53.7), new(12, 71.0, 75.7, 80.5), new(24, 81.7, 87.1, 92.5), new(36, 88.7, 96.1, 103.3), new(48, 94.9, 103.3, 111.7), new(60, 100.0, 110.0, 119.2)];
    private static readonly Curve[] HeightGirls = [new(0, 45.6, 49.1, 52.9), new(12, 69.2, 74.0, 79.2), new(24, 80.0, 85.7, 91.4), new(36, 87.4, 95.1, 102.7), new(48, 93.9, 102.7, 111.3), new(60, 99.0, 109.4, 118.9)];
    private static readonly Curve[] HcBoys = [new(0, 32.1, 34.5, 36.9), new(6, 40.0, 43.3, 46.6), new(12, 43.0, 46.1, 49.2), new(24, 45.5, 48.3, 51.1)];
    private static readonly Curve[] HcGirls = [new(0, 31.5, 33.9, 36.2), new(6, 39.0, 42.2, 45.4), new(12, 41.9, 45.0, 48.0), new(24, 44.4, 47.2, 50.0)];

    static bool IsFemale(string? sex) => (sex ?? "").Trim().ToLowerInvariant().StartsWith('f');

    static Curve[]? Table(string? sex, string measure) => measure switch
    {
        "weight" => IsFemale(sex) ? WeightGirls : WeightBoys,
        "height" => IsFemale(sex) ? HeightGirls : HeightBoys,
        "hc" => IsFemale(sex) ? HcGirls : HcBoys,
        _ => null,
    };

    static Curve? Interpolate(Curve[] table, double ageMonths)
    {
        if (ageMonths < 0 || ageMonths > 60 || table.Length == 0) return null;
        if (ageMonths <= table[0].AgeMonths) return table[0];
        if (ageMonths >= table[^1].AgeMonths) return table[^1];
        for (var i = 0; i < table.Length - 1; i++)
        {
            var lo = table[i]; var hi = table[i + 1];
            if (ageMonths >= lo.AgeMonths && ageMonths <= hi.AgeMonths)
            {
                var t = (ageMonths - lo.AgeMonths) / (hi.AgeMonths - lo.AgeMonths);
                return new Curve(ageMonths, lo.P3 + (hi.P3 - lo.P3) * t, lo.P50 + (hi.P50 - lo.P50) * t, lo.P97 + (hi.P97 - lo.P97) * t);
            }
        }
        return null;
    }

    static int JsRound(double v) => (int)Math.Floor(v + 0.5);

    static int ValueToPercentile(double value, Curve c)
    {
        if (value <= c.P3) return Math.Max(1, JsRound(3 * (value / c.P3)));
        if (value >= c.P97) return Math.Min(99, JsRound(97 + 2 * (value - c.P97) / c.P97));
        if (value <= c.P50) return JsRound(3 + (value - c.P3) / (c.P50 - c.P3) * 47);
        return JsRound(50 + (value - c.P50) / (c.P97 - c.P50) * 47);
    }

    public static int? EstimatePercentile(string? sex, double ageMonths, string measure, double value)
    {
        if (!double.IsFinite(value) || value <= 0) return null;
        var table = Table(sex, measure);
        if (table is null) return null;
        var curve = Interpolate(table, ageMonths);
        return curve is null ? null : ValueToPercentile(value, curve);
    }

    /// <summary>Monthly centile curves from birth through maxAge (months), with p15/p85 as midpoints.</summary>
    public static IReadOnlyList<GrowthCurvePoint> GetCurvePoints(string? sex, string measure, int maxAge = 60)
    {
        var table = Table(sex, measure);
        if (table is null) return [];
        var cap = Math.Min(60, Math.Max(0, maxAge));
        var list = new List<GrowthCurvePoint>();
        for (var mo = 0; mo <= cap; mo++)
            if (Interpolate(table, mo) is { } c)
                list.Add(new GrowthCurvePoint(c.AgeMonths, c.P3, (c.P3 + c.P50) / 2, c.P50, (c.P50 + c.P97) / 2, c.P97));
        return list;
    }

    // ---------- peds dosing (peds-dosing.ts) ----------
    public static (double Mg, bool Capped) MgPerKgDose(double mgPerKg, double weightKg, double? maxMg)
    {
        if (!double.IsFinite(mgPerKg) || !double.IsFinite(weightKg) || weightKg <= 0) return (0, false);
        var mg = mgPerKg * weightKg;
        var capped = false;
        if (maxMg is { } max && double.IsFinite(max) && mg > max) { mg = max; capped = true; }
        return (Math.Round(mg * 10, MidpointRounding.AwayFromZero) / 10, capped);
    }

    public static double MlFromConcentration(double mgNeeded, double mgPerMl) =>
        !double.IsFinite(mgNeeded) || !double.IsFinite(mgPerMl) || mgPerMl <= 0 ? 0 : Math.Round(mgNeeded / mgPerMl * 10, MidpointRounding.AwayFromZero) / 10;

    /// <summary>JS Number.parseFloat: leading numeric part, NaN when none.</summary>
    public static double ParseFloat(string? s)
    {
        var m = Regex.Match((s ?? "").Trim(), @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN;
    }

    // ---------- page helpers (consultation2 page.tsx) ----------
    /// <summary>Whole months between the birth date and today (Asia/Manila clinic date).</summary>
    public static int? AgeMonths(DateOnly? dob, DateOnly today)
    {
        if (dob is not { } b) return null;
        var months = (today.Year - b.Year) * 12 + (today.Month - b.Month);
        if (today.Day < b.Day) months--;
        return Math.Max(0, months);
    }

    /// <summary>web vitalFormValue: an exact form key first, then any key containing one of the parts.</summary>
    public static string VitalValue(IEnumerable<VitalLine> vitals, params string[] keys)
    {
        var list = vitals.ToList();
        foreach (var k in keys)
            if (list.FirstOrDefault(v => v.FormKey == k)?.Value?.Trim() is { Length: > 0 } exact) return exact;
        foreach (var v in list)
            if (keys.Any(part => v.FormKey.Contains(part, StringComparison.Ordinal)) && v.Value?.Trim() is { Length: > 0 } val) return val;
        return "";
    }

    public static string? PedsVitalRangeNote(int? ageYears) => ageYears switch
    {
        null or >= 18 => null,
        < 1 => "Pediatric normals (approx): HR 100–160 / RR 30–60",
        < 3 => "Pediatric normals (approx): HR 90–150 / RR 24–40",
        < 6 => "Pediatric normals (approx): HR 80–140 / RR 22–34",
        < 12 => "Pediatric normals (approx): HR 70–120 / RR 18–30",
        _ => "Pediatric normals (approx): HR 60–100 / RR 12–20",
    };

    public static IReadOnlyList<string> GrowthPercentileNotes(string? sex, int? ageMonths, IEnumerable<VitalLine> vitals)
    {
        if (ageMonths is not { } mo || mo > 60) return [];
        var list = vitals.ToList();
        var notes = new List<string>();
        var w = ParseFloat(VitalValue(list, "weight"));
        var h = ParseFloat(VitalValue(list, "height"));
        var hc = ParseFloat(VitalValue(list, "head", "hc"));
        if (double.IsFinite(w) && EstimatePercentile(sex, mo, "weight", w) is { } pw) notes.Add($"Weight ≈ {pw}th %ile");
        if (double.IsFinite(h) && EstimatePercentile(sex, mo, "height", h) is { } ph) notes.Add($"Height ≈ {ph}th %ile");
        if (double.IsFinite(hc) && EstimatePercentile(sex, mo, "hc", hc) is { } phc) notes.Add($"HC ≈ {phc}th %ile");
        return notes;
    }

    /// <summary>lib/pf-default.ts followUpViewLine (PRN shows no date and says PRN once).</summary>
    public static string FollowUpViewLine(ConsultationDraft d, Func<DateOnly, string> formatDate)
    {
        if (d.FollowUpMode == FollowUpMode.None || d.FollowUpDate is not { } date) return "No follow-up recorded.";
        if (d.FollowUpMode == FollowUpMode.Prn || ConsultRules.IsPrnFollowUp(d.FollowUpReason, d.FollowUpInstructions))
        {
            var text = (d.FollowUpInstructions ?? "").Trim() is { Length: > 0 } ins ? ins : (d.FollowUpReason ?? "").Trim();
            return text.Length > 0 ? text : "PRN — return if needed";
        }
        var reason = (d.FollowUpReason ?? "").Trim();
        var instructions = (d.FollowUpInstructions ?? "").Trim();
        return $"{formatDate(date)}{(reason.Length > 0 ? " — " + reason : "")}{(instructions.Length > 0 ? $" ({instructions})" : "")}{(d.FollowUpReminder ? " · reminder on" : "")}";
    }

    /// <summary>consultation2 formatProfessionalFeeLine (completed view + "Consultation saved").</summary>
    public static string ProfessionalFeeLine(string? decision, decimal? amount, string? waiveReason, decimal? computedTotal)
    {
        if (decision == "Waive")
        {
            var reason = (waiveReason ?? "").Trim();
            return reason.Length > 0 ? $"Professional fee: Waive — {reason}" : "Professional fee: Waive";
        }
        if (decision == "Charge")
        {
            if (amount is { } n) return $"Professional fee: Charge {FeeCalculator.FormatPeso(n)}";
            return computedTotal is { } t ? $"Professional fee: Charge (Total: {FeeCalculator.FormatPeso(t)})" : "Professional fee: Charge";
        }
        return "Professional fee: Decide at desk";
    }
}
//JJQR - END
