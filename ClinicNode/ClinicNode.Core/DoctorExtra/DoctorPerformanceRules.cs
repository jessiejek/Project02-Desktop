//JJQR - AI (Nov 21, 2026) - D-PERF pure helpers: 1:1 port of Project02-fe lib/doctor/performance.ts (percent change, greeting, cheerful wording, peso, "Also" strip, 14-day chart geometry).
using System.Globalization;

namespace ClinicNode.Core.DoctorExtra;

public enum ChangeDirection { Up, Down, Flat, New }
public enum ChangeTone { Positive, Soft, Neutral }
public sealed record PerformanceChange(ChangeDirection Direction, int? Percent, string Text, ChangeTone Tone);
public sealed record AlsoItem(string Key, string Icon, string Value, string Label);
public enum ChartMetric { Collected, PatientsSeen }
public sealed record ChartBar(DateOnly Date, double Value, double X, double Y, double Width, double Height, string Label, bool IsToday);
public sealed record ChartTick(double Value, double Y);
public sealed record ChartModel(double Width, double Height, IReadOnlyList<ChartBar> Bars, double Baseline, double Max, IReadOnlyList<ChartTick> Ticks, double Total);
public sealed record DiagnosisBar(string Label, string? Code, int Count, int Pct);

public static class DoctorPerformanceRules
{
    public static readonly (PerformancePeriodName Id, string Label)[] PeriodOptions =
        [(PerformancePeriodName.Today, "Today"), (PerformancePeriodName.Week, "This week"), (PerformancePeriodName.Month, "This month")];

    /// <summary>"vs yesterday" / "last week" / "last month".</summary>
    public static string PreviousLabel(PerformancePeriodName p) => p switch { PerformancePeriodName.Week => "last week", PerformancePeriodName.Month => "last month", _ => "yesterday" };

    /// <summary>"today" / "this week" / "this month" for use inside a sentence.</summary>
    public static string PeriodPhrase(PerformancePeriodName p) => p switch { PerformancePeriodName.Week => "this week", PerformancePeriodName.Month => "this month", _ => "today" };

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ── money ────────────────────────────────────────────────────────────
    /// <summary>"₱1,250" (centavos only when present).</summary>
    public static string PesoLabel(decimal? value) => "₱" + (value ?? 0m).ToString("#,##0.##", Inv);

    /// <summary>Short form for chart axes: ₱950, ₱1.2k, ₱12k, ₱1.5M.</summary>
    public static string PesoCompact(double value)
    {
        var n = double.IsFinite(value) ? Math.Max(0, value) : 0;
        if (n >= 1_000_000) return $"₱{Trim(n / 1_000_000)}M";
        if (n >= 10_000) return $"₱{Math.Round(n / 1000, MidpointRounding.AwayFromZero):0}k";
        if (n >= 1_000) return $"₱{Trim(n / 1000)}k";
        return $"₱{Math.Round(n, MidpointRounding.AwayFromZero):0}";
    }

    private static string Trim(double n) => (Math.Round(n * 10, MidpointRounding.AwayFromZero) / 10).ToString("0.#", Inv);

    // ── change vs the previous period ────────────────────────────────────
    /// <summary>Percent change from previous to current, rounded. null when there is no baseline (previous is 0).</summary>
    public static int? PercentChange(double current, double previous) =>
        !double.IsFinite(current) || !double.IsFinite(previous) || previous == 0 ? null : (int)Math.Round((current - previous) / Math.Abs(previous) * 100, MidpointRounding.AwayFromZero);

    public static PerformanceChange DescribeChange(double current, double previous, string previousLabel)
    {
        var cur = double.IsFinite(current) ? current : 0;
        var prev = double.IsFinite(previous) ? previous : 0;
        if (cur == prev)
            return cur == 0
                ? new(ChangeDirection.Flat, null, $"Nothing yet — same as {previousLabel}", ChangeTone.Neutral)
                : new(ChangeDirection.Flat, 0, $"Same as {previousLabel}", ChangeTone.Neutral);
        if (prev == 0) return new(ChangeDirection.New, null, $"A fresh start — {previousLabel} had none", ChangeTone.Positive);
        var pct = Math.Abs(PercentChange(cur, prev) ?? 0);
        if (pct == 0) return new(ChangeDirection.Flat, 0, $"About the same as {previousLabel}", ChangeTone.Neutral);
        return cur > prev
            ? new(ChangeDirection.Up, pct, $"↑ {pct}% vs {previousLabel}", ChangeTone.Positive)
            : new(ChangeDirection.Down, pct, $"↓ {pct}% vs {previousLabel}", ChangeTone.Soft);
    }

    // ── greeting & cheerful wording ──────────────────────────────────────
    public static int ManilaHour(DateTimeOffset now) => now.ToOffset(TimeSpan.FromHours(8)).Hour;

