// JJQR - AI (Nov 21, 2026) - queue / walk-in / dashboard DTOs.
using ClinicApp.Domain.Enums;

namespace ClinicNode.Core.Queue;

public sealed record QueueEntry(
    Guid BookingId,
    Guid PatientId,
    Guid DoctorId,
    string PatientName,
    string PatientCode,
    string? QueueNumber,
    bool IsPriority,
    BookingStatus Status,
    PaymentStatus PaymentStatus,
    VisitType VisitType,
    bool IsWalkIn,
    decimal AmountDue,
    TimeOnly SlotStart,
    DateTimeOffset CreatedAt,
    string? DiscountCategory,
    string? CancellationReason);

public sealed record QueueBoard(
    DateOnly Date,
    int Booked,
    int Waiting,
    int InProgress,
    int Completed,
    int NoShow,
    int Total,
    int ReadyForPayment,
    IReadOnlyList<QueueEntry> Items);

public sealed record WalkInRequest(
    Guid PatientId,
    Guid? DoctorId,
    VisitType VisitType = VisitType.New,
    bool MedCertRequested = false,
    string? DiscountCategory = null,
    string? Notes = null,
    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.3: ids made by the DESKTOP before the call, so the API call and a local fallback of the SAME request use the same ids (a lost reply cannot create a second booking)
    Guid? BookingId = null,
    Guid? PaymentId = null);

public sealed record WalkInTicket(
    Guid BookingId,
    Guid PaymentId,
    string QueueNumber,
    string Lane,
    bool IsPriority,
    string PatientName,
    string PatientCode,
    string DoctorName,
    string ClinicName,
    string? ClinicAddress,
    VisitType VisitType,
    decimal ProvisionalFee,
    DateTimeOffset IssuedAt);

public sealed record DoctorListItem(Guid DoctorId, string FullName, string Specialization);

public sealed class QueueActionResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public Guid? BookingId { get; init; }
    public BookingStatus? Status { get; init; }
    public WalkInTicket? Ticket { get; init; }

    public static QueueActionResult Fail(string error) => new() { Succeeded = false, Error = error };
    public static QueueActionResult Ok(Guid bookingId, BookingStatus status) =>
        new() { Succeeded = true, BookingId = bookingId, Status = status };
    public static QueueActionResult OkTicket(WalkInTicket ticket) =>
        new() { Succeeded = true, BookingId = ticket.BookingId, Status = BookingStatus.CheckedIn, Ticket = ticket };
}
// JJQR - END
