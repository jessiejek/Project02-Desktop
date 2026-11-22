//JJQR - AI (Nov 21, 2026) - "Collect payments": today's completed visits that owe (queue board), earlier unpaid days (for-payment), doctor-waived visits awaiting "Confirm waiver"; exact-amount Record payment; desk waiver (only when the doctor already decided Waive); receipt.
using System.Data;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Events;
using ClinicNode.Core.Payments;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Staff;

public sealed record CollectRow(
    Guid BookingId,
    Guid PaymentId,
    string PatientName,
    string PatientCode,
    DateOnly AppointmentDate,
    string? QueueNumber,
    decimal AmountDue,
    string? WaivedReason)
{
    /// <summary>Web: a row with <c>waivedReason !== undefined</c> is a doctor-waived visit (₱0, "Confirm waiver").</summary>
    public bool IsWaived => WaivedReason is not null;
}

public sealed record CollectBoard(int WaitingToPay, decimal TotalDue, int WaivedToConfirm, IReadOnlyList<CollectRow> Rows);

public sealed record RecordPaymentInput(Guid BookingId, PaymentMethod Method, string? AmountReceivedText, string? ReferenceNumber, string? Notes,
    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 2.4 (D6): the booklet receipt number TYPED by the doctor. Null = the desk's automatic numbering (unchanged).
    string? OrNumber = null);
//JJQR - END

public sealed record ReceiptView(
    Guid BookingId, string PatientName, string DoctorName, DateOnly VisitDate, decimal Amount,
    PaymentStatus Status, PaymentMethod? Method, string? ReferenceNumber, string? OrNumber, string? WaivedReason,
    DateTimeOffset IssuedAt, bool PendingSync);

public interface IStaffPaymentsService
{
    Task<CollectBoard> GetCollectBoardAsync(CancellationToken ct = default);
    Task<StaffResult> RecordAsync(RecordPaymentInput input, CancellationToken ct = default);
    Task<StaffResult> ConfirmWaiverAsync(Guid paymentId, CancellationToken ct = default);
    Task<StaffResult> WaiveAsync(Guid bookingId, string? reason, CancellationToken ct = default);
    Task<ReceiptView?> GetReceiptAsync(Guid bookingId, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentMethod>> ListMethodsAsync(CancellationToken ct = default);
}

public sealed class StaffPaymentsService : IStaffPaymentsService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IPaymentService _payments;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;
    private readonly IClinicEventBus _events;
    //JJQR - AI (Nov 21, 2026) - optional sync hook: after a successful save, ask the background loop to push now.
    private readonly ISyncBridge? _sync;

    public StaffPaymentsService(IDbContextFactory<ClinicAppDbContext> factory, IPaymentService payments, ISyncOutbox outbox, IAuthService auth, IClinicEventBus events, ISyncBridge? sync = null)
    {
        _factory = factory;
        _payments = payments;
        _outbox = outbox;
        _auth = auth;
        _events = events;
        _sync = sync;
    }
    //JJQR - END

    public async Task<CollectBoard> GetCollectBoardAsync(CancellationToken ct = default)
    {
        var today = ClinicClock.Today;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var unpaid = await db.Bookings.AsNoTracking()
            .Include(b => b.Patient).Include(b => b.Payment)
            .Where(b => b.Status == BookingStatus.Completed && b.AmountDue > 0 && b.Payment != null && b.Payment.Status == PaymentStatus.Unpaid && b.AppointmentDate <= today)
            .ToListAsync(ct);

        // Today first (queue-board order: S- lane ahead of R-, then arrival), then earlier days by date.
        var todayRows = unpaid.Where(b => b.AppointmentDate == today && b.QueueNumber != null)
            .OrderBy(b => QueueLane.IsPriority(b.QueueNumber) ? 0 : 1).ThenBy(b => b.CreatedAt);
        var older = unpaid.Where(b => b.AppointmentDate < today).OrderBy(b => b.AppointmentDate);
        var rows = todayRows.Concat(older).Select(b => new CollectRow(
            b.BookingId, b.Payment!.PaymentId, Name(b.Patient), b.Patient?.PatientCode ?? "",
            b.AppointmentDate, b.QueueNumber, b.AmountDue, null)).ToList();

        var known = rows.Select(r => r.BookingId).ToHashSet();
        var waived = await _payments.ListWaivedAwaitingConfirmationAsync(ct);
        rows.AddRange(waived.Where(w => !known.Contains(w.BookingId)).Select(w => new CollectRow(
            w.BookingId, w.PaymentId, string.IsNullOrWhiteSpace(w.PatientName) ? "—" : w.PatientName.Trim(), w.PatientCode,
            w.AppointmentDate, w.QueueNumber, 0, w.WaivedReason ?? "")));

        var waiting = rows.Count(r => !r.IsWaived);
        return new CollectBoard(waiting, rows.Sum(r => r.AmountDue), rows.Count - waiting, rows);
    }

