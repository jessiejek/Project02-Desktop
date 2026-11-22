// JJQR - AI (Nov 21, 2026) - start a local walk-in visit so a doctor can consult offline without the live queue.
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Queue;

namespace ClinicNode.Core.Visits;

public sealed record TodayVisit(
    Guid BookingId,
    Guid PatientId,
    string PatientName,
    string PatientCode,
    string? QueueNumber,
    BookingStatus Status,
    TimeOnly SlotStart,
    bool IsWalkIn);

public interface IVisitStarter
{
    Task<IReadOnlyList<TodayVisit>> ListTodayAsync(Guid doctorId, CancellationToken ct = default);

    /// <summary>Create a walk-in booking + unpaid payment (client-supplied ids) and return the booking id to open in consultation.</summary>
    Task<Guid> StartWalkInConsultAsync(Guid patientId, Guid doctorId, string? discountCategory, CancellationToken ct = default);
}

public sealed class VisitStarter : IVisitStarter
{
    private readonly IQueueService _queue;
    private readonly IAuthService _auth;

    public VisitStarter(IQueueService queue, IAuthService auth)
    {
        _queue = queue;
        _auth = auth;
    }

    public async Task<IReadOnlyList<TodayVisit>> ListTodayAsync(Guid doctorId, CancellationToken ct = default)
    {
        var rows = await _queue.ListDoctorTodayAsync(doctorId, ct);
        return rows.Select(b => new TodayVisit(
            b.BookingId,
            b.PatientId,
            b.PatientName,
            b.PatientCode,
            b.QueueNumber,
            b.Status,
            b.SlotStart,
            b.IsWalkIn)).ToList();
    }

    public async Task<Guid> StartWalkInConsultAsync(Guid patientId, Guid doctorId, string? discountCategory, CancellationToken ct = default)
    {
        _ = _auth.Current; // must be signed in so CreatedByUserId and later consult gates have an actor
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.3: the ids are made here, before the call, so an API attempt and a local fallback of this same request use the same ids //JJQR - END
        var result = await _queue.CheckInWalkInAsync(new WalkInRequest(patientId, doctorId, DiscountCategory: discountCategory, BookingId: Guid.NewGuid(), PaymentId: Guid.NewGuid()), ct);
        if (!result.Succeeded || result.BookingId is null)
            throw new InvalidOperationException(result.Error ?? "Could not start the walk-in visit.");
        return result.BookingId.Value;
    }
}
// JJQR - END
