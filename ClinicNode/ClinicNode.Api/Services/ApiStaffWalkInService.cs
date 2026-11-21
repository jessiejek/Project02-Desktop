//JJQR - AI (21 Nov 2026) - Api implementation of IStaffWalkInService (web WalkInWizard → checkInWalkIn):
//   POST /api/queue { patient_id, visit_type, med_cert_requested, discount_category, notes } → printable ticket
// The server issues the queue number (S- for Senior / PWD / 60+, R- otherwise; separate counters per lane per day) and the provisional fee.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using static ClinicNode.Api.Services.ApiStaff;

namespace ClinicNode.Api.Services;

public sealed class ApiStaffWalkInService(IApiClient api) : IStaffWalkInService
{
    internal sealed record TicketDto(Guid BookingId, string? QueueNumber, string? Lane, bool? IsPriority, int? Sequence, string? PatientName, string? PatientCode,
        string? DoctorName, string? ClinicName, string? ClinicAddress, VisitType? VisitType, decimal? ProvisionalFee, DateTimeOffset? IssuedAt);

    public async Task<QueueActionResult> CheckInWalkInAsync(WalkInRequest request, CancellationToken ct = default)
    {
        TicketDto? t;
        try
        {
            t = await api.SendAsync<TicketDto>(HttpMethod.Post, "/api/queue", new
            {
                patient_id = request.PatientId,
                visit_type = request.VisitType.ToString(),
                med_cert_requested = request.MedCertRequested,
                discount_category = string.IsNullOrWhiteSpace(request.DiscountCategory) ? null : request.DiscountCategory,
                notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            }, null, ct);
        }
        catch (Exception ex) when (IsServerAnswer(ex))
        {
            return QueueActionResult.Fail(string.IsNullOrWhiteSpace(ex.Message) ? "Could not check this patient in. Please try again." : ex.Message);
        }
        if (t is null || string.IsNullOrWhiteSpace(t.QueueNumber)) return QueueActionResult.Fail("Could not check this patient in. Please try again.");
        var priority = t.IsPriority ?? QueueLane.IsPriority(t.QueueNumber);
        return QueueActionResult.OkTicket(new WalkInTicket(
            t.BookingId, Guid.Empty, t.QueueNumber, t.Lane ?? (priority ? QueueLane.Priority : QueueLane.Regular), priority,
            t.PatientName ?? "", t.PatientCode ?? "", t.DoctorName ?? "", t.ClinicName ?? "", t.ClinicAddress,
            t.VisitType ?? request.VisitType, t.ProvisionalFee ?? 0, t.IssuedAt ?? DateTimeOffset.UtcNow));
    }
}
//JJQR - END
