//JJQR - AI (Nov 21, 2026) - line-for-line port of Project02-fe src/lib/rx-sig-parse.ts and timingToSlots (rx-structured.ts). Regexes use ECMAScript semantics so \b, \d and /i match the browser.
using System.Text.RegularExpressions;

namespace ClinicNode.Core.Printing;

public sealed record RxSigResult(string? Meal, IReadOnlyList<string> Slots, bool AsNeeded, bool AsDirected);

public static class RxSig
{
    public const string Breakfast = "BREAKFAST";
    public const string Lunch = "LUNCH";
    public const string Dinner = "DINNER";
    public const string Bedtime = "BEDTIME";

    static readonly string[] SlotOrder = [Breakfast, Lunch, Dinner, Bedtime];
    static readonly string[] SlotLabels = ["Breakfast", "Lunch", "Dinner", "Bedtime"];

    const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.ECMAScript | RegexOptions.CultureInvariant;
    static bool T(string text, string pattern) => Regex.IsMatch(text, pattern, I);

    static readonly Regex StrengthToken = new(@"^\d+(?:\.\d+)?(?:mg(?:\/\d+(?:\.\d+)?ml)?|mcg|ml|iu|g|%)$", I);
    static readonly Regex StrengthPrefix = new(@"^\s*\d+(?:\.\d+)?\s*(?:mg(?:\s*\/\s*\d+(?:\.\d+)?\s*ml)?|mcg|ml|iu|g|%)\s*", I);
    static readonly Regex Ws = new(@"\s+");

    /// <summary>rx-structured.ts timingToSlots: "Breakfast, Dinner" → ["Breakfast","Dinner"] in slot order.</summary>
    public static IReadOnlyList<string> TimingToSlots(string? timing)
    {
        var raw = (timing ?? "").Trim();
        if (raw.Length == 0) return [];
        var seen = new HashSet<string>();
        foreach (var part in raw.Split(','))
        {
            var p = part.Trim().ToLowerInvariant();
            foreach (var l in SlotLabels)
                if (l.ToLowerInvariant() == p) seen.Add(l);
        }
        return SlotLabels.Where(seen.Contains).ToList();
    }

    static string CombineText(string? instruction, string? route)
    {
        var a = (instruction ?? "").Trim();
        var b = (route ?? "").Trim();
        if (a.Length == 0) return b;
        if (b.Length == 0) return a;
        return a + " " + b;
    }

    /// <summary>Returns (found, meal).</summary>
    static (bool Found, string? Meal) ParseStructuredMeal(string? mealRelation)
    {
        var m = (mealRelation ?? "").Trim().ToLowerInvariant();
        return m is "before" or "after" ? (true, m) : (false, null);
    }

    static bool HasBeforeMeal(string text) =>
        T(text, @"\bbefore\s+meals?\b") ||
        T(text, @"\bbefore\s+eating\b") ||
        T(text, @"\bbefore\s+food\b") ||
        T(text, @"\bbefore\s+(?:breakfast|lunch|dinner|supper)\b") ||
        T(text, @"\ba\.c\.") ||
        T(text, @"\bac\b") ||
        T(text, @"\bempty\s+stomach\b") ||
        T(text, @"\d+\s*(?:min|mins|minutes)\s+before\b") ||
        T(text, @"\b1\s+hour\s+before\b");

    static string StripNonMealAfter(string text)
    {
        text = Regex.Replace(text, @"\bafter\s+each\s+loose\s+stool\b", " ", I);
        text = Regex.Replace(text, @"\bafter\s+each\s+bowel\s+movement\b", " ", I);
        text = Regex.Replace(text, @"\bafter\s+vomiting\b", " ", I);
        return text;
    }

    static bool HasAfterMeal(string text)
    {
        var t = StripNonMealAfter(text);
        return T(t, @"\bafter\s+meals?\b") ||
               T(t, @"\bafter\s+eating\b") ||
               T(t, @"\bafter\s+food\b") ||
               T(t, @"\bafter\s+(?:breakfast|lunch|dinner|supper)\b") ||
               T(t, @"\bp\.c\.") ||
               T(t, @"\bpc\b") ||
               T(t, @"\bwith\s+food\b") ||
               T(t, @"\bwith\s+meals?\b") ||
               T(t, @"\bwith\s+(?:breakfast|lunch|dinner|supper)\b") ||
               T(t, @"\btaken\s+with\s+food\b") ||
               T(t, @"\bafter\s+each\s+meal\b");
    }

    static string? MealFromText(string text)
    {
        var before = HasBeforeMeal(text);
        var after = HasAfterMeal(text);
        if (before && after) return null;
        if (before) return "before";
        if (after) return "after";
        return null;
    }