    public async Task<StaffResult> RecordAsync(RecordPaymentInput input, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.AsNoTracking().Include(b => b.Payment).SingleOrDefaultAsync(b => b.BookingId == input.BookingId, ct);
        if (booking?.Payment is null) return StaffResult.Fail("Payment not found.");
        var due = booking.Payment.Status == PaymentStatus.Unpaid ? booking.Payment.Amount : booking.AmountDue;
        var amount = StaffRules.CheckAmountReceived(input.AmountReceivedText, due);
        if (!amount.Ok) return StaffResult.Fail(amount.Error!);
        // Allocate the OR number in a serializable transaction first, then pass it into ConfirmAsync
        // (which saves on its own context). Reuses a number already reserved on this payment.
        string orNumber;
        //JJQR - AI (Nov 22, 2026) - step 2.4: a number the doctor typed from the booklet is used as it is (no automatic numbering), after one check that it is not already on another payment on this computer
        if (input.OrNumber is not null)
        {
            var typed = input.OrNumber.Trim();
            if (typed.Length == 0) return StaffResult.Fail("Type the receipt number from your booklet.");
            if (typed.Length > 100) return StaffResult.Fail("The receipt number must be 100 characters or fewer.");
            var lower = typed.ToLower();
            if (await db.Payments.AsNoTracking().AnyAsync(p => p.PaymentId != booking.Payment.PaymentId && p.OrNumber != null && p.OrNumber.ToLower() == lower, ct))
                return StaffResult.Fail($"Receipt number {typed} is already used on another payment on this computer. Check the booklet and type a different number.");
            orNumber = typed;
        }
        else
        //JJQR - END
        await using (var write = await _factory.CreateDbContextAsync(ct))
        {
            var payment = await write.Payments.SingleOrDefaultAsync(p => p.PaymentId == booking.Payment.PaymentId, ct);
            if (payment is null) return StaffResult.Fail("Payment not found.");
            try { orNumber = await AllocateOrNumberAsync(write, payment, ct); }
            catch (Exception ex) { return StaffResult.Fail("Could not assign an official receipt number. Please try again." + (ex.Message.Length > 0 ? " " + ex.Message : "")); }
        }
        var result = await _payments.ConfirmAsync(booking.Payment.PaymentId, new ConfirmPaymentRequest(
            input.Method,
            string.IsNullOrWhiteSpace(input.ReferenceNumber) ? null : input.ReferenceNumber.Trim(),
            orNumber,
            amount.Value,
            string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim()), ct);
        if (!result.Succeeded) return StaffResult.Fail(result.Error ?? "The payment was not recorded. Please try again.");
        //JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync(); // ConfirmAsync already saved
        //JJQR - END
        return StaffResult.Ok(booking.Payment.PaymentId);
    }

    public async Task<StaffResult> ConfirmWaiverAsync(Guid paymentId, CancellationToken ct = default)
    {
        var result = await _payments.ConfirmWaiverAsync(paymentId, ct);
        if (!result.Succeeded) return StaffResult.Fail(result.Error ?? "Could not confirm the waiver. Please try again.");
        //JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync(); // ConfirmWaiverAsync already saved
        //JJQR - END
        return StaffResult.Ok(paymentId);
    }

