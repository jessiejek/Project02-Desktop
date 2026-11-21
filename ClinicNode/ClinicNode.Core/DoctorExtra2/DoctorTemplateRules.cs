//JJQR - AI (21 Nov 2026) - templates workspace helpers, 1:1 port of Project02-fe lib/templates/validation.ts, format.ts, search.ts (matchesQuery / snippet),
// reorder.ts (moveId), urlState.ts (tabs, phrase fields; the Diagnoses tab stays hidden like SHOW_DIAGNOSES_TAB = false) and RxTab.tsx (ROUTES, TIMING_SLOTS).
using System.Text.RegularExpressions;

namespace ClinicNode.Core.DoctorExtra;

public sealed record TplSearchHit(string Tab, Guid Id, string Title, string Snippet, string? Field);

public static partial class DoctorTemplateRules
{
    /// <summary>VISIBLE_TEMPLATE_TABS + TAB_LABELS (Diagnoses hidden on the web).</summary>
    public static readonly (string Id, string Label)[] Tabs =
        [("soap", "SOAP"), ("phrases", "Phrases"), ("rx", "Prescriptions"), ("favorites", "Favorites"), ("certificates", "Certificates")];

    /// <summary>PHRASE_FIELDS: DB enum casing → label.</summary>
    public static readonly (string Value, string Label)[] PhraseFields =
        [("ChiefComplaint", "Chief complaint"), ("Subjective", "Subjective"), ("Objective", "Objective"), ("Assessment", "Assessment"), ("Plan", "Plan")];

    public static readonly string[] Routes = ["PO", "IV", "IM", "SC", "SL", "Topical", "Inhaled", "Nasal", "Ophthalmic", "Otic", "PR"];
    public static readonly string[] TimingSlots = ["Breakfast", "Lunch", "Dinner", "Bedtime"];
    public static readonly string[] MealOptions = ["Before", "After"];
    public static readonly string[] DurationKinds = ["Maintain", "Days", "Weeks"];

    public const string CopyPrefix = "Copy of ";
    public const string NilGuid = "00000000-0000-0000-0000-000000000000";

    public const string RestoreTitle = "Restore built-in templates?";
    public const string RestoreBody = "This adds back any built-in templates you deleted. Your own templates and edits are not changed.";
    public const string DeleteTitle = "Delete this item?";

    public static string DeleteBody(string label) => $"“{label}” will be removed. You can undo for 5 seconds after deleting.";

    public static string RestoredToast(int restored) =>
        restored > 0 ? $"Restored {restored} template(s)" : "Nothing to restore — you already have all built-in templates.";

