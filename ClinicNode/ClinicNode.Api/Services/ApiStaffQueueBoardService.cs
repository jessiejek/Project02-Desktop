//JJQR - AI (21 Nov 2026) - Api implementation of IStaffQueueBoardService, the same calls the web /staff/queue makes:
//   GET /api/queue                                   (six counts + rows; S- lane first, then first come first served — ordered by the server)
//   PUT /api/queue/{id}/check-in|call|hold|complete|no-show {}
//   PUT /api/queue/{id}/cancel { reason }            (reason required; Pending / CheckedIn / OnHold only; refused once paid)
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using static ClinicNode.Api.Services.ApiStaff;

namespace ClinicNode.Api.Services;

public sealed class ApiStaffQueueBoardService(IApiClient api) : IStaffQueueBoardService
{
    static readonly HashSet<string> Actions = ["check-in", "call", "hold", "complete", "no-show"];

    public async Task<(StaffQueueSummary Summary, IReadOnlyList<StaffQueueRow> Rows)> GetAsync(CancellationToken ct = default)
    {
        var b = await api.GetAsync<QueueBoardDto>("/api/queue", null, ct);
        var s = b?.Summary ?? new QueueSummaryDto(0, 0, 0, 0, 0, 0);
        var rows = (b?.Items ?? []).Select(e => new StaffQueueRow(ToEntry(e))).ToList();
        return (new StaffQueueSummary(s.Waiting, s.InProgress, s.Booked, s.Completed, s.NoShow, s.Total), rows);
    }

    internal static QueueEntry ToEntry(QueueItemDto e) => new(
        e.BookingId, e.PatientId, Guid.Empty, e.PatientName ?? "", e.PatientCode ?? "", e.QueueNumber,
        e.IsPriority ?? ClinicApp.Domain.QueueLane.IsPriority(e.QueueNumber), e.Status, PaymentStatus.Unpaid, e.VisitType ?? VisitType.New,
        e.IsWalkIn ?? false, e.AmountDue ?? 0, ManilaTime(e.CheckedInAt), e.CheckedInAt ?? DateTimeOffset.MinValue, null, null);

    public Task<StaffResult> ActAsync(Guid bookingId, string action, CancellationToken ct = default)
    {
        if (!Actions.Contains(action)) return Task.FromResult(StaffResult.Fail("Unknown queue action."));
        return Write(() => api.SendAsync(HttpMethod.Put, $"/api/queue/{bookingId:D}/{action}", new { }, null, ct),
            bookingId, "That queue update didn’t go through. Please try again.");
    }

    public Task<StaffResult> CancelAsync(Guid bookingId, string? reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Task.FromResult(StaffResult.Fail("Please enter a reason."));
        return Write(() => api.SendAsync(HttpMethod.Put, $"/api/queue/{bookingId:D}/cancel", new { reason = reason.Trim() }, null, ct),
            bookingId, "Could not cancel the booking. Please try again.");
    }
}
//JJQR - END
