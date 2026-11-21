// JJQR - AI (Nov 21, 2026) - front-desk payments: exact-amount collection and Confirm waiver. No refunds offline.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Events;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Payments;

public sealed record PaymentListItem(
    Guid PaymentId,
    Guid BookingId,
    string PatientName,
    string PatientCode,
    string? QueueNumber,
    DateOnly AppointmentDate,
    BookingStatus BookingStatus,
    PaymentStatus Status,
    decimal AmountDue,
    string? WaivedReason,
    DateTimeOffset? ConfirmedAt,
    bool AwaitingWaiverConfirmation);

public sealed record ConfirmPaymentRequest(
    PaymentMethod PaymentMethod,
    string? ReferenceNumber = null,
    string? OrNumber = null,
    decimal? AmountReceived = null,
    string? ConfirmNotes = null);

public sealed class PaymentActionResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public Guid? PaymentId { get; init; }

    public static PaymentActionResult Fail(string error) => new() { Succeeded = false, Error = error };
    public static PaymentActionResult Ok(Guid paymentId) => new() { Succeeded = true, PaymentId = paymentId };
}

public interface IPaymentService
{
    Task<IReadOnlyList<PaymentListItem>> ListUnpaidAsync(string? query = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentListItem>> ListWaivedAwaitingConfirmationAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PaymentMethod>> ListMethodsAsync(CancellationToken ct = default);
    Task<PaymentActionResult> ConfirmAsync(Guid paymentId, ConfirmPaymentRequest request, CancellationToken ct = default);
    Task<PaymentActionResult> ConfirmWaiverAsync(Guid paymentId, CancellationToken ct = default);
}

public sealed class PaymentService : IPaymentService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;
    private readonly IClinicEventBus _events;
    //JJQR - AI (Nov 21, 2026)
    private readonly ClinicNode.Core.Sync.ISyncBridge _sync;
    //JJQR - END

