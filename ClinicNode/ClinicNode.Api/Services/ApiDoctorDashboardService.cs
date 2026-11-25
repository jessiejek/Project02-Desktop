//JJQR - AI (Nov 21, 2026) - Api implementation of IDoctorDashboardService, composed from the SAME endpoints the web /doctor/dashboard uses:
//   GET /api/doctors/{id} · GET /api/doctors/{id}/day-status?date= · GET /api/bookings/doctor/today · GET /api/doctor/performance?period=today · GET /api/announcements
//   PUT /api/doctors/{id}/day-status · GET /api/reports/doctor-earnings (= v_doctor_earnings)
// Methods the API cannot serve yet throw ApiNotSupportedException, so the router uses the Local service for them (CallNext / StartConsult: queue endpoints, not converted yet).
// API gaps (also in docs/ONLINE-FIRST.md): no single dashboard endpoint, so the month tiles are derived from today's data and Rx / lab print flags are false.
using System.Globalization;
using System.Text.Json.Nodes; //JJQR - AI (21 Nov 2026) //JJQR - END
using ClinicApp.Domain;
using ClinicApp.Domain.Entities; //JJQR - AI (Nov 25, 2026) - VUnpaidCompletedVisit (GAV-2) //JJQR - END
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Staff;

namespace ClinicNode.Api.Services;

public sealed class ApiDoctorDashboardService(IApiClient api) : IDoctorDashboardService
{
    internal sealed record StaffDto(string? FullName);
    internal sealed record DoctorDto(Guid DoctorId, StaffDto? StaffAccounts);
    internal sealed record DayStatusDto(Guid? DoctorId, DateOnly StatusDate, DoctorDayStatusEnum Status, int? RunningLateMinutes);
    internal sealed record PatientDto(string? FirstName, string? LastName);
    internal sealed record BookingDto(Guid BookingId, Guid PatientId, DateOnly AppointmentDate, string? SlotStartTime, BookingStatus Status, string? QueueNumber,
        VisitType VisitType, string? DiscountCategory, DateTimeOffset? UpdatedAt, PatientDto? Patients);
    internal sealed record AnnouncementDto(string Title, string Body, bool IsActive, DateTimeOffset CreatedAt);
    internal sealed record EarningsRowDto(Guid? DoctorId, string? Period, int? CompletedVisits, decimal? GrossBilled, decimal? Collected, decimal? Waived);

    //JJQR - AI (21 Nov 2026) - web /doctor/dashboard loadAttention + loadClinicOverview: GET /api/consultations?doctorId= (Finish chart, follow-ups due today)
    // and GET /api/reports/pending-follow-ups (7-day tile). PRN follow-ups (lib/pf-default isPrnFollowUp: "PRN" in reason or instructions) are filtered HERE.
    internal sealed record PendingFollowUpDto(Guid? DoctorId, DateOnly? FollowUpDate, string? Reason);

    internal static bool IsPrn(string? reason, string? instructions) =>
        (reason ?? "").Contains("PRN", StringComparison.OrdinalIgnoreCase) || (instructions ?? "").Contains("PRN", StringComparison.OrdinalIgnoreCase);

    private static JsonObject? Pick(JsonNode? n) => n switch { JsonArray a => a.Count > 0 ? a[0] as JsonObject : null, JsonObject o => o, _ => null };

    private static string? S(JsonObject? o, string key) => o?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    //JJQR - END

