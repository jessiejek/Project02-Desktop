//JJQR - AI (Nov 21, 2026) - "Today’s queue" view model data: six summary cards + rows with the web's per-status actions.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Queue;

namespace ClinicNode.Core.Staff;

public sealed record StaffQueueRow(QueueEntry Entry)
{
    public QueueActions Actions => StaffRules.QueueActionsFor(Entry.Status);
    public bool IsPriority => QueueLane.IsPriority(Entry.QueueNumber);
    public string VisitLabel => StaffRules.VisitTypeLabel(Entry.VisitType);
    public string SourceLabel => Entry.IsWalkIn ? "Walk-in" : "Booked online";
}

public sealed record StaffQueueSummary(int Waiting, int WithDoctor, int BookedNotArrived, int Completed, int NoShow, int Total);

public interface IStaffQueueBoardService
{
    Task<(StaffQueueSummary Summary, IReadOnlyList<StaffQueueRow> Rows)> GetAsync(CancellationToken ct = default);
    Task<StaffResult> ActAsync(Guid bookingId, string action, CancellationToken ct = default);
    Task<StaffResult> CancelAsync(Guid bookingId, string? reason, CancellationToken ct = default);
}

public sealed class StaffQueueBoardService : IStaffQueueBoardService
{
    private readonly IQueueService _queue;

    public StaffQueueBoardService(IQueueService queue) => _queue = queue;

    public async Task<(StaffQueueSummary Summary, IReadOnlyList<StaffQueueRow> Rows)> GetAsync(CancellationToken ct = default)
    {
        var b = await _queue.GetBoardAsync(null, ct);
        return (new StaffQueueSummary(b.Waiting, b.InProgress, b.Booked, b.Completed, b.NoShow, b.Total), b.Items.Select(e => new StaffQueueRow(e)).ToList());
    }

    /// <summary>action = check-in | call | hold | complete | no-show (web updateQueueEntry).</summary>
    public async Task<StaffResult> ActAsync(Guid bookingId, string action, CancellationToken ct = default)
    {
        var r = action switch
        {
            "check-in" => await _queue.CheckInBookedAsync(bookingId, ct),
            "call" => await _queue.CallAsync(bookingId, ct),
            "hold" => await _queue.HoldAsync(bookingId, ct),
            "complete" => await _queue.CompleteAsync(bookingId, ct),
            "no-show" => await _queue.NoShowAsync(bookingId, ct),
            _ => QueueActionResult.Fail("Unknown queue action."),
        };
        return r.Succeeded ? StaffResult.Ok(bookingId) : StaffResult.Fail(r.Error ?? "That queue update didn’t go through. Please try again.");
    }

    public async Task<StaffResult> CancelAsync(Guid bookingId, string? reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return StaffResult.Fail("Please enter a reason.");
        var r = await _queue.CancelAsync(bookingId, reason.Trim(), ct);
        return r.Succeeded ? StaffResult.Ok(bookingId) : StaffResult.Fail(r.Error ?? "Could not cancel the booking. Please try again.");
    }
}
//JJQR - END
