//JJQR - AI (Nov 21, 2026) - staff dashboard ("Front desk today"): stat cards, ready-to-pay banner, Arrivals with check-in / undo / Cancel.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Staff;

public sealed record ArrivalRow(Guid BookingId, string PatientName, TimeOnly SlotStart, string? QueueNumber, BookingStatus Status, PaymentStatus PaymentStatus)
{
    public bool CanCancel => StaffRules.DashboardCanCancel(Status, PaymentStatus);
    public string CheckInLabel => StaffRules.CheckInLabel(Status);
}

public sealed record StaffDashboard(int BookedOrCheckedIn, int ReadyForPayment, int PatientsToday, IReadOnlyList<ArrivalRow> Arrivals);

public interface IStaffDashboardService
{
    Task<StaffDashboard> GetAsync(CancellationToken ct = default);
    Task<StaffResult> ToggleCheckInAsync(Guid bookingId, CancellationToken ct = default);
    Task<StaffResult> CancelAsync(Guid bookingId, string? reason, CancellationToken ct = default);
}

public sealed class StaffDashboardService : IStaffDashboardService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IQueueService _queue;

    public StaffDashboardService(IDbContextFactory<ClinicAppDbContext> factory, IQueueService queue)
    {
        _factory = factory;
        _queue = queue;
    }

    public async Task<StaffDashboard> GetAsync(CancellationToken ct = default)
    {
        var today = ClinicClock.Today;
        await using var db = await _factory.CreateDbContextAsync(ct);
        // GET /api/bookings/staff/today?activeOnly=true — live rows only, ordered by slot time.
        var live = await db.Bookings.AsNoTracking()
            .Include(b => b.Patient).Include(b => b.Payment)
            .Where(b => b.AppointmentDate == today && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired)
            .ToListAsync(ct);
        live = live.OrderBy(b => b.SlotStartTime).ToList();
        var arrivals = live.Where(b => StaffRules.IsArrival(b.Status))
            .Select(b => new ArrivalRow(
                b.BookingId,
                b.Patient is null ? "" : $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
                b.SlotStartTime, b.QueueNumber, b.Status, b.Payment?.Status ?? PaymentStatus.Unpaid))
            .ToList();
        // "Ready for payment" reads the queue board (same source as Queue / Payments): Completed with amount due > 0.
        var board = await _queue.GetBoardAsync(today, ct);
        var ready = board.Items.Count(e => e.Status == BookingStatus.Completed && e.AmountDue > 0);
        return new StaffDashboard(arrivals.Count, ready, live.Count, arrivals);
    }

    public async Task<StaffResult> ToggleCheckInAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var status = await db.Bookings.AsNoTracking().Where(b => b.BookingId == bookingId).Select(b => (BookingStatus?)b.Status).SingleOrDefaultAsync(ct);
        if (status is null) return StaffResult.Fail("Booking not found.");
        var result = status == BookingStatus.Confirmed
            ? await _queue.CheckInBookedAsync(bookingId, ct)
            : await _queue.UndoCheckInAsync(bookingId, ct);
        return result.Succeeded ? StaffResult.Ok(bookingId) : StaffResult.Fail("Could not update the check-in. Please try again.");
    }

    public async Task<StaffResult> CancelAsync(Guid bookingId, string? reason, CancellationToken ct = default)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0) return StaffResult.Fail("Please enter a reason.");
        var result = await _queue.CancelAsync(bookingId, trimmed, ct);
        return result.Succeeded ? StaffResult.Ok(bookingId) : StaffResult.Fail(result.Error ?? "Could not cancel the booking. Please try again.");
    }
}
//JJQR - END
