// JJQR - AI (Nov 21, 2026) - doctor "Your clinic today" data, from web /doctor/dashboard: day status, up-next queue cards,
// Needs attention (unpaid completed / charts to finish / follow-ups due today), clinic report tiles, recent bookings, practice analytics.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Practice;

public enum AttentionKind { UnpaidCompleted, FinishChart, FollowUpDue }

//JJQR - AI (Nov 25, 2026) - GAV-2: Date = the visit date the item belongs to (null = today), so Needs attention can group by date (AttentionGroups)
public sealed record AttentionItem(AttentionKind Kind, Guid BookingId, string PatientName, string Text, string ActionLabel, string Aside, DateOnly? Date = null,
    decimal? Amount = null); //JJQR - AI (Nov 25, 2026) - unpaid rows: the amount due, for the doctor's "Mark received" //JJQR - END
//JJQR - END
public sealed record UpNextItem(Guid BookingId, string PatientName, string QueueNumber, bool IsPriority, VisitType VisitType, bool InProgress, int WaitMinutes, bool HasPrescription = false, bool HasLabOrders = false)
{
    public string ActionLabel => InProgress ? "Resume consultation" : "Start consultation";
    public string Title => $"Queue {QueueNumber} · {PatientName}";
    public string Subtitle => (VisitType == VisitType.FollowUp ? "Follow-up" : "New") + (IsPriority ? " · Priority (Senior/PWD)" : "")
        + (InProgress ? " · In progress" : $" · Waiting {WaitMinutes}m");
}
public sealed record RecentBooking(Guid BookingId, string PatientName, string Slot, string? QueueNumber, BookingStatus Status, bool HasPrescription = false, bool HasLabOrders = false);
public sealed record AnnouncementItem(string Title, string Body, DateTimeOffset CreatedAt);

public sealed record DoctorDashboard(
    string DoctorName,
    DoctorDayStatusEnum DayStatus,
    int Waiting, int InProgress, int CompletedToday, int TotalToday,
    int LongestWaitMinutes,
    IReadOnlyList<UpNextItem> UpNext,
    IReadOnlyList<AttentionItem> Unpaid,
    IReadOnlyList<AttentionItem> Charts,
    IReadOnlyList<AttentionItem> FollowUps,
    int PendingVerifications, int OnHoldMonth, int UnpaidCompletedMonth, int FollowUpsDue7Days,
    IReadOnlyList<RecentBooking> Recent,
    IReadOnlyList<AnnouncementItem> Announcements);

public sealed record PracticeTotals(int CompletedVisits, decimal Billed, decimal Collected, decimal Waived);
public sealed record MonthTrend(string Period, int CompletedVisits, decimal Collected);
public sealed record PracticeAnalytics(PracticeTotals Totals, IReadOnlyList<MonthTrend> Trend, int? CollectedDeltaPercent);

public interface IDoctorDashboardService
{
    Task<DoctorDashboard> GetAsync(Guid doctorId, CancellationToken ct = default);
    Task<StaffResult> SetDayStatusAsync(Guid doctorId, DoctorDayStatusEnum status, CancellationToken ct = default);
    Task<PracticeAnalytics> GetAnalyticsAsync(Guid doctorId, DateOnly from, DateOnly to, bool isMonthRange, CancellationToken ct = default);
    // JJQR - AI (Nov 21, 2026) - Call next / start consult (CheckedIn → InProgress) and Rx/lab print flags.
    Task<StaffResult> CallNextAsync(Guid doctorId, CancellationToken ct = default);
    Task<StaffResult> StartConsultAsync(Guid doctorId, Guid bookingId, CancellationToken ct = default);
    // JJQR - END
    //JJQR - AI (Nov 25, 2026) - doctor "Mark received" on a completed, unpaid Needs attention row: Paid, Cash, exactly `amount` (what the doctor saw), no OR,
    // confirmed by the doctor. Only the doctor's own Completed visit with an Unpaid payment of that amount; anything else is a Fail with a plain message.
    Task<StaffResult> MarkReceivedAsync(Guid doctorId, Guid bookingId, decimal amount, CancellationToken ct = default);
    //JJQR - END
}