    /// <summary>Port of <c>POST /api/payments/{id}/waive</c> for staff: only when the doctor's consultation decided Waive; the desk's own waiver counts as confirmed.</summary>
    public async Task<StaffResult> WaiveAsync(Guid bookingId, string? reason, CancellationToken ct = default)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length < StaffRules.MinDialogReasonLength) return StaffResult.Fail("At least 5 characters.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var payment = await db.Payments.Include(p => p.Booking).SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
        if (payment is null) return StaffResult.Fail("Payment not found.");
        var session = _auth.Current;
        if (session is null || !session.IsAdmin)
        {
            var doctorDecided = await db.Consultations.AnyAsync(c => c.BookingId == bookingId && c.PfDecision == "Waive", ct);
            if (!doctorDecided) return StaffResult.Fail("Only the doctor can waive the professional fee.");
        }
        var illegal = PaymentStatusMachine.RejectReason(payment.Status, PaymentStatus.Waived);
        if (illegal is not null) return StaffResult.Fail(illegal);

        var now = DateTimeOffset.UtcNow;
        payment.Status = PaymentStatus.Waived;
        payment.WaivedReason = trimmed;
        payment.WaivedByUserId = session?.UserId;
        payment.WaivedAt = now;
        payment.ConfirmedByUserId = session?.UserId;
        payment.ConfirmedAt = now;
        payment.UpdatedAt = now;
        payment.Booking.AmountDue = 0;
        payment.Booking.UpdatedAt = now;
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "payment",
            EntityId = payment.PaymentId,
            OperationType = "waive",
            PayloadJson = $$"""{"paymentId":"{{payment.PaymentId}}","bookingId":"{{payment.BookingId}}","reason":{{System.Text.Json.JsonSerializer.Serialize(trimmed)}}}""",
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        //JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.PaymentUpdated, payment.BookingId, payment.Booking.DoctorId));
        return StaffResult.Ok(payment.PaymentId);
    }

    public async Task<ReceiptView?> GetReceiptAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await db.Bookings.AsNoTracking()
            .Include(x => x.Patient).Include(x => x.Payment)
            .Include(x => x.Doctor).ThenInclude(d => d.StaffAccount)
            .SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b?.Payment is null) return null;
        var p = b.Payment;
        var pending = (await _outbox.ListPendingAsync(db, 500, ct)).Any(e => e.EntityId == p.PaymentId);
        return new ReceiptView(b.BookingId, Name(b.Patient), b.Doctor?.StaffAccount?.FullName ?? "", b.AppointmentDate,
            p.AmountReceived ?? p.Amount, p.Status, p.PaymentMethod, p.ReferenceNumber,
            StaffRules.DisplayOrNumber(p.OrNumber, b.BookingId, p.Status is PaymentStatus.Paid or PaymentStatus.Waived),
            p.WaivedReason, p.ConfirmedAt ?? DateTimeOffset.UtcNow, pending);
    }

    public Task<IReadOnlyList<PaymentMethod>> ListMethodsAsync(CancellationToken ct = default) => _payments.ListMethodsAsync(ct);

    /// <summary>Next official receipt number for this node and the Manila date, persisted on <paramref name="payment"/>.
    /// Format <c>OR-&lt;nodePrefix&gt;-&lt;yyyymmdd&gt;-&lt;seq&gt;</c>. Opens a serializable (SQLite IMMEDIATE) transaction
    /// when the caller does not already have one, so two concurrent cashiers cannot receive the same sequence.
    /// Reuses a number already stored on this payment for that day. Call from admin confirm-payment as well.</summary>
    public static Task<string> AllocateOrNumberAsync(ClinicAppDbContext db, Payment payment, CancellationToken ct = default) =>
        AllocateOrNumberAsync(db, payment, ClinicClock.Today, ct);

    /// <inheritdoc cref="AllocateOrNumberAsync(ClinicAppDbContext, Payment, CancellationToken)"/>
    public static async Task<string> AllocateOrNumberAsync(ClinicAppDbContext db, Payment payment, DateOnly onDate, CancellationToken ct = default)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                return await AllocateOnceAsync(db, payment, onDate, ct);
            }
            catch (Exception ex) when (IsRetryableOrConflict(ex) && attempt < 7)
            {
                last = ex;
                await Task.Delay(20 * (attempt + 1), ct);
            }
        }
        throw last ?? new InvalidOperationException("Could not allocate an official receipt number.");
    }

    static async Task<string> AllocateOnceAsync(ClinicAppDbContext db, Payment payment, DateOnly onDate, CancellationToken ct)
    {
        var owns = db.Database.CurrentTransaction is null;
        await using var tx = owns ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        try
        {
            var identity = await SyncOutbox.GetOrCreateIdentityAsync(db, ct);
            var nodePrefix = StaffRules.NodePrefixFrom(identity.NodeId);
            var dayPrefix = StaffRules.OrDayPrefix(nodePrefix, onDate);
            if (StaffRules.SeqOf(payment.OrNumber, dayPrefix) is not null)
            {
                if (owns) await tx!.CommitAsync(ct);
                return payment.OrNumber!;
            }

            var existing = await db.Payments.AsNoTracking()
                .Where(p => p.OrNumber != null && p.OrNumber.StartsWith(dayPrefix))
                .Select(p => p.OrNumber!)
                .ToListAsync(ct);
            var next = 1;
            foreach (var n in existing)
                if (StaffRules.SeqOf(n, dayPrefix) is { } seq && seq >= next)
                    next = seq + 1;
            var or = StaffRules.FormatOrNumber(nodePrefix, onDate, next);
            payment.OrNumber = or;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            if (owns)
            {
                await db.SaveChangesAsync(ct);
                await tx!.CommitAsync(ct);
            }
            return or;
        }
        catch
        {
            if (owns && tx is not null) await tx.RollbackAsync(ct);
            throw;
        }
    }

    static bool IsRetryableOrConflict(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is Microsoft.Data.Sqlite.SqliteException s)
            {
                // 5/6 = busy/locked; 19 = constraint (UNIQUE once an or_number index exists).
                if (s.SqliteErrorCode is 5 or 6 or 19) return true;
                if (s.SqliteExtendedErrorCode is 2067 or 1555) return true;
            }
        }
        return false;
    }

    static string Name(Patient? p) => p is null ? "—" : $"{p.FirstName} {p.LastName}".Trim() is { Length: > 0 } n ? n : "—";
}
//JJQR - END
