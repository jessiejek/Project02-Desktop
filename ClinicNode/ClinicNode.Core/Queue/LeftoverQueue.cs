//JJQR - AI (2026-11-25) - why: GAV-4 (owner approved the pro mockup, "apply it also in web please"). A patient left Waiting / On hold / In consultation
// when the day ended used to vanish from every Today screen at midnight. They are listed under "⚠ Not finished on an earlier day", one group per visit
// date (Manila), the most recent date open, older dates closed. Same rules as web src/lib/leftover-queue.ts.
// Online: ApiLeftoverQueueService (GET /api/queue/leftovers, PUT /api/queue/{id}/move-to-today, PUT /api/queue/{id}/no-show). Offline: this local
// service lists the same rows from this computer's database and marks no-show through the normal queue (synced later); Move to today needs the
// clinic server (a new queue number for today must come from the cloud), so offline it answers with a plain refusal and changes nothing.
using System.Globalization;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Staff;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Queue;

public sealed record LeftoverRow(Guid BookingId, DateOnly Date, string QueueNumber, bool IsPriority, Guid PatientId, string PatientName, string PatientCode,
    Guid DoctorId, BookingStatus Status, VisitType VisitType, DateTimeOffset CheckedInAt, DateTimeOffset UpdatedAt);

public sealed record LeftoverGroup(DateOnly Date, string Header, string CountLabel, string Chip, IReadOnlyList<LeftoverRow> Items);

public static class LeftoverGroups
{
    public const int DaysBack = 30;
    public const string MoveNeedsServer = "Moving a patient to today needs the clinic server. Connect and try again.";
    public const string MoveOldServer = "Moving a patient to today needs the updated clinic server.";

    public static bool IsLive(BookingStatus s) => s is BookingStatus.CheckedIn or BookingStatus.OnHold or BookingStatus.InProgress;

    public static string PatientsLabel(int n) => n == 1 ? "1 patient" : $"{n} patients";

    /// <summary>"Nov 24" (year added when it is not this year).</summary>
    public static string Chip(DateOnly date, DateOnly today) =>
        date.ToString(date.Year == today.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>"Tue, Nov 24".</summary>
    public static string Day(DateOnly date, DateOnly today) =>
        date.ToString("ddd", CultureInfo.InvariantCulture) + ", " + Chip(date, today);

    /// <summary>Earlier days only (before today, within DaysBack), live statuses only, newest date first; inside a date priority lane then arrival.</summary>
    public static IReadOnlyList<LeftoverGroup> Group(IEnumerable<LeftoverRow> rows, DateOnly today)
    {
        var from = today.AddDays(-DaysBack);
        var seen = new HashSet<Guid>();
        return rows
            .Where(r => r.Date < today && r.Date >= from && IsLive(r.Status) && seen.Add(r.BookingId))
            .GroupBy(r => r.Date)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var list = g.OrderBy(r => r.IsPriority ? 0 : 1).ThenBy(r => r.CheckedInAt).ToList();
                return new LeftoverGroup(g.Key, Day(g.Key, today), PatientsLabel(list.Count), Chip(g.Key, today), list);
            })
            .ToList();
    }

    /// <summary>The most recent date is open, older ones closed; the user's own toggle wins.</summary>
    public static bool IsOpen(IReadOnlyList<LeftoverGroup> groups, DateOnly date, IReadOnlyDictionary<DateOnly, bool> toggled) =>
        toggled.TryGetValue(date, out var open) ? open : groups.Count > 0 && groups[0].Date == date;

    /// <summary>Status pill text and the small "since …" line under it ("In progress" · "since Nov 24 4:52 PM", "Waiting" · "since Nov 24").</summary>
    public static (string Pill, string Since) StatusText(LeftoverRow r, DateOnly today)
    {
        var chip = Chip(r.Date, today);
        if (r.Status == BookingStatus.InProgress)
            return ("In progress", $"since {chip} {ClinicClock.TimeOf(r.UpdatedAt).ToString("h:mm tt", CultureInfo.InvariantCulture)}");
        return (r.Status == BookingStatus.OnHold ? "On hold" : "Waiting", $"since {chip}");
    }
}

//JJQR - AI (2026-11-26) - why: owner (t78u/t79u) "yesterday's queue should not cross over TODAY's". Earlier days get their OWN card below today's: one
// collapsible group per earlier date ("Wed, Nov 25") and under it that day's Queue (leftover rows) and that day's Needs attention. Build() merges the two
// by date; today and future dates never appear. Most recent day open, older closed, a toggle wins. Same rules as web earlierDays() (leftover-queue.ts).
public sealed record EarlierDay(DateOnly Date, string Header, string CountLabel, IReadOnlyList<LeftoverRow> Queue, int AttentionCount);