    public static string GreetingWord(DateTimeOffset now) => ManilaHour(now) is var h && h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";

    /// <summary>"Dr. Maria Santos" whether or not the stored name already starts with "Dr".</summary>
    public static string DoctorDisplayName(string? name)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace((name ?? "").Trim(), @"\s+", " ");
        if (clean.Length == 0) return "Doctor";
        return System.Text.RegularExpressions.Regex.IsMatch(clean, @"^dr\.?\s", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            ? System.Text.RegularExpressions.Regex.Replace(clean, @"^dr\.?\s+", "Dr. ", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            : $"Dr. {clean}";
    }

    public static string Greeting(DateTimeOffset now, string? name) => $"{GreetingWord(now)}, {DoctorDisplayName(name)}";

    private static string Plural(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

    /// <summary>One warm sentence under the greeting. Always upbeat, never a scolding.</summary>
    public static string Headline(PerformancePeriodName period, DoctorPerformanceStats s, DoctorPerformanceStats previous)
    {
        var seen = s.PatientsSeen;
        if (seen == 0)
            return period switch
            {
                PerformancePeriodName.Today => "A calm start — your first patient of the day will be along soon.",
                PerformancePeriodName.Week => "A fresh week — your numbers will fill in as you see patients.",
                _ => "A fresh month — your numbers will fill in as you see patients.",
            };
        var money = s.Collected > 0 ? $" and collected {PesoLabel(s.Collected)}" : "";
        var baseText = $"You've seen {Plural(seen, "patient")} {PeriodPhrase(period)}{money}";
        if (seen > previous.PatientsSeen && previous.PatientsSeen > 0) return $"{baseText} — busier than {PreviousLabel(period)}. Lovely work!";
        if (s.Collected > previous.Collected && previous.Collected > 0) return $"{baseText} — up on {PreviousLabel(period)}. Lovely work!";
        return $"{baseText}. Thank you for caring for them.";
    }

    public static string EmptyMessage(PerformancePeriodName period) => period == PerformancePeriodName.Today
        ? "No completed visits yet today. Your numbers will appear here as you finish consultations."
        : $"No completed visits {PeriodPhrase(period)} yet. They'll show up here as you finish consultations.";

    public static bool IsEmptyPeriod(DoctorPerformanceStats s) => s.PatientsSeen == 0 && s.Collected == 0 && s.Billed == 0;

    /// <summary>Average consult length, or null when there are too few samples to be honest about.</summary>
    public static string? AvgConsultLabel(DoctorPerformanceStats s, int minSamples = 5) =>
        s.AvgConsultMinutes is null || s.AvgConsultSamples < minSamples ? null : $"{Math.Round(s.AvgConsultMinutes.Value, MidpointRounding.AwayFromZero):0} min";

    /// <summary>The secondary counts under the cards. Zero-valued items stay (a clear 0 is information), except consult time.</summary>
    public static IReadOnlyList<AlsoItem> AlsoItems(DoctorPerformanceStats s)
    {
        var items = new List<AlsoItem>
        {
            new("rx", "medication", s.RxGroups.ToString(Inv), s.RxGroups == 1 ? "prescription written" : "prescriptions written"),
            new("cert", "description", s.MedCerts.ToString(Inv), s.MedCerts == 1 ? "medical certificate" : "medical certificates"),
            new("lab", "science", s.LabOrders.ToString(Inv), s.LabOrders == 1 ? "lab order" : "lab orders"),
            new("followups", "event", s.FollowUpVisits.ToString(Inv), s.FollowUpVisits == 1 ? "follow-up visit" : "follow-up visits"),
            new("first", "person", s.FirstTimePatients.ToString(Inv), s.FirstTimePatients == 1 ? "first-time patient" : "first-time patients"),
        };
        if (AvgConsultLabel(s) is { } avg) items.Add(new("avg", "schedule", avg, "average consult"));
        return items;
    }

    /// <summary>Detail line under the Patients card.</summary>
    public static string PatientsDetail(DoctorPerformanceStats s) => s.PatientsSeen > 0
        ? $"{s.NewVisits} new · {s.FollowUpVisits} follow-up{(s.NoShows > 0 ? $" · {s.NoShows} no-show{(s.NoShows == 1 ? "" : "s")}" : "")}"
        : "Completed visits appear here";

    /// <summary>Extra line under the Collected hero: waived / refunded.</summary>
    public static string CollectedFootnote(DoctorPerformanceStats s)
    {
        var extras = new List<string>();
        if (s.Waived > 0) extras.Add($"{PesoLabel(s.Waived)} waived");
        if (s.Refunded > 0) extras.Add($"{PesoLabel(s.Refunded)} refunded (not counted)");
        return "Your own gross figures" + (extras.Count > 0 ? " · " + string.Join(" · ", extras) : "");
    }

    public static string UnpaidDetail(int count) => count == 0 ? "All settled — nothing outstanding" : $"{count} completed visit{(count == 1 ? "" : "s")} unpaid";

    /// <summary>The range line: "Nov 21, 2026" or "Nov 17, 2026 – Nov 21, 2026", plus the scope note.</summary>
    public static string RangeLabel(DoctorPerformancePeriod p)
    {
        static string F(DateOnly d) => d.ToString("MMM d, yyyy", Inv);
        return (p.From == p.To ? F(p.From) : $"{F(p.From)} – {F(p.To)}") + " · your own patients, gross figures";
    }

    // ── 14-day chart geometry ────────────────────────────────────────────
    private static readonly string[] Weekdays = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>"Wed 19" from a clinic date.</summary>
    public static string DayLabel(DateOnly d) => $"{Weekdays[(int)d.DayOfWeek]} {d.Day}";

    /// <summary>Rounds a max up to a "nice" axis top (1, 2, 5 × 10ⁿ) so ticks read cleanly.</summary>
    public static double NiceMax(double value)
    {
        if (!double.IsFinite(value) || value <= 0) return 1;
        var pow = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var f = value / pow;
        var nice = f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10;
        return nice * pow;
    }

    private static double R(double n) => Math.Round(n * 100, MidpointRounding.AwayFromZero) / 100;

    public static ChartModel BuildChart(IReadOnlyList<DoctorPerformanceDay> series, ChartMetric metric, DateOnly today,
        double width = 640, double height = 240, double padLeft = 48, double padRight = 8, double padTop = 12, double padBottom = 30)
    {
        var innerW = Math.Max(1, width - padLeft - padRight);
        var innerH = Math.Max(1, height - padTop - padBottom);
        var values = series.Select(d => Math.Max(0, (double)(metric == ChartMetric.Collected ? d.Collected : d.PatientsSeen))).ToList();
        var max = NiceMax(values.Count == 0 ? 0 : values.Max());
        var slot = series.Count > 0 ? innerW / series.Count : innerW;
        var barW = Math.Max(2, Math.Min(36, slot * 0.62));
        var baseline = padTop + innerH;
        var bars = series.Select((d, i) =>
        {
            var v = values[i];
            var h = v > 0 ? Math.Max(3, v / max * innerH) : 0; // a visible stub for any non-zero day
            return new ChartBar(d.Date, v, R(padLeft + i * slot + (slot - barW) / 2), R(baseline - h), R(barW), R(h), DayLabel(d.Date), d.Date == today);
        }).ToList();
        var ticks = new[] { 0.0, 0.5, 1.0 }.Select(f => new ChartTick(R(max * f), R(baseline - f * innerH))).ToList();
        return new ChartModel(width, height, bars, R(baseline), max, ticks, values.Sum());
    }

    /// <summary>Screen-reader style summary, e.g. "Collected over the last 14 days: ₱12,400 in total; best day Tue 11 (₱3,000)."</summary>
    public static string ChartSummary(IReadOnlyList<DoctorPerformanceDay> series, ChartMetric metric)
    {
        string Fmt(decimal n) => metric == ChartMetric.Collected ? PesoLabel(n) : Plural((int)n, "patient");
        decimal V(DoctorPerformanceDay d) => metric == ChartMetric.Collected ? d.Collected : d.PatientsSeen;
        var total = series.Sum(V);
        DoctorPerformanceDay? best = null;
        foreach (var d in series) if (V(d) > (best is null ? 0 : V(best))) best = d;
        var what = metric == ChartMetric.Collected ? "Collected" : "Patients seen";
        if (best is null) return $"{what} over the last {series.Count} days: nothing yet.";
        return $"{what} over the last {series.Count} days: {Fmt(total)} in total; best day {DayLabel(best.Date)} ({Fmt(V(best))}).";
    }

    /// <summary>Readout line for one day (hover/tap): "Tue 11 (today) · 3 patients · ₱3,000 collected · ₱3,200 billed".</summary>
    public static string DayReadout(DoctorPerformanceDay d, DateOnly today) =>
        $"{DayLabel(d.Date)}{(d.Date == today ? " (today)" : "")} · {Plural(d.PatientsSeen, "patient")} · {PesoLabel(d.Collected)} collected";

    /// <summary>Share (0–100) of each diagnosis relative to the busiest one — for the little bars.</summary>
    public static IReadOnlyList<DiagnosisBar> DiagnosisBars(IReadOnlyList<DoctorTopDiagnosis> top)
    {
        var max = Math.Max(1, top.Count == 0 ? 1 : top.Max(t => t.Count));
        return top.Select(t => new DiagnosisBar(t.Label, t.Icd10Code, t.Count, Math.Max(6, (int)Math.Round(t.Count / (double)max * 100, MidpointRounding.AwayFromZero)))).ToList();
    }
}
//JJQR - END