public sealed class DoctorDashboardService : IDoctorDashboardService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IQueueService _queue;
    private readonly IServiceProvider _sp;
    private readonly NodeOptions? _options;
    // the staff day-status service is registered by AddClinicNodeStaff(); fall back to creating it so Core alone still works
    private IDoctorStatusService Status => _sp.GetService(typeof(IDoctorStatusService)) as IDoctorStatusService
        ?? Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<DoctorStatusService>(_sp);

    public DoctorDashboardService(IDbContextFactory<ClinicAppDbContext> factory, IQueueService queue, IServiceProvider sp, NodeOptions? options = null)
    {
        _factory = factory;
        _queue = queue;
        _sp = sp;
        _options = options;
    }

    void Log(string source, Exception ex)
    {
        if (_options is not null) NodeErrorLog.Write(_options, source, ex);
    }

    static bool IsPrn(string? reason, string? instructions) =>
        (reason ?? "").Contains("PRN", StringComparison.OrdinalIgnoreCase) || (instructions ?? "").Contains("PRN", StringComparison.OrdinalIgnoreCase);

    public Task<StaffResult> SetDayStatusAsync(Guid doctorId, DoctorDayStatusEnum status, CancellationToken ct = default) =>
        Status.SetAsync(doctorId, status, ct);

    // JJQR - AI (Nov 21, 2026)
    public async Task<StaffResult> CallNextAsync(Guid doctorId, CancellationToken ct = default)
    {
        var entries = await _queue.ListDoctorTodayAsync(doctorId, ct);
        var next = entries.FirstOrDefault(e => e.Status == BookingStatus.CheckedIn);
        if (next is null) return StaffResult.Fail("No one is waiting to be called.");
        return await CallOwnedAsync(next.BookingId, ct);
    }

    public async Task<StaffResult> StartConsultAsync(Guid doctorId, Guid bookingId, CancellationToken ct = default)
    {
        var entries = await _queue.ListDoctorTodayAsync(doctorId, ct);
        var row = entries.FirstOrDefault(e => e.BookingId == bookingId);
        if (row is null) return StaffResult.Fail("This booking is not on your list today.");
        if (row.Status == BookingStatus.InProgress) return StaffResult.Ok(bookingId);
        if (row.Status != BookingStatus.CheckedIn) return StaffResult.Fail("This patient is not ready to consult.");
        return await CallOwnedAsync(bookingId, ct);
    }

    async Task<StaffResult> CallOwnedAsync(Guid bookingId, CancellationToken ct)
    {
        var called = await _queue.CallAsync(bookingId, ct);
        return called.Succeeded ? StaffResult.Ok(called.BookingId ?? bookingId) : StaffResult.Fail(called.Error ?? "Could not call this patient.");
    }

    static async Task<Dictionary<Guid, (bool Rx, bool Labs)>> LoadPrintFlagsAsync(ClinicAppDbContext db, IReadOnlyList<Guid> bookingIds, CancellationToken ct)
    {
        var map = bookingIds.Distinct().ToDictionary(id => id, _ => (Rx: false, Labs: false));
        if (map.Count == 0) return map;
        var rxIds = await db.PrescriptionGroups.AsNoTracking()
            .Where(g => bookingIds.Contains(g.BookingId))
            .Select(g => g.BookingId)
            .Distinct()
            .ToListAsync(ct);
        foreach (var id in rxIds)
            map[id] = (true, map[id].Labs);

        var consults = await db.Consultations.AsNoTracking()
            .Where(c => bookingIds.Contains(c.BookingId))
            .Select(c => new { c.BookingId, c.ConsultationId })
            .ToListAsync(ct);
        var consultIds = consults.Select(c => c.ConsultationId).ToList();
        if (consultIds.Count > 0)
        {
            var labConsults = await db.LabOrders.AsNoTracking()
                .Where(l => consultIds.Contains(l.ConsultationId))
                .Select(l => l.ConsultationId)
                .Distinct()
                .ToListAsync(ct);
            foreach (var id in consults.Where(c => labConsults.Contains(c.ConsultationId)).Select(c => c.BookingId))
                map[id] = (map[id].Rx, true);
        }
        return map;
    }
    // JJQR - END

    public async Task<DoctorDashboard> GetAsync(Guid doctorId, CancellationToken ct = default)
    {
        try { return await GetCoreAsync(doctorId, ct); }
        catch (Exception ex) { Log("IDoctorDashboardService.GetAsync", ex); throw; }
    }

    //JJQR - AI (Nov 25, 2026) - doctor "Mark received" (Local / Offline copy): same checks as the server's POST payments/booking/{id}/doctor-received, then the
    // existing front-desk confirm (IPaymentService.ConfirmAsync: exact amount, outbox "confirm" for sync, PaymentUpdated event). Never throws for a refusal.
    public const string MarkReceivedNote = "Cash received by the doctor (marked on the doctor dashboard).";

    public async Task<StaffResult> MarkReceivedAsync(Guid doctorId, Guid bookingId, decimal amount, CancellationToken ct = default)
    {
        try
        {
            await using (var db = await _factory.CreateDbContextAsync(ct))
            {
                var b = await db.Bookings.AsNoTracking().Include(x => x.Payment).SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
                if (b is null) return StaffResult.Fail("This visit was not found.");
                if (b.DoctorId != doctorId) return StaffResult.Fail("This is not your visit.");
                if (b.Status != BookingStatus.Completed) return StaffResult.Fail("Only a completed visit can be marked received.");
                if (b.Payment is not { } p) return StaffResult.Fail("This visit has no fee to collect.");
                if (p.Status == PaymentStatus.Waived) return StaffResult.Fail("This professional fee was waived, so there is nothing to collect.");
                if (p.Status != PaymentStatus.Unpaid) return StaffResult.Fail("This visit is no longer unpaid (it was already recorded).");
                if (p.Amount <= 0) return StaffResult.Fail("There is no amount due on this visit.");
                if (p.Amount != amount) return StaffResult.Fail($"The amount due is now ₱{p.Amount:0.##}, not ₱{amount:0.##}. Check the visit and try again.");
                var payments = _sp.GetService(typeof(ClinicNode.Core.Payments.IPaymentService)) as ClinicNode.Core.Payments.IPaymentService
                    ?? Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<ClinicNode.Core.Payments.PaymentService>(_sp);
                var res = await payments.ConfirmAsync(p.PaymentId, new ClinicNode.Core.Payments.ConfirmPaymentRequest(PaymentMethod.Cash, null, null, amount, MarkReceivedNote), ct);
                return res.Succeeded ? StaffResult.Ok(bookingId) : StaffResult.Fail(res.Error ?? "Couldn't mark it received.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log("IDoctorDashboardService.MarkReceivedAsync", ex);
            return StaffResult.Fail("Couldn't mark it received on this computer. Try again.");
        }
    }
    //JJQR - END

    async Task<DoctorDashboard> GetCoreAsync(Guid doctorId, CancellationToken ct)
    {
        var today = ClinicClock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var in7 = today.AddDays(7);
        var entries = await _queue.ListDoctorTodayAsync(doctorId, ct);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var doctor = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount).SingleOrDefaultAsync(d => d.DoctorId == doctorId, ct);
        var dayStatus = (await db.DoctorDayStatuses.AsNoTracking().FirstOrDefaultAsync(s => s.DoctorId == doctorId && s.StatusDate == today, ct))?.Status
            ?? DoctorDayStatusEnum.Available;

        var now = DateTimeOffset.UtcNow;
        var waitingRows = entries.Where(e => e.Status == BookingStatus.CheckedIn).ToList();
        int Minutes(QueueEntry e) => (int)Math.Max(0, (now - e.CreatedAt).TotalMinutes);
        var upNextEntries = entries
            .Where(e => e.Status is BookingStatus.CheckedIn or BookingStatus.InProgress)
            .OrderByDescending(e => e.Status == BookingStatus.InProgress) // someone already with the doctor stays on top; lane order is kept by the queue
            .Take(3)
            .ToList();

        var unpaid = entries.Where(e => e.Status == BookingStatus.Completed && e.AmountDue > 0)
            .Select(e => new AttentionItem(AttentionKind.UnpaidCompleted, e.BookingId, e.PatientName, $"{e.PatientName} — completed, unpaid", "View appointment", $"₱{e.AmountDue:0.##} due", today, e.AmountDue))
            .ToList();
        //JJQR - AI (Nov 25, 2026) - GAV-2: an unpaid completed visit from an earlier day (last AttentionGroups.DaysBack days) stays listed under its date
        var attentionFrom = AttentionGroups.From(today);
        var olderUnpaid = await db.Bookings.AsNoTracking().Include(b => b.Patient).Include(b => b.Payment)
            .Where(b => b.DoctorId == doctorId && b.Status == BookingStatus.Completed
                && b.AppointmentDate >= attentionFrom && b.AppointmentDate < today
                && (b.Payment == null || b.Payment.Status == PaymentStatus.Unpaid))
            .ToListAsync(ct);
        unpaid.AddRange(olderUnpaid.Where(b => b.AmountDue > 0).OrderByDescending(b => b.AppointmentDate)
            .Select(b => new AttentionItem(AttentionKind.UnpaidCompleted, b.BookingId, Name(b), $"{Name(b)} — completed, unpaid", "View appointment", $"₱{b.AmountDue:0.##} due", b.AppointmentDate, b.AmountDue)));
        //JJQR - END

        var todayConsults = await db.Consultations.AsNoTracking()
            .Where(c => c.DoctorId == doctorId && c.Status == ConsultationStatus.Completed)
            .Join(db.Bookings.AsNoTracking().Include(b => b.Patient).Where(b => b.AppointmentDate >= attentionFrom && b.AppointmentDate <= today), c => c.BookingId, b => b.BookingId, (c, b) => new { c, b })
            .ToListAsync(ct);
        var charts = todayConsults
            .Where(x => string.IsNullOrWhiteSpace(x.c.Assessment) && string.IsNullOrWhiteSpace(x.c.Plan))
            .Select(x => new AttentionItem(AttentionKind.FinishChart, x.c.BookingId, Name(x.b), $"{Name(x.b)} — Assessment and Plan are blank", "Finish chart", "Finish chart", x.b.AppointmentDate))
            .ToList();

        var dueFollowUps = await db.FollowUps.AsNoTracking()
            .Where(f => f.DoctorId == doctorId && f.FollowUpDate >= today && f.FollowUpDate <= in7)
            .ToListAsync(ct);
        var dueFollowUps7 = dueFollowUps.Where(f => !IsPrn(f.Reason, f.Instructions)).ToList();
        var dueTodayConsultIds = dueFollowUps7.Where(f => f.FollowUpDate == today).Select(f => f.ConsultationId).ToList();
        var dueTodayBookings = await db.Consultations.AsNoTracking().Where(c => dueTodayConsultIds.Contains(c.ConsultationId))
            .Join(db.Bookings.AsNoTracking().Include(b => b.Patient), c => c.BookingId, b => b.BookingId, (c, b) => b).ToListAsync(ct);
        var followUps = dueTodayBookings
            .Select(b => new AttentionItem(AttentionKind.FollowUpDue, b.BookingId, Name(b), $"{Name(b)} — follow-up due today", "View chart", "View chart", today))
            .ToList();

        var monthBookings = await db.Bookings.AsNoTracking().Include(b => b.Patient).Include(b => b.Payment)
            .Where(b => b.DoctorId == doctorId && b.AppointmentDate >= monthStart && b.AppointmentDate <= today).ToListAsync(ct);
        var recent = await db.Bookings.AsNoTracking().Include(b => b.Patient)
            .Where(b => b.DoctorId == doctorId).OrderByDescending(b => b.CreatedAt).Take(20).ToListAsync(ct);
        var announcements = await db.Announcements.AsNoTracking().Where(a => a.IsActive).OrderByDescending(a => a.CreatedAt).Take(5).ToListAsync(ct);

        // JJQR - AI (Nov 21, 2026) - print flags for per-row Print Rx / Print Labs (web AppointmentPrintActions).
        var flagIds = upNextEntries.Select(e => e.BookingId).Concat(recent.Select(b => b.BookingId)).Distinct().ToList();
        var flags = await LoadPrintFlagsAsync(db, flagIds, ct);
        (bool Rx, bool Labs) Flag(Guid id) => flags.GetValueOrDefault(id);
        var upNext = upNextEntries
            .Select(e => new UpNextItem(e.BookingId, e.PatientName, e.QueueNumber ?? "—", e.IsPriority, e.VisitType, e.Status == BookingStatus.InProgress, Minutes(e), Flag(e.BookingId).Rx, Flag(e.BookingId).Labs))
            .ToList();
        // JJQR - END

        return new DoctorDashboard(
            doctor?.StaffAccount?.FullName ?? "Doctor",
            dayStatus,
            entries.Count(e => e.Status == BookingStatus.CheckedIn),
            entries.Count(e => e.Status == BookingStatus.InProgress),
            entries.Count(e => e.Status == BookingStatus.Completed),
            entries.Count,
            waitingRows.Count == 0 ? 0 : waitingRows.Max(Minutes),
            upNext, unpaid, charts, followUps,
            monthBookings.Count(b => b.Status == BookingStatus.ProofSubmitted),
            monthBookings.Count(b => b.Status == BookingStatus.OnHold),
            monthBookings.Count(b => b.Status == BookingStatus.Completed && b.AmountDue > 0 && (b.Payment == null || b.Payment.Status == PaymentStatus.Unpaid)),
            dueFollowUps7.Count,
            recent.Select(b => new RecentBooking(b.BookingId, Name(b), b.SlotStartTime.ToString("HH:mm"), b.QueueNumber, b.Status, Flag(b.BookingId).Rx, Flag(b.BookingId).Labs)).ToList(),
            announcements.Select(a => new AnnouncementItem(a.Title, a.Body, a.CreatedAt)).ToList());
    }

    static string Name(ClinicApp.Domain.Entities.Booking b) => b.Patient is null ? "A patient" : $"{b.Patient.FirstName} {b.Patient.LastName}".Trim();

    public async Task<PracticeAnalytics> GetAnalyticsAsync(Guid doctorId, DateOnly from, DateOnly to, bool isMonthRange, CancellationToken ct = default)
    {
        try { return await GetAnalyticsCoreAsync(doctorId, from, to, isMonthRange, ct); }
        catch (Exception ex) { Log("IDoctorDashboardService.GetAnalyticsAsync", ex); throw; }
    }

    async Task<PracticeAnalytics> GetAnalyticsCoreAsync(Guid doctorId, DateOnly from, DateOnly to, bool isMonthRange, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var all = await db.Bookings.AsNoTracking().Include(b => b.Payment)
            .Where(b => b.DoctorId == doctorId && b.Status == BookingStatus.Completed).ToListAsync(ct);

        static PracticeTotals Sum(IEnumerable<ClinicApp.Domain.Entities.Booking> rows)
        {
            var list = rows.ToList();
            return new PracticeTotals(
                list.Count,
                list.Sum(b => b.TotalFee),
                list.Where(b => b.Payment?.Status == PaymentStatus.Paid).Sum(b => b.Payment!.Amount),
                list.Where(b => b.Payment?.Status == PaymentStatus.Waived).Sum(b => b.Payment!.Amount));
        }

        var totals = Sum(all.Where(b => b.AppointmentDate >= from && b.AppointmentDate <= to));
        var trend = all.GroupBy(b => $"{b.AppointmentDate.Year:0000}-{b.AppointmentDate.Month:00}").OrderBy(g => g.Key)
            .Select(g => { var t = Sum(g); return new MonthTrend(g.Key, t.CompletedVisits, t.Collected); }).ToList();
        int? delta = null;
        if (isMonthRange)
        {
            var prev = from.AddMonths(-1);
            var key = $"{prev.Year:0000}-{prev.Month:00}";
            var p = trend.FirstOrDefault(t => t.Period == key);
            if (p is not null && p.Collected > 0) delta = (int)Math.Round((double)((totals.Collected - p.Collected) / p.Collected * 100));
        }
        return new PracticeAnalytics(totals, trend, delta);
    }
}
// JJQR - END