    static List<string> Ordered(HashSet<string> seen) => SlotOrder.Where(seen.Contains).ToList();

    static List<string> ParseStructuredSlots(string? timing)
    {
        var raw = (timing ?? "").Trim();
        if (raw.Length == 0) return [];
        var seen = new HashSet<string>();
        foreach (var part in raw.Split(','))
        {
            var p = part.Trim().ToLowerInvariant();
            if (p == "breakfast") seen.Add(Breakfast);
            else if (p == "lunch") seen.Add(Lunch);
            else if (p == "dinner") seen.Add(Dinner);
            else if (p == "bedtime") seen.Add(Bedtime);
        }
        return Ordered(seen);
    }

    static bool HasNightHint(string text) =>
        T(text, @"\bat\s+night\b") ||
        T(text, @"\bnight\b") ||
        T(text, @"\bbedtime\b") ||
        T(text, @"\bqhs\b") ||
        T(text, @"\bhs\b") ||
        T(text, @"\bh\.s\.") ||
        T(text, @"\bbefore\s+sleep\b") ||
        T(text, @"\bat\s+bed(?:time)?\b");

    static List<string> ExplicitSlotsFromText(string text)
    {
        var seen = new HashSet<string>();
        if (T(text, @"\bevery\s+meal\b") ||
            T(text, @"\beach\s+meal\b") ||
            T(text, @"\bwith\s+meals?\b") ||
            T(text, @"\bafter\s+meals?\b") ||
            T(text, @"\bbefore\s+meals?\b") ||
            T(text, @"\bthree\s+meals?\b"))
        {
            seen.Add(Breakfast);
            seen.Add(Lunch);
            seen.Add(Dinner);
        }
        if (T(text, @"\bmorning\b") || T(text, @"\bbreakfast\b") || T(text, @"\bumaga\b") || T(text, @"\d\s*am\b"))
            seen.Add(Breakfast);
        if (T(text, @"\bnoon\b") || T(text, @"\bmidday\b") || T(text, @"\blunch\b") || T(text, @"\btanghali\b"))
            seen.Add(Lunch);
        if (T(text, @"\bevening\b") || T(text, @"\bdinner\b") || T(text, @"\bsupper\b"))
            seen.Add(Dinner);
        if (HasNightHint(text))
            seen.Add(Bedtime);
        return Ordered(seen);
    }

    static int DetectFrequency(string text)
    {
        if (T(text, @"\bqid\b") ||
            T(text, @"\bfour\s+times\b") ||
            T(text, @"\b4x\s*\/\s*day\b") ||
            T(text, @"\b4\s*x\s*\/\s*day\b") ||
            T(text, @"\bevery\s+6\s+hours?\b") ||
            T(text, @"\bq6h\b") ||
            T(text, @"\bq4h\b") ||
            T(text, @"\bevery\s+4\s+hours?\b"))
            return 4;
        if (T(text, @"\btid\b") ||
            T(text, @"\bthrice\b") ||
            T(text, @"\bthree\s+times\b") ||
            T(text, @"\b3x\s*\/\s*day\b") ||
            T(text, @"\b3\s*x\s*\/\s*day\b") ||
            T(text, @"\bevery\s+8\s+hours?\b") ||
            T(text, @"\bq8h\b"))
            return 3;
        if (T(text, @"\bbid\b") ||
            T(text, @"\btwice\s+daily\b") ||
            T(text, @"\btwice\s+a\s+day\b") ||
            T(text, @"\b2x\s*\/\s*day\b") ||
            T(text, @"\b2\s*x\s*\/\s*day\b") ||
            T(text, @"\btwo\s+times\s+a\s+day\b") ||
            T(text, @"\bevery\s+12\s+hours?\b") ||
            T(text, @"\bq12h\b"))
            return 2;
        if (T(text, @"\bonce\s+daily\b") ||
            T(text, @"\bonce\s+a\s+day\b") ||
            T(text, @"\bod\b") ||
            T(text, @"\bqd\b") ||
            T(text, @"\b1x\s*\/\s*day\b") ||
            T(text, @"\b1\s*x\s*\/\s*day\b") ||
            T(text, @"\b1x\s+a\s+day\b") ||
            T(text, @"\b1\s*x\s+a\s+day\b") ||
            T(text, @"\bdaily\b") ||
            T(text, @"\bevery\s+day\b") ||
            T(text, @"\bevery\s+24\s+hours?\b") ||
            T(text, @"\bq24h\b"))
            return 1;
        return 0;
    }

    static List<string> FrequencyDefault(int freq, string text) => freq switch
    {
        1 => HasNightHint(text) ? [Bedtime] : [Breakfast],
        2 => [Breakfast, Dinner],
        3 => [Breakfast, Lunch, Dinner],
        4 => [Breakfast, Lunch, Dinner, Bedtime],
        _ => [],
    };

