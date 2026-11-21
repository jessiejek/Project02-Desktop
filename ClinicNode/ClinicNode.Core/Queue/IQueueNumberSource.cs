//JJQR - AI (21 Nov 2026) - A: where local bookings get their queue number. Implemented by ClinicNode.Sync (QueueBlockNumberSource: numbers only from
// blocks reserved online via POST /api/sync/queue-blocks). Null = this computer has no reserved number left for that day / lane.
// The booking paths (QueueService / StaffBookingService / AdminBookingService) are wired in step C (on hold).
using ClinicApp.Infrastructure;

namespace ClinicNode.Core.Queue;

//JJQR - AI (21 Nov 2026) - TASK C: the desk message when the reserved block is used up (QueueService / StaffBookingService)
public static class QueueNumberMessages
{
    public const string NoNumbersLeft = "This computer has no reserved queue numbers left for today. Connect to the internet once so it can reserve more, or check the patient in on a computer that is online.";
}
//JJQR - END

public interface IQueueNumberSource
{
    Task<string?> NextForPatientAsync(ClinicAppDbContext db, DateOnly day, Guid patientId, string? discountCategory, CancellationToken ct = default);
}
//JJQR - END
