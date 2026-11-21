//JJQR - AI (21 Nov 2026) - Api implementation of IStaffDashboardService, the same calls the web /staff/dashboard makes:
//   GET /api/bookings/staff/today?pageSize=500&activeOnly=true&statuses=Confirmed,CheckedIn   (Arrivals)
//   GET /api/bookings/staff/today?pageSize=1&activeOnly=true                                   ("Patients on today's list" = totalCount)
//   GET /api/queue                                                                             ("Ready for payment": Completed with amount due > 0)
//   PUT /api/bookings/{id}/status { status: CheckedIn | Confirmed }                            (Check in / Undo check-in)
//   PUT /api/bookings/{id}/status { status: Cancelled, reason }                                (Cancel, reason required; not checked in, not paid)
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Staff;
using static ClinicNode.Api.Services.ApiStaff;

namespace ClinicNode.Api.Services;

public sealed class ApiStaffDashboardService(IApiClient api) : IStaffDashboardService
{
    public async Task<StaffDashboard> GetAsync(CancellationToken ct = default)
    {
        var list = await api.GetAsync<Paged<BookingDto>>("/api/bookings/staff/today",
            Q(("pageSize", "500"), ("activeOnly", "true"), ("statuses", "Confirmed,CheckedIn")), ct);
        var count = await api.GetAsync<Paged<BookingDto>>("/api/bookings/staff/today", Q(("pageSize", "1"), ("activeOnly", "true")), ct);
        // Web: a failed queue read leaves the board empty (0 ready) instead of failing the dashboard.
        var board = await Optional(() => api.GetAsync<QueueBoardDto>("/api/queue", null, ct), null);

        var rows = (list?.Items ?? [])
            .Where(b => StaffRules.IsArrival(b.Status))
            .Select(b => new ArrivalRow(b.BookingId, PatientName(b.Patients), b.SlotStartTime ?? TimeOnly.MinValue, b.QueueNumber, b.Status,
                b.Payments?.Status ?? PaymentStatus.Unpaid))
            .ToList();
        var ready = (board?.Items ?? []).Count(e => e.Status == BookingStatus.Completed && (e.AmountDue ?? 0) > 0);
        var total = Math.Max(count?.TotalCount ?? rows.Count, rows.Count); // never below the rows shown
        return new StaffDashboard(rows.Count, ready, total, rows);
    }

    public async Task<StaffResult> ToggleCheckInAsync(Guid bookingId, CancellationToken ct = default)
    {
        BookingDto? b;
        try { b = await api.GetAsync<BookingDto>($"/api/bookings/{bookingId:D}", null, ct); }
        catch (ApiNotFoundException) { return StaffResult.Fail("Booking not found."); }
        if (b is null) return StaffResult.Fail("Booking not found.");
        var next = b.Status == BookingStatus.Confirmed ? BookingStatus.CheckedIn : BookingStatus.Confirmed;
        var r = await Write(() => api.SendAsync(HttpMethod.Put, $"/api/bookings/{bookingId:D}/status", new { status = next.ToString(), reason = (string?)null }, null, ct),
            bookingId, "Could not update the check-in. Please try again.");
        return r.Succeeded ? r : StaffResult.Fail("Could not update the check-in. Please try again.");
    }

    public Task<StaffResult> CancelAsync(Guid bookingId, string? reason, CancellationToken ct = default)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0) return Task.FromResult(StaffResult.Fail("Please enter a reason."));
        return Write(() => api.SendAsync(HttpMethod.Put, $"/api/bookings/{bookingId:D}/status", new { status = nameof(BookingStatus.Cancelled), reason = trimmed }, null, ct),
            bookingId, "Could not cancel the booking. Please try again.");
    }
}
//JJQR - END
