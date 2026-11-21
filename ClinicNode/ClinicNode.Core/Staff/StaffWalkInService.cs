//JJQR - AI (21 Nov 2026) - walk-in check-in seam for the online-first router: the web WalkInWizard posts POST /api/queue (ApiStaffWalkInService);
// offline it is the node's own IQueueService.CheckInWalkInAsync (R-/S- number from the local per-lane, per-day counter), exactly as before.
using ClinicNode.Core.Queue;

namespace ClinicNode.Core.Staff;

public interface IStaffWalkInService
{
    /// <summary>"Check in and get queue number": S- for Senior / PWD (served first), R- otherwise; separate counters per lane per day.</summary>
    Task<QueueActionResult> CheckInWalkInAsync(WalkInRequest request, CancellationToken ct = default);
}

public sealed class StaffWalkInService(IQueueService queue) : IStaffWalkInService
{
    public Task<QueueActionResult> CheckInWalkInAsync(WalkInRequest request, CancellationToken ct = default) => queue.CheckInWalkInAsync(request, ct);
}
//JJQR - END