    static List<string> SlotsFromText(string text, int freq)
    {
        var expl = ExplicitSlotsFromText(text);
        if (freq == 0 || expl.Count >= freq)
        {
            if (freq > 0 && expl.Count == 3 && !HasSpecificTimeWord(text)) return FrequencyDefault(freq, text);
            return expl;
        }
        return FrequencyDefault(freq, text);
    }

    static bool HasSpecificTimeWord(string text) =>
        T(text, @"\b(?:morning|breakfast|umaga|noon|midday|lunch|tanghali|evening|dinner|supper)\b") ||
        T(text, @"\d\s*am\b") ||
        HasNightHint(text);

    static bool DetectAsNeeded(string text, bool hasFreq)
    {
        if (T(text, @"\bprn\b")) return true;
        if (T(text, @"\bp\.r\.n\.?")) return true;
        if (T(text, @"\bas\s+needed\b")) return true;
        if (T(text, @"\bwhen\s+needed\b")) return true;
        if (T(text, @"\bif\s+needed\b")) return true;
        if (T(text, @"\bwhen\s+necessary\b")) return true;
        if (T(text, @"\bif\s+necessary\b")) return true;
        if (T(text, @"\bsos\b")) return true;
        if (T(text, @"\bafter\s+each\s+loose\s+stool\b")) return true;
        if (T(text, @"\beach\s+loose\s+stool\b")) return true;
        if (T(text, @"\beach\s+bowel\s+movement\b")) return true;
        if (T(text, @"\bstat\b")) return true;
        if (!hasFreq && (T(text, @"\bfor\s+fever\b") || T(text, @"\bfor\s+pain\b"))) return true;
        return false;
    }

    public static RxSigResult Parse(string? instruction, string? route = null, string? timing = null, string? mealRelation = null)
    {
        var text = CombineText(instruction, route);
        var (found, structuredMeal) = ParseStructuredMeal(mealRelation);
        var meal = found ? structuredMeal : MealFromText(text);
        var structuredSlots = ParseStructuredSlots(timing);
        var freq = DetectFrequency(text);
        var asNeeded = DetectAsNeeded(text, freq > 0);
        List<string> slots;
        if (structuredSlots.Count > 0) slots = structuredSlots;
        else if (asNeeded) slots = [];
        else slots = SlotsFromText(text, freq);
        var asDirected = !asNeeded && slots.Count == 0 && text.Length > 0;
        return new RxSigResult(meal, slots, asNeeded, asDirected);
    }

    static string CollapseWs(string s) => Ws.Replace(s, " ").Trim();

    static string NormalizeForCompare(string s) =>
        Regex.Replace(CollapseWs(s).ToLowerInvariant(), @"(\d)\s+(?=[a-z%])", "$1", I);

    static string[] Tokens(string s)
    {
        var t = NormalizeForCompare(s);
        return t.Length == 0 ? [] : t.Split(' ');
    }

    static bool ContainsSeq(string[] hay, string[] needle)
    {
        if (needle.Length == 0) return true;
        if (needle.Length > hay.Length) return false;
        for (var i = 0; i <= hay.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
                if (hay[i + j] != needle[j]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }

    static bool EveryTokenIn(string[] needles, string[] hay) => needles.All(n => hay.Contains(n));

    /// <summary>rx-sig-parse.ts rxDisplayName: drop a dosage already spelled in the generic name.</summary>
    public static string DisplayName(string genericName, string dosage)
    {
        var name = CollapseWs(genericName);
        var dose = CollapseWs(dosage);
        if (dose.Length == 0) return name;
        var nameToks = Tokens(name);
        var doseToks = Tokens(dose);
        if (ContainsSeq(nameToks, doseToks) || EveryTokenIn(doseToks, nameToks)) return name;
        if (doseToks.Length > 0 && StrengthToken.IsMatch(doseToks[0]) && nameToks.Contains(doseToks[0]))
        {
            var rest = StrengthPrefix.Replace(dose, "", 1).Trim();
            if (rest.Length == 0 || EveryTokenIn(Tokens(rest), nameToks)) return name;
            return name + " " + rest;
        }
        var perIntake = Regex.Match(dose, @"^\d+(?:\.\d+)?\s*([a-z]+)$", I);
        if (perIntake.Success && nameToks.Length > 1 && nameToks[^1] == perIntake.Groups[1].Value.ToLowerInvariant())
            return name;
        return string.Join(" ", new[] { genericName, dosage }.Where(s => !string.IsNullOrEmpty(s)));
    }
}
//JJQR - END
