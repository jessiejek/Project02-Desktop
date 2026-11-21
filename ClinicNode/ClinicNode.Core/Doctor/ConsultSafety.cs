//JJQR - AI (21 Nov 2026) - 1:1 ports of the web consultation's pure rules (no clinical content added):
//   DrugSafety       ← Project02-fe/src/lib/clinical/drug-safety.ts (checkDrugSafety; tables copied verbatim)
//   RxAllergy        ← PrescriptionForm.tsx allergyBlockReason
//   TemplateSpecialty← Project02-fe/src/lib/clinical/template-specialty.ts (tagTemplate / defaultSpecialtyFromDoctor / matchesSpecialtyFilter)
//   RenalCue         ← consultation2/[bookingId]/page.tsx renalCueText (profile fallback; see note)
using System.Globalization;
using System.Text.RegularExpressions;

namespace ClinicNode.Core.Doctor;

public sealed record DrugSafetyAlert(string Level, string Message); // Level: "warn" | "block"

public static class DrugSafety
{
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

    static readonly string[] PregnancyBlocks = ["isotretinoin", "methotrexate", "warfarin", "misoprostol"];

    static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();

    static bool DrugMatches(string name, string token)
    {
        var n = Norm(name);
        var t = Norm(token);
        if (n.Length == 0 || t.Length == 0) return false;
        return n.Contains(t, StringComparison.Ordinal) || t.Contains(n, StringComparison.Ordinal);
    }

    //JJQR - AI (Nov 21, 2026) - child cautions (a warning, never a block: the doctor decides). Template doses are ADULT doses, so any medicine for a child also gets a dose reminder.
    public static readonly (string Match, int UnderYears, string Note)[] ChildCautions =
    [
        ("loperamide", 12, "Loperamide is not recommended under 12 (contraindicated under 2). Check this is appropriate for this child."),
        ("aspirin", 16, "Aspirin under 16: risk of Reye syndrome. Use paracetamol unless there is a specific indication."),
        ("codeine", 12, "Codeine is not recommended under 12."),
        ("tramadol", 12, "Tramadol is not recommended under 12."),
        ("doxycycline", 8, "Tetracyclines under 8: tooth discolouration. Consider an alternative."),
        ("tetracycline", 8, "Tetracyclines under 8: tooth discolouration. Consider an alternative."),
        ("ciprofloxacin", 18, "Fluoroquinolone in a child: joint/tendon risk. Use only if no alternative."),
        ("levofloxacin", 18, "Fluoroquinolone in a child: joint/tendon risk. Use only if no alternative."),
        ("ofloxacin", 18, "Fluoroquinolone in a child: joint/tendon risk. Use only if no alternative."),
    ];

    public static IReadOnlyList<DrugSafetyAlert> Check(IEnumerable<string?> genericNames, bool pregnant = false, int? trimester = null, int? ageYears = null)
    {
        var names = genericNames.Select(Norm).Where(n => n.Length > 0).ToList();
        var outList = new List<DrugSafetyAlert>();
        var seen = new HashSet<string>();
        void Push(string level, string message)
        {
            if (!seen.Add($"{level}:{message}")) return;
            outList.Add(new DrugSafetyAlert(level, message));
        }

        foreach (var group in DuplicateTherapyGroups)
        {
            var hits = names.Where(n => group.Any(g => DrugMatches(n, g))).ToList();
            if (hits.Count >= 2)
                Push("warn", $"Duplicate therapy ({string.Join("/", group.Take(3))}…): {string.Join(", ", hits.Distinct())}");
        }

        foreach (var pair in InteractionPairs)
            if (names.Any(n => DrugMatches(n, pair.A)) && names.Any(n => DrugMatches(n, pair.B)))
                Push("warn", pair.Note);

        //JJQR - AI (Nov 21, 2026) - child cautions + dose reminder
        if (ageYears is < 18)
        {
            foreach (var c in ChildCautions)
                if (ageYears < c.UnderYears && names.Any(n => DrugMatches(n, c.Match))) Push("warn", c.Note);
            if (names.Count > 0) Push("warn", $"Child patient ({ageYears} y): confirm each dose by weight. Template doses are adult doses.");
        }
        //JJQR - END

        if (pregnant)
        {
            foreach (var n in names)
                if (PregnancyBlocks.Any(b => DrugMatches(n, b)))
                    Push("block", $"Pregnancy: {n} is contraindicated (teratogen/high risk).");
            foreach (var entry in PregnancyUnsafe)
            {
                if (entry.Trimester == "3" && trimester is { } tri && tri < 3) continue;
                var hit = names.FirstOrDefault(n => DrugMatches(n, entry.Match));
                if (hit is null) continue;
                if (PregnancyBlocks.Any(b => DrugMatches(hit, b))) continue;
                Push("warn", $"Pregnancy: {entry.Note}");
            }
        }
        return outList;
    }
}