    static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);

    // ── validation.ts ("why can't I save?"; null = OK) ─────────────────────
    public static string? ValidateSoap(string? title, params string?[] fields) =>
        Blank(title) ? "Give the template a name." : fields.All(Blank) ? "Add text to at least one SOAP field." : null;

    public static string? ValidatePhrase(string? label, string? body)
    {
        if (Blank(label)) return "Give the phrase a name.";
        if (Blank(body)) return "Add the phrase text.";
        if (label!.Trim().Length > 200) return "Phrase name is too long (max 200 characters).";
        if (body!.Trim().Length > 4000) return "Phrase text is too long (max 4000 characters).";
        return null;
    }

    public static string? ValidateRxSet(string? title, IEnumerable<string?> genericNames) =>
        Blank(title) ? "Give the prescription set a name." : !genericNames.Any(n => !Blank(n)) ? "Add at least one medicine." : null;

    public static string? ValidateFavorite(string? genericName) => Blank(genericName) ? "Choose or type a medicine." : null;

    public static string? ValidateCert(string? title, params string?[] fields) =>
        Blank(title) ? "Give the certificate template a name." : fields.All(Blank) ? "Fill in at least one certificate field." : null;

    // ── format.ts ─────────────────────────────────────────────────────────
    public static string PhraseFieldLabel(string value) =>
        PhraseFields.FirstOrDefault(f => f.Value == value) is { Label: { } l } ? l : value;

    public static bool IsCopyTitle(string title) => title.StartsWith(CopyPrefix, StringComparison.Ordinal);

    /// <summary>"Copy of X", then "Copy of X (2)", … so a duplicate never looks like the original.</summary>
    public static string CopyTitle(string title, IEnumerable<string> existing)
    {
        var b = IsCopyTitle(title) ? title : CopyPrefix + title;
        var taken = new HashSet<string>(existing.Select(t => t.ToLowerInvariant()));
        if (!taken.Contains(b.ToLowerInvariant())) return b;
        for (var n = 2; n < 100; n++)
            if (!taken.Contains($"{b} ({n})".ToLowerInvariant())) return $"{b} ({n})";
        return $"{b} ({DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()})";
    }

    /// <summary>"AMOXICILLIN 500MG" → "Amoxicillin 500mg". Text that already has lowercase letters is left alone.</summary>
    public static string NormalCase(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return "";
        if (t != t.ToUpperInvariant()) return t;
        var lower = t.ToLowerInvariant();
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    /// <summary>"Amoxicillin 500 mg — 3 times a day, after meals · 7 days".</summary>
    public static string RxSig(string genericName, string? dosage, string? instruction, string? timing, string? mealRelation, string? durationKind, int? durationValue)
    {
        var name = NormalCase(genericName);
        var dose = NormalCase(dosage);
        var meal = string.IsNullOrEmpty(mealRelation) ? "" : $"{mealRelation.ToLowerInvariant()} meals";
        var frequency = string.Join(", ", new[] { NormalCase(instruction), NormalCase(timing), meal }.Where(s => s.Length > 0));
        var duration = durationKind == "Maintain" ? "maintenance"
            : !string.IsNullOrEmpty(durationKind) && durationValue is { } v && v != 0 ? $"{v} {durationKind.ToLowerInvariant()}" : "";
        var head = string.Join(" ", new[] { name, dose }.Where(s => s.Length > 0));
        var tail = string.Join(" · ", new[] { frequency, duration }.Where(s => s.Length > 0));
        return tail.Length > 0 ? $"{head} — {tail}" : head;
    }

    public static string RxSig(TplRxLine l) => RxSig(l.GenericName, l.Dosage, l.Instruction, l.Timing, l.MealRelation, l.DurationKind, l.DurationValue);

    /// <summary>First few words of the text, as a phrase name (max 5 words / 40 chars, trailing punctuation dropped).</summary>
    public static string PhraseNameFromText(string? text)
    {
        var words = string.Join(" ", (text ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(5));
        var clipped = words;
        if (words.Length > 40)
        {
            var cut = TrailingWordRx().Replace(words[..40], "");
            clipped = cut.Length > 0 ? cut : words[..40];
        }
        var name = TrailingPunctRx().Replace(clipped, "");
        return name.Length == 0 ? "" : char.ToUpperInvariant(name[0]) + name[1..];
    }

    /// <summary>shared.tsx truncate(): collapse whitespace, cut at <paramref name="max"/> with "…".</summary>
    public static string Truncate(string? text, int max = 140)
    {
        var t = SpaceRx().Replace(text ?? "", " ").Trim();
        return t.Length <= max ? t : t[..(max - 1)] + "…";
    }

    // ── search.ts ─────────────────────────────────────────────────────────
    static string Normalize(string? s) => SpaceRx().Replace((s ?? "").ToLowerInvariant(), " ").Trim();

    /// <summary>Every whitespace-separated term must appear somewhere in the combined text. Empty query matches everything.</summary>
    public static bool MatchesQuery(string? query, params string?[] texts)
    {
        var terms = Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return true;
        var hay = Normalize(string.Join(" ", texts.Where(t => !string.IsNullOrEmpty(t))));
        return terms.All(hay.Contains);
    }

    public static string Snippet(string? text, int max = 110) => Truncate(text, max);

    /// <summary>search.ts searchAll (diagnoses tab hidden): hits per tab id, capped at 50 each. Empty query = no hits.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<TplSearchHit>> SearchAll(string? query, TemplatesWorkspace lib, int limitPerTab = 50)
    {
        var empty = new List<TplSearchHit>();
        var outp = Tabs.ToDictionary(t => t.Id, _ => (IReadOnlyList<TplSearchHit>)empty);
        if (Normalize(query) == "") return outp;
        static string? First(params string?[] xs) => xs.FirstOrDefault(x => !string.IsNullOrEmpty(x));
        outp["soap"] = lib.Soap.Where(t => MatchesQuery(query, t.Title, t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan))
            .Select(t => new TplSearchHit("soap", t.Id, t.Title, Snippet(First(t.ChiefComplaint, t.Subjective, t.Assessment, t.Plan, t.Objective)), null)).Take(limitPerTab).ToList();
        outp["phrases"] = lib.Phrases.Where(p => MatchesQuery(query, p.Label, p.Body))
            .Select(p => new TplSearchHit("phrases", p.Id, p.Label, Snippet(p.Body), p.Field)).Take(limitPerTab).ToList();
        outp["rx"] = lib.Rx.Where(t => MatchesQuery(query, new[] { t.Title }.Concat(t.Lines.SelectMany(i => new[] { i.GenericName, i.Dosage, i.Instruction, i.Indication })).ToArray()))
            .Select(t => new TplSearchHit("rx", t.Id, t.Title, Snippet(string.Join(", ", t.Lines.Select(i => i.GenericName))), null)).Take(limitPerTab).ToList();
        outp["favorites"] = lib.Favorites.Where(f => MatchesQuery(query, f.GenericName, f.Dosage, f.Instruction))
            .Select(f => new TplSearchHit("favorites", f.Id, f.GenericName, Snippet(string.Join(" · ", new[] { f.Dosage, f.Instruction }.Where(x => !string.IsNullOrEmpty(x)))), null)).Take(limitPerTab).ToList();
        outp["certificates"] = lib.Certificates.Where(c => MatchesQuery(query, c.Title, c.DiagnosisText, c.Recommendations, c.PurposeException))
            .Select(c => new TplSearchHit("certificates", c.Id, c.Title, Snippet(First(c.DiagnosisText, c.Recommendations, c.PurposeException)), null)).Take(limitPerTab).ToList();
        return outp;
    }

    // ── reorder.ts ────────────────────────────────────────────────────────
    /// <summary>Swap <paramref name="id"/> with its neighbour; returns the SAME list when the move is not possible.</summary>
    public static IReadOnlyList<Guid> MoveId(IReadOnlyList<Guid> ids, Guid id, int dir)
    {
        var i = ids.ToList().IndexOf(id);
        var j = i + dir;
        if (i < 0 || j < 0 || j >= ids.Count) return ids;
        var next = ids.ToList();
        (next[i], next[j]) = (next[j], next[i]);
        return next;
    }

    [GeneratedRegex(@"\s+")] private static partial Regex SpaceRx();
    [GeneratedRegex(@"\s+\S*$")] private static partial Regex TrailingWordRx();
    [GeneratedRegex(@"[\s.,;:\-–—]+$")] private static partial Regex TrailingPunctRx();
}
//JJQR - END
