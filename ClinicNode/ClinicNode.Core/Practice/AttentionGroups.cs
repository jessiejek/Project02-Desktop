//JJQR - AI (Nov 25, 2026) - owner (GAV-2, approved mockup): the doctor's "Needs attention" is grouped by the visit date. "Today · Nov 25" comes first and is
// open; each older date ("Nov 24 · 2 items") is a header that opens / closes. Items not cleared on an earlier day stay under their own date (looked back
// DaysBack days). Clinic (Manila) dates. Same rules as web src/lib/attention-groups.ts.
using System.Globalization;

namespace ClinicNode.Core.Practice;

public sealed record AttentionGroup(DateOnly Date, bool IsToday, string Header, string CountLabel, IReadOnlyList<AttentionItem> Items);

public static class AttentionGroups
{
    public const int DaysBack = 30;
    public const int MaxRows = 8;

    /// <summary>The first date still shown.</summary>
    public static DateOnly From(DateOnly today) => today.AddDays(-DaysBack);

    /// <summary>"Nov 24" (the year is added when it is not this year).</summary>
    public static string ShortDate(DateOnly date, DateOnly today) =>
        date.ToString(date.Year == today.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture);

    public static string ItemsLabel(int n) => n == 1 ? "1 item" : $"{n} items";

    /// <summary>Today first, then older dates newest first; unpaid, then charts, then follow-ups inside a date. An item with no date counts as today.
    /// Future and too-old dates and repeats (same kind + booking) are dropped.</summary>
    public static IReadOnlyList<AttentionGroup> Group(IEnumerable<AttentionItem> items, DateOnly today)
    {
        var from = From(today);
        var seen = new HashSet<(AttentionKind, Guid)>();
        var kept = new List<(DateOnly Date, AttentionItem Item)>();
        foreach (var a in items)
        {
            var date = a.Date ?? today;
            if (date > today || date < from) continue;
            if (a.BookingId != Guid.Empty && !seen.Add((a.Kind, a.BookingId))) continue;
            kept.Add((date, a));
        }
        return kept
            .GroupBy(x => x.Date)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var list = g.Select(x => x.Item).OrderBy(a => (int)a.Kind).ToList(); // enum order: UnpaidCompleted, FinishChart, FollowUpDue
                var isToday = g.Key == today;
                return new AttentionGroup(g.Key, isToday, isToday ? $"Today · {ShortDate(g.Key, today)}" : ShortDate(g.Key, today), ItemsLabel(list.Count), list);
            })
            .ToList();
    }

    /// <summary>Today is open unless the doctor closed it; older dates are closed unless she opened them.</summary>
    public static bool IsOpen(AttentionGroup g, IReadOnlyDictionary<DateOnly, bool> toggled) =>
        toggled.TryGetValue(g.Date, out var open) ? open : g.IsToday;
}
//JJQR - END