public static class RxAllergy
{
    /// <summary>web allergyBlockReason: the matching allergen (trimmed) when the generic name and an allergen contain each other.</summary>
    //JJQR - AI (Nov 22, 2026) - an allergy to a drug CLASS must block its members ("Penicillin" allergy vs "Amoxicillin" was not caught: names never contain each other) //JJQR - END
    static readonly (string Class, string[] Members)[] Classes =
    [
        ("penicillin", ["penicillin", "amoxicillin", "ampicillin", "cloxacillin", "dicloxacillin", "flucloxacillin", "oxacillin", "piperacillin", "amoxiclav", "co-amoxiclav", "clavulan"]),
        ("cephalosporin", ["cef", "ceph"]),
        ("sulfa", ["sulfa", "sulfonamide", "cotrimoxazole", "co-trimoxazole", "trimethoprim"]),
        ("nsaid", ["ibuprofen", "mefenamic", "diclofenac", "naproxen", "ketorolac", "celecoxib", "etoricoxib", "aspirin", "indomethacin", "meloxicam"]),
        ("macrolide", ["azithromycin", "clarithromycin", "erythromycin"]),
        ("quinolone", ["ciprofloxacin", "levofloxacin", "moxifloxacin", "ofloxacin", "floxacin"]),
    ];

    public static string? BlockReason(string? genericName, IEnumerable<string?> patientAllergies)
    {
        var g = (genericName ?? "").Trim().ToLowerInvariant();
        if (g.Length == 0) return null;
        foreach (var raw in patientAllergies)
        {
            var al = (raw ?? "").Trim().ToLowerInvariant();
            if (al.Length == 0) continue;
            if (g.Contains(al, StringComparison.Ordinal) || al.Contains(g, StringComparison.Ordinal)) return raw!.Trim();
            foreach (var (cls, members) in Classes)
            {
                var allergyInClass = al.Contains(cls, StringComparison.Ordinal) || members.Any(m => al.Contains(m, StringComparison.Ordinal));
                if (allergyInClass && members.Any(m => g.Contains(m, StringComparison.Ordinal))) return raw!.Trim();
            }
        }
        return null;
    }
}

public static class TemplateSpecialty
{
    public const string All = "All";
    public static readonly (string Id, string Label)[] ChipLabels = [("All", "All"), ("General", "Adult"), ("Peds", "Pedia")]; //JJQR - AI (Nov 21, 2026) - family medicine: adult and pedia only (OB / IM removed; their templates are Adult now) //JJQR - END

    static readonly (Regex Match, string[] Tags)[] SoapTitleTags =
    [
        (new Regex("pediatric|peds|child|infant|uri", RegexOptions.IgnoreCase), ["Peds", "General"]),
        (new Regex("prenatal|anc|obstet|pregnancy|postpartum|labor", RegexOptions.IgnoreCase), ["General"]),
        (new Regex(@"hypertension|diabetes|dm\b|cad|copd|asthma adult|internal", RegexOptions.IgnoreCase), ["General"]),
        (new Regex("uri|upper respiratory|pharyng", RegexOptions.IgnoreCase), ["General", "Peds"]),
    ];

    /// <summary>tagTemplate("soap", title).</summary>
    public static IReadOnlyCollection<string> SoapTags(string? title)
    {
        var t = (title ?? "").Trim();
        if (t.Length == 0) return ["General"];
        var set = new HashSet<string>();
        foreach (var r in SoapTitleTags)
            if (r.Match.IsMatch(t)) foreach (var tag in r.Tags) set.Add(tag);
        if (set.Count == 0) set.Add("General");
        return set;
    }

    public static string DefaultFromDoctor(string? specialization)
    {
        var s = (specialization ?? "").ToLowerInvariant();
        if (s.Contains("pediatr")) return "Peds";
        return "General";
    }

    public static bool MatchesSoap(string? title, string filter) => filter == All || SoapTags(title).Contains(filter);

    /// <summary>The saved group wins (Adult = "General" chip, Pedia = "Peds" chip); a template without one is grouped from its title as before.</summary>
    public static bool MatchesSoap(string? title, string? category, string filter) => category switch
    {
        "Adult" => filter is All or "General",
        "Pedia" => filter is All or "Peds",
        _ => filter == All || (DefaultCategory(title) == "Pedia" ? filter == "Peds" : filter == "General"),
    };

