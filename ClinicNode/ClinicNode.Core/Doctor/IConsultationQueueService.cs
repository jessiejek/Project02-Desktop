//JJQR - AI (21 Nov 2026) - inventory G9: the doctor's "Call next" / "Start consultation" (TodayPage) online-first.
// Online: ApiConsultationQueueService (PUT /api/queue/{bookingId}/call, Doctor allowed on their own queue).
// Offline: LocalConsultationQueueService → the existing local IDoctorDashboardService.CallNextAsync / StartConsultAsync (unchanged).
using ClinicNode.Core.Practice;
using ClinicNode.Core.Staff;

namespace ClinicNode.Core.Doctor;

public interface IConsultationQueueService
{
    /// <summary>First checked-in patient of this doctor (priority lane, then arrival order) → In progress. Ok(bookingId).</summary>
    Task<StaffResult> CallNextAsync(Guid doctorId, CancellationToken ct = default);
    /// <summary>Checked-in → In progress (already in progress = Ok). Ok(bookingId).</summary>
    Task<StaffResult> StartConsultAsync(Guid doctorId, Guid bookingId, CancellationToken ct = default);
}

public sealed class LocalConsultationQueueService(IDoctorDashboardService dash) : IConsultationQueueService
{
    public Task<StaffResult> CallNextAsync(Guid doctorId, CancellationToken ct = default) => dash.CallNextAsync(doctorId, ct);
    public Task<StaffResult> StartConsultAsync(Guid doctorId, Guid bookingId, CancellationToken ct = default) => dash.StartConsultAsync(doctorId, bookingId, ct);
}
//JJQR - END