    private static string Name(PatientDto? p) => $"{p?.FirstName} {p?.LastName}".Trim() is { Length: > 0 } n ? n : "Patient";
    private static bool IsPriority(BookingDto b) =>
        (b.QueueNumber ?? "").StartsWith("S-", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(b.DiscountCategory);
    private static string Slot(string? t) =>
        TimeOnly.TryParse(t, CultureInfo.InvariantCulture, out var v) ? v.ToString("h:mm tt", CultureInfo.InvariantCulture) : "—";
    private static string Peso(decimal v) => "₱" + v.ToString("#,##0.##", CultureInfo.InvariantCulture);

    public async Task<DoctorDashboard> GetAsync(Guid doctorId, CancellationToken ct = default)
    {
        var today = ClinicClock.Today;
        //JJQR - AI (Nov 22, 2026) - owner: slow Today page. Six independent reads were made one after another; they now go out AT THE SAME TIME.
        var tDoctor = api.GetAsync<DoctorDto>($"/api/doctors/{doctorId:D}", null, ct);
        var tStatus = api.GetAsync<DayStatusDto>($"/api/doctors/{doctorId:D}/day-status", new Dictionary<string, string?> { ["date"] = today.ToString("yyyy-MM-dd") }, ct);
        var tBookings = api.GetAsync<List<BookingDto>>("/api/bookings/doctor/today", null, ct);
        var tPerf = new ApiDoctorPerformanceService(api).GetAsync(doctorId, PerformancePeriodName.Today, today, ct);
        var tAnnouncements = api.GetAsync<List<AnnouncementDto>>("/api/announcements", null, ct);
        var tConsults = api.GetAsync<List<JsonObject>>("/api/consultations", new Dictionary<string, string?> { ["doctorId"] = doctorId.ToString("D") }, ct);
        //JJQR - AI (Nov 25, 2026) - GAV-2: earlier days' Needs attention (web loadClinicOverview): unpaid completed visits from the report, and this doctor's
        // bookings of the last AttentionGroups.DaysBack days for the patient names on older charts. Both optional: a failed read only leaves older dates out.
        var attentionFrom = AttentionGroups.From(today);
        var tUnpaidReport = api.GetAsync<List<VUnpaidCompletedVisit>>("/api/reports/unpaid-completed-visits", null, ct);
        var tRecentBookings = api.GetAsync<List<BookingDto>>("/api/bookings", new Dictionary<string, string?> { ["doctorId"] = doctorId.ToString("D"), ["from"] = attentionFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }, ct);
        //JJQR - END
        var doctor = await tDoctor;
        var status = await tStatus;
        var bookings = await tBookings ?? [];
        var perf = await tPerf;
        var announcements = await tAnnouncements ?? [];
        //JJQR - END

        var now = DateTimeOffset.UtcNow;
        int Wait(BookingDto b) => b.UpdatedAt is { } u ? Math.Max(0, (int)(now - u).TotalMinutes) : 0;
        var live = bookings.Where(b => b.Status is BookingStatus.CheckedIn or BookingStatus.InProgress).ToList();
        var upNext = live
            .OrderBy(b => b.Status == BookingStatus.InProgress ? 0 : 1)
            .ThenBy(b => IsPriority(b) ? 0 : 1)
            .ThenBy(b => b.QueueNumber ?? "", Comparer<string>.Create(DoctorExtraRules.CompareQueueNumbers))
            .Select(b => new UpNextItem(b.BookingId, Name(b.Patients), b.QueueNumber ?? "—", IsPriority(b), b.VisitType, b.Status == BookingStatus.InProgress, Wait(b)))
            .ToList();
        var waiting = bookings.Where(b => b.Status == BookingStatus.CheckedIn).ToList();

        var unpaid = (perf?.Unpaid.Items ?? []).Select(u => new AttentionItem(AttentionKind.UnpaidCompleted, u.BookingId, u.PatientName, $"{Peso(u.AmountDue)} still to collect", "Open", u.AppointmentDate.ToString("MMM d", CultureInfo.InvariantCulture), u.AppointmentDate)).ToList();
        //JJQR - AI (Nov 25, 2026) - GAV-2: unpaid completed visits from earlier days stay listed under their date
        List<BookingDto> recentBookings = [];
        try
        {
            foreach (var r in await tUnpaidReport ?? [])
                if (r.BookingId is { } rid && r.DoctorId == doctorId && r.AppointmentDate is { } rd && rd >= attentionFrom && rd < today && (r.AmountDue ?? 0) > 0)
                    unpaid.Add(new AttentionItem(AttentionKind.UnpaidCompleted, rid, r.PatientName ?? "", $"{Peso(r.AmountDue ?? 0)} still to collect", "Open", rd.ToString("MMM d", CultureInfo.InvariantCulture), rd));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ApiOfflineException and not ApiUnauthorizedException) { }
        try { recentBookings = await tRecentBookings ?? []; }
        catch (Exception ex) when (ex is not OperationCanceledException and not ApiOfflineException and not ApiUnauthorizedException) { }
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - Finish chart + follow-ups due today exactly as web loadAttention (same texts as the Local service):
        //   chart  = today's Completed consultation with BOTH Assessment and Plan blank ("{name} — Assessment and Plan are blank" · "Finish chart")
        //   follow = consultation whose follow-up date is today and is NOT PRN ("{name} — follow-up due today" · "View chart")
        // Names come from today's bookings (the consultation list has none), else "A patient". A failed read leaves both lists empty (web catch).
        var todayIso = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var nameByBooking = bookings.Concat(recentBookings).GroupBy(b => b.BookingId).ToDictionary(g => g.Key, g => Name(g.First().Patients)); //JJQR - AI (Nov 25, 2026) - GAV-2: + the last 30 days //JJQR - END
        string Who(Guid id) => nameByBooking.TryGetValue(id, out var n) && n != "Patient" ? n : "A patient";
        var charts = new List<AttentionItem>();
        var followUps = new List<AttentionItem>();
        try
        {
            var consults = await tConsults ?? [];
            foreach (var c in consults)
            {
                if (!Guid.TryParse(S(c, "booking_id"), out var bookingId)) continue;
                //JJQR - AI (Nov 25, 2026) - GAV-2: a blank chart from an earlier day (last AttentionGroups.DaysBack days) stays listed under its date
                var visitIso = S(Pick(c["bookings"]), "appointment_date") ?? "";
                var inWindow = DateOnly.TryParseExact(visitIso.Length >= 10 ? visitIso[..10] : visitIso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var visitDate)
                    && visitDate >= attentionFrom && visitDate <= today;
                if (inWindow && string.Equals(S(c, "status"), nameof(ConsultationStatus.Completed), StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(S(c, "assessment")) && string.IsNullOrWhiteSpace(S(c, "plan")))
                    charts.Add(new AttentionItem(AttentionKind.FinishChart, bookingId, Who(bookingId), $"{Who(bookingId)} — Assessment and Plan are blank", "Finish chart", "Finish chart", visitDate));
                //JJQR - END
                var f = Pick(c["follow_ups"]);
                if ((S(f, "follow_up_date") ?? "").StartsWith(todayIso, StringComparison.Ordinal) && !IsPrn(S(f, "reason"), S(f, "instructions")))
                    followUps.Add(new AttentionItem(AttentionKind.FollowUpDue, bookingId, Who(bookingId), $"{Who(bookingId)} — follow-up due today", "View chart", "View chart", today));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ApiOfflineException and not ApiUnauthorizedException)
        {
            charts.Clear();
            followUps.Clear();
        }

        // "Follow-Ups Due in 7 Days" tile: v_pending_follow_ups for this doctor, today..today+7, PRN excluded (web loadClinicOverview)
        int? followUps7 = null;
        try
        {
            var in7 = today.AddDays(7);
            followUps7 = (await api.GetAsync<List<PendingFollowUpDto>>("/api/reports/pending-follow-ups", null, ct) ?? [])
                .Count(r => r.DoctorId == doctorId && r.FollowUpDate is { } d && d >= today && d <= in7 && !IsPrn(r.Reason, null));
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ApiOfflineException and not ApiUnauthorizedException) { }
        //JJQR - END
        var recent = bookings.OrderByDescending(b => b.SlotStartTime).Take(10)
            .Select(b => new RecentBooking(b.BookingId, Name(b.Patients), Slot(b.SlotStartTime), b.QueueNumber, b.Status)).ToList();

        return new DoctorDashboard(
            doctor?.StaffAccounts?.FullName ?? "Doctor",
            status?.Status ?? DoctorDayStatusEnum.Available,
            Waiting: waiting.Count,
            InProgress: bookings.Count(b => b.Status == BookingStatus.InProgress),
            CompletedToday: bookings.Count(b => b.Status == BookingStatus.Completed),
            TotalToday: bookings.Count(b => b.Status is not (BookingStatus.Cancelled or BookingStatus.Expired)),
            LongestWaitMinutes: waiting.Count == 0 ? 0 : waiting.Max(Wait),
            upNext, unpaid, charts, followUps,
            PendingVerifications: bookings.Count(b => b.Status == BookingStatus.ProofSubmitted),
            OnHoldMonth: bookings.Count(b => b.Status == BookingStatus.OnHold),
            UnpaidCompletedMonth: perf?.Unpaid.Count ?? 0,
            FollowUpsDue7Days: followUps7 ?? (perf?.FollowUpsDue.Items ?? []).Count(f => !IsPrn(f.Reason, null)), //JJQR - AI (21 Nov 2026) - PRN excluded //JJQR - END
            recent,
            announcements.Where(a => a.IsActive).OrderByDescending(a => a.CreatedAt).Select(a => new AnnouncementItem(a.Title, a.Body, a.CreatedAt)).ToList());
    }

    public async Task<StaffResult> SetDayStatusAsync(Guid doctorId, DoctorDayStatusEnum status, CancellationToken ct = default)
    {
        try
        {
            await api.SendAsync(HttpMethod.Put, $"/api/doctors/{doctorId:D}/day-status",
                new { status_date = ClinicClock.Today.ToString("yyyy-MM-dd"), status = status.ToString(), running_late_minutes = (int?)null }, null, ct);
            return StaffResult.Ok(doctorId);
        }
        catch (Exception ex) when (ex is ApiValidationException or ApiForbiddenException or ApiNotFoundException or ApiConflictException)
        {
            return StaffResult.Fail(ex.Message); // a real answer from the server: shown, never retried locally
        }
    }

    public async Task<PracticeAnalytics> GetAnalyticsAsync(Guid doctorId, DateOnly from, DateOnly to, bool isMonthRange, CancellationToken ct = default)
    {
        // v_doctor_earnings is monthly, so only a calendar-month range can be answered exactly from it; any other range uses the Local bookings query.
        if (!isMonthRange) throw new ApiNotSupportedException("Custom date ranges are answered from the local copy.");
        var rows = (await api.GetAsync<List<EarningsRowDto>>("/api/reports/doctor-earnings", new Dictionary<string, string?> { ["doctorId"] = doctorId.ToString("D") }, ct) ?? [])
            .Where(r => r.Period is not null && (r.DoctorId is null || r.DoctorId == doctorId)).ToList();
        var trend = rows.OrderBy(r => r.Period, StringComparer.Ordinal).Select(r => new MonthTrend(r.Period!, r.CompletedVisits ?? 0, r.Collected ?? 0)).ToList();
        var key = $"{from.Year:0000}-{from.Month:00}";
        var cur = rows.FirstOrDefault(r => r.Period == key);
        var totals = new PracticeTotals(cur?.CompletedVisits ?? 0, cur?.GrossBilled ?? 0, cur?.Collected ?? 0, cur?.Waived ?? 0);
        var prev = from.AddMonths(-1);
        var p = trend.FirstOrDefault(t => t.Period == $"{prev.Year:0000}-{prev.Month:00}");
        int? delta = p is not null && p.Collected > 0 ? (int)Math.Round((double)((totals.Collected - p.Collected) / p.Collected * 100)) : null;
        return new PracticeAnalytics(totals, trend, delta);
    }

    public Task<StaffResult> CallNextAsync(Guid doctorId, CancellationToken ct = default) => throw new ApiNotSupportedException("The queue is served from the local copy until it is converted.");
    public Task<StaffResult> StartConsultAsync(Guid doctorId, Guid bookingId, CancellationToken ct = default) => throw new ApiNotSupportedException("The queue is served from the local copy until it is converted.");
}
//JJQR - END