    /// <summary>What the editor pre-selects for a template that has no group yet.</summary>
    public static string DefaultCategory(string? title) => new Regex(@"pediatric|\bpeds?\b|child|infant|newborn|toddler", RegexOptions.IgnoreCase).IsMatch(title ?? "") ? "Pedia" : "Adult";
}

public static class RenalCue
{
    /// <summary>
    /// web renalCueText. The web first looks for a lab-values row carrying creatinine_mg_dl / egfr; GET /api/patients/{id}/lab-values
    /// returns PatientLabValue rows (test_code / value / unit / observed_at) without those fields, so on the web that branch never fires
    /// and the cue always comes from the patient profile — which is what this ports.
    /// </summary>
    public static string? Text(PatientClinicalFlags? p)
    {
        if (p is null || (p.LastCreatinineMgDl is null && p.LastEgfr is null)) return null;
        var cr = p.LastCreatinineMgDl?.ToString("0.##########", CultureInfo.InvariantCulture) ?? "—";
        var egfr = p.LastEgfr?.ToString("0.##########", CultureInfo.InvariantCulture) ?? "—";
        var date = p.LastRenalLabsDate is { } d ? $" ({d:yyyy-MM-dd})" : "";
        return $"Last renal: Cr {cr} mg/dL, eGFR {egfr}{date}";
    }
}
//JJQR - END

//JJQR - AI (21 Nov 2026) - web VitalsEditor.tsx vitalOutOfRange (adult default ranges by form key, copied verbatim)
public static class VitalRanges
{
    static readonly Dictionary<string, (double Min, double Max)> SingleRanges = new()
    {
        ["pulse_rate"] = (60, 100),
        ["temperature"] = (36.1, 37.2),
        ["respiratory_rate"] = (12, 20),
        ["o2_saturation"] = (95, 100),
        ["oxygen_saturation"] = (95, 100),
        ["spo2"] = (95, 100),
        ["weight"] = (35, 200),
        ["blood_pressure_systolic"] = (90, 140),
        ["blood_pressure_diastolic"] = (60, 90),
    };
    static readonly (double Min, double Max) Systolic = (90, 140);
    static readonly (double Min, double Max) Diastolic = (60, 90);

    static double? Num(string raw)
    {
        var cleaned = new string((raw ?? "").Where(c => char.IsDigit(c) || c == '.').ToArray());
        // parseFloat reads the leading number only
        var m = Regex.Match(cleaned, @"^\d*\.?\d+|^\d+");
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }

    static bool Within(double n, (double Min, double Max) r) => n >= r.Min && n <= r.Max;

    //JJQR - AI (Nov 21, 2026) - child ranges (under 18): adult limits flagged normal children (weight 26 kg, breathing 24/min). Weight and height are judged on the growth chart, not here.
    static readonly Dictionary<string, (double Min, double Max)> ChildRanges = new()
    {
        ["pulse_rate"] = (70, 120),
        ["respiratory_rate"] = (18, 30),
        ["temperature"] = (36.1, 37.5),
        ["o2_saturation"] = (95, 100),
        ["oxygen_saturation"] = (95, 100),
        ["spo2"] = (95, 100),
    };

    public static bool OutOfRange(string? formKey, string? value, int? ageYears)
    {
        if (ageYears is not < 18) return OutOfRange(formKey, value);
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0) return false;
        var key = (formKey ?? "").ToLowerInvariant();
        if (key is "weight" or "height" or "head_circumference" or "length") return false;
        if (key == "blood_pressure")
        {
            var parts = trimmed.Split('/').Select(p => p.Trim()).ToArray();
            if (parts.Length >= 2)
                return (Num(parts[0]) is { } sys && !Within(sys, (80, 125))) || (Num(parts[1]) is { } dia && !Within(dia, (45, 85)));
            return Num(trimmed) is { } lone && !Within(lone, (80, 125));
        }
        if (ChildRanges.TryGetValue(key, out var r)) return Num(trimmed) is { } n && !Within(n, r);
        return OutOfRange(formKey, value);
    }
    //JJQR - END

    public static bool OutOfRange(string? formKey, string? value)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0) return false;
        var key = (formKey ?? "").ToLowerInvariant();
        if (key == "blood_pressure")
        {
            var parts = trimmed.Split('/').Select(p => p.Trim()).ToArray();
            if (parts.Length >= 2)
            {
                if (Num(parts[0]) is { } sys && !Within(sys, Systolic)) return true;
                if (Num(parts[1]) is { } dia && !Within(dia, Diastolic)) return true;
                return false;
            }
            return Num(trimmed) is { } lone && !Within(lone, Systolic);
        }
        if (!SingleRanges.TryGetValue(key, out var range)) return false;
        return Num(trimmed) is { } n && !Within(n, range);
    }
}
//JJQR - END