public static class EarlierDays
{
    public static string CountLabel(int queue, int attention)
    {
        var parts = new List<string>();
        if (queue > 0) parts.Add($"{queue} in queue");
        if (attention > 0) parts.Add(attention == 1 ? "1 needs attention" : $"{attention} need attention");
        return string.Join(" · ", parts);
    }

    public static IReadOnlyList<EarlierDay> Build(IReadOnlyList<LeftoverGroup> leftovers, IEnumerable<(DateOnly Date, int Count)> attention, DateOnly today)
    {
        var byDate = new Dictionary<DateOnly, (IReadOnlyList<LeftoverRow> Queue, int Attention)>();
        foreach (var g in leftovers)
            if (g.Date < today && g.Items.Count > 0) byDate[g.Date] = (g.Items, 0);
        foreach (var (date, count) in attention)
        {
            if (date >= today || count <= 0) continue;
            var cur = byDate.TryGetValue(date, out var c) ? c : ((IReadOnlyList<LeftoverRow>)Array.Empty<LeftoverRow>(), 0);
            byDate[date] = (cur.Item1, cur.Item2 + count);
        }
        return byDate.OrderByDescending(kv => kv.Key)
            .Select(kv => new EarlierDay(kv.Key, LeftoverGroups.Day(kv.Key, today), CountLabel(kv.Value.Queue.Count, kv.Value.Attention), kv.Value.Queue, kv.Value.Attention))
            .ToList();
    }

    public static bool IsOpen(IReadOnlyList<EarlierDay> days, DateOnly date, IReadOnlyDictionary<DateOnly, bool> toggled) =>
        toggled.TryGetValue(date, out var open) ? open : days.Count > 0 && days[0].Date == date;
}
//JJQR - END

public interface ILeftoverQueueService
{
    /// <summary>Earlier-day visits not finished. <paramref name="doctorId"/> = that doctor's own (doctor screens); null = the whole clinic (staff).</summary>
    Task<IReadOnlyList<LeftoverRow>> ListAsync(Guid? doctorId, CancellationToken ct = default);
    /// <summary>Same booking, today's date, a new queue number for today. Ok(bookingId); a refusal (already moved / finished / offline) is a Fail in plain words.</summary>
    Task<StaffResult> MoveToTodayAsync(Guid bookingId, CancellationToken ct = default);
    /// <summary>Closes the earlier-day visit as a no-show (the normal queue no-show).</summary>
    Task<StaffResult> MarkNoShowAsync(Guid bookingId, CancellationToken ct = default);
}

public sealed class LocalLeftoverQueueService(IDbContextFactory<ClinicAppDbContext> factory, IQueueService queue) : ILeftoverQueueService
{
    public async Task<IReadOnlyList<LeftoverRow>> ListAsync(Guid? doctorId, CancellationToken ct = default)
    {
        var today = ClinicClock.Today;
        var from = today.AddDays(-LeftoverGroups.DaysBack);
        await using var db = await factory.CreateDbContextAsync(ct);
        var q = db.Bookings.AsNoTracking().Include(b => b.Patient)
            .Where(b => b.AppointmentDate < today && b.AppointmentDate >= from && b.QueueNumber != null && b.DeletedAt == null
                && (b.Status == BookingStatus.CheckedIn || b.Status == BookingStatus.OnHold || b.Status == BookingStatus.InProgress));
        if (doctorId is { } d) q = q.Where(b => b.DoctorId == d);
        var rows = await q.ToListAsync(ct);
        return rows.Select(b => new LeftoverRow(b.BookingId, b.AppointmentDate, b.QueueNumber ?? "—", QueueLane.IsPriority(b.QueueNumber), b.PatientId,
            b.Patient is null ? "" : $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(), b.Patient?.PatientCode ?? "", b.DoctorId, b.Status, b.VisitType,
            b.CreatedAt, b.UpdatedAt)).ToList();
    }

    public Task<StaffResult> MoveToTodayAsync(Guid bookingId, CancellationToken ct = default) =>
        Task.FromResult(StaffResult.Fail(LeftoverGroups.MoveNeedsServer));

    public async Task<StaffResult> MarkNoShowAsync(Guid bookingId, CancellationToken ct = default)
    {
        var r = await queue.NoShowAsync(bookingId, ct);
        return r.Succeeded ? StaffResult.Ok(bookingId) : StaffResult.Fail(r.Error ?? "Could not mark this visit no-show.");
    }
}
//JJQR - END