    public PaymentService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth, IClinicEventBus events, ClinicNode.Core.Sync.ISyncBridge? sync = null)
    {
        //JJQR - AI (Nov 21, 2026)
        _sync = sync ?? new ClinicNode.Core.Sync.NoOpSyncBridge();
        //JJQR - END
        _factory = factory;
        _outbox = outbox;
        _auth = auth;
        _events = events;
    }

    public async Task<IReadOnlyList<PaymentListItem>> ListUnpaidAsync(string? query = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = db.Payments.AsNoTracking()
            .Include(p => p.Booking).ThenInclude(b => b.Patient)
            .Where(p => p.Status == PaymentStatus.Unpaid);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var s = query.Trim();
            rows = rows.Where(p =>
                (p.Booking.Patient.FirstName + " " + p.Booking.Patient.LastName).Contains(s)
                || p.Booking.Patient.PatientCode.Contains(s)
                || (p.Booking.QueueNumber != null && p.Booking.QueueNumber.Contains(s)));
        }
        var list = await rows.OrderBy(p => p.Booking.AppointmentDate).ThenBy(p => p.Booking.QueueNumber).ToListAsync(ct);
        return list.Select(ToItem).ToList();
    }

    public async Task<IReadOnlyList<PaymentListItem>> ListWaivedAwaitingConfirmationAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var list = await db.Payments.AsNoTracking()
            .Include(p => p.Booking).ThenInclude(b => b.Patient)
            .Where(p => p.Status == PaymentStatus.Waived && p.ConfirmedAt == null && p.Booking.Status == BookingStatus.Completed)
            .OrderByDescending(p => p.Booking.AppointmentDate).ThenBy(p => p.Booking.QueueNumber)
            .ToListAsync(ct);
        return list.Select(ToItem).ToList();
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListMethodsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var accepted = await db.ClinicAcceptedPaymentMethods.AsNoTracking().Select(m => m.PaymentMethod).ToListAsync(ct);
        if (accepted.Count == 0)
            return Enum.GetValues<PaymentMethod>();
        return accepted;
    }

    public async Task<PaymentActionResult> ConfirmAsync(Guid paymentId, ConfirmPaymentRequest request, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var payment = await db.Payments.Include(p => p.Booking).SingleOrDefaultAsync(p => p.PaymentId == paymentId, ct);
        if (payment is null) return PaymentActionResult.Fail("Payment not found.");
        if (payment.Status == PaymentStatus.Waived)
            return PaymentActionResult.Fail("The doctor waived this professional fee, so there is nothing to collect.");

        var illegal = PaymentStatusMachine.RejectReason(payment.Status, PaymentStatus.Paid);
        if (illegal is not null) return PaymentActionResult.Fail(illegal);
        if (!Enum.IsDefined(request.PaymentMethod))
            return PaymentActionResult.Fail("Choose a valid payment method.");
        if (request.AmountReceived is { } received && received != payment.Amount)
            return PaymentActionResult.Fail($"Amount received (₱{received:0.##}) must be exactly the amount due (₱{payment.Amount:0.##}).");
        if (request.ReferenceNumber is { Length: > 100 } || request.OrNumber is { Length: > 100 } || request.ConfirmNotes is { Length: > 1000 })
            return PaymentActionResult.Fail("Reference number, OR number or notes are too long.");

        var now = DateTimeOffset.UtcNow;
        payment.Status = PaymentStatus.Paid;
        payment.PaymentMethod = request.PaymentMethod;
        payment.ReferenceNumber = BlankToNull(request.ReferenceNumber);
        payment.OrNumber = BlankToNull(request.OrNumber);
        payment.AmountReceived = request.AmountReceived ?? payment.Amount;
        payment.ConfirmNotes = BlankToNull(request.ConfirmNotes);
        payment.ConfirmedByUserId = _auth.Current?.UserId;
        payment.ConfirmedAt = now;
        payment.UpdatedAt = now;
        payment.Booking.AmountDue = 0;
        payment.Booking.UpdatedAt = now;

        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "payment",
            EntityId = payment.PaymentId,
            OperationType = "confirm",
            PayloadJson = $$"""{"paymentId":"{{payment.PaymentId}}","bookingId":"{{payment.BookingId}}"}""",
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync.RequestSync();
        //JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.PaymentUpdated, payment.BookingId, payment.Booking.DoctorId));
        return PaymentActionResult.Ok(payment.PaymentId);
    }

    public async Task<PaymentActionResult> ConfirmWaiverAsync(Guid paymentId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var payment = await db.Payments.Include(p => p.Booking).SingleOrDefaultAsync(p => p.PaymentId == paymentId, ct);
        if (payment is null) return PaymentActionResult.Fail("Payment not found.");
        if (payment.Status != PaymentStatus.Waived)
            return PaymentActionResult.Fail("Only a waived professional fee can be confirmed this way.");
        if (payment.ConfirmedAt is not null)
            return PaymentActionResult.Ok(payment.PaymentId); // idempotent

        var now = DateTimeOffset.UtcNow;
        payment.ConfirmedByUserId = _auth.Current?.UserId;
        payment.ConfirmedAt = now;
        payment.UpdatedAt = now;
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "payment",
            EntityId = payment.PaymentId,
            OperationType = "confirm-waiver",
            PayloadJson = $$"""{"paymentId":"{{payment.PaymentId}}","bookingId":"{{payment.BookingId}}"}""",
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync.RequestSync();
        //JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.PaymentUpdated, payment.BookingId, payment.Booking.DoctorId));
        return PaymentActionResult.Ok(payment.PaymentId);
    }

    static PaymentListItem ToItem(ClinicApp.Domain.Entities.Payment p) => new(
        p.PaymentId,
        p.BookingId,
        p.Booking.Patient is null ? "" : $"{p.Booking.Patient.FirstName} {p.Booking.Patient.LastName}".Trim(),
        p.Booking.Patient?.PatientCode ?? "",
        p.Booking.QueueNumber,
        p.Booking.AppointmentDate,
        p.Booking.Status,
        p.Status,
        p.Amount,
        p.WaivedReason,
        p.ConfirmedAt,
        p.Status == PaymentStatus.Waived && p.ConfirmedAt is null);

    static string? BlankToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
// JJQR - END
