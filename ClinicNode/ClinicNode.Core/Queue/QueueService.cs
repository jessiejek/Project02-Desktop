// JJQR - AI (Nov 21, 2026) - staff/doctor queue: R-/S- lanes, check-in, Cancel, walk-in with node-issued numbers.
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Events;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Queue;

public interface IQueueService
{
    Task<QueueBoard> GetBoardAsync(DateOnly? date = null, CancellationToken ct = default);
    Task<IReadOnlyList<QueueEntry>> ListDoctorTodayAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<QueueEntry>> ListStaffTodayAsync(bool includeCancelled = true, CancellationToken ct = default);
    Task<IReadOnlyList<DoctorListItem>> ListDoctorsAsync(CancellationToken ct = default);
    Task<QueueActionResult> CheckInWalkInAsync(WalkInRequest request, CancellationToken ct = default);
    Task<QueueActionResult> CheckInBookedAsync(Guid bookingId, CancellationToken ct = default);
    Task<QueueActionResult> UndoCheckInAsync(Guid bookingId, CancellationToken ct = default);
    Task<QueueActionResult> CallAsync(Guid bookingId, CancellationToken ct = default);
    Task<QueueActionResult> HoldAsync(Guid bookingId, CancellationToken ct = default);
    Task<QueueActionResult> CompleteAsync(Guid bookingId, CancellationToken ct = default);
    Task<QueueActionResult> NoShowAsync(Guid bookingId, CancellationToken ct = default);
    Task<QueueActionResult> CancelAsync(Guid bookingId, string reason, CancellationToken ct = default);
    Task<WalkInTicket?> GetTicketAsync(Guid bookingId, CancellationToken ct = default);
}

public sealed class QueueService : IQueueService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;
    private readonly IClinicEventBus _events;
    //JJQR - AI (Nov 21, 2026)
    private readonly ClinicNode.Core.Sync.ISyncBridge _sync;
    //JJQR - END
    private readonly IQueueNumberSource? _numbers; //JJQR - AI (21 Nov 2026) - TASK C: reserved queue blocks (null = no sync registered → QueueSequencer) //JJQR - END

    public QueueService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth, IClinicEventBus events, ClinicNode.Core.Sync.ISyncBridge? sync = null,
        IQueueNumberSource? numbers = null) //JJQR - AI (21 Nov 2026) - TASK C: optional, filled by the Sync DI registration //JJQR - END
    {
        _numbers = numbers; //JJQR - AI (21 Nov 2026) //JJQR - END
        //JJQR - AI (Nov 21, 2026)
        _sync = sync ?? new ClinicNode.Core.Sync.NoOpSyncBridge();
        //JJQR - END
        _factory = factory;
        _outbox = outbox;
        _auth = auth;
        _events = events;
    }

    public async Task<QueueBoard> GetBoardAsync(DateOnly? date = null, CancellationToken ct = default)
    {
        var day = date ?? ClinicClock.Today;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await QueryDay(db, day)
            .Where(b => b.QueueNumber != null && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired)
            .ToListAsync(ct);
        var items = SortLane(rows).Select(ToEntry).ToList();
        return new QueueBoard(
            day,
            rows.Count(b => b.Status is BookingStatus.Pending or BookingStatus.Confirmed),
            rows.Count(b => b.Status is BookingStatus.CheckedIn or BookingStatus.OnHold),
            rows.Count(b => b.Status == BookingStatus.InProgress),
            rows.Count(b => b.Status == BookingStatus.Completed),
            rows.Count(b => b.Status == BookingStatus.NoShow),
            rows.Count,
            rows.Count(b => b.Status == BookingStatus.Completed && b.AmountDue > 0 && (b.Payment == null || b.Payment.Status == PaymentStatus.Unpaid)),
            items);
    }

    public async Task<IReadOnlyList<QueueEntry>> ListDoctorTodayAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await QueryDay(db, ClinicClock.Today)
            .Where(b => b.DoctorId == doctorId && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired)
            .ToListAsync(ct);
        return SortLane(rows).Select(ToEntry).ToList();
    }

    public async Task<IReadOnlyList<QueueEntry>> ListStaffTodayAsync(bool includeCancelled = true, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = QueryDay(db, ClinicClock.Today);
        if (!includeCancelled) q = q.Where(b => b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired);
        var rows = await q.ToListAsync(ct);
        return SortLane(rows).Select(ToEntry).ToList();
    }

    public async Task<IReadOnlyList<DoctorListItem>> ListDoctorsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Doctors.AsNoTracking().Include(d => d.StaffAccount)
            .OrderBy(d => d.StaffAccount!.FullName)
            .Select(d => new DoctorListItem(d.DoctorId, d.StaffAccount!.FullName, d.Specialization))
            .ToListAsync(ct);
    }

    public async Task<QueueActionResult> CheckInWalkInAsync(WalkInRequest request, CancellationToken ct = default)
    {
        if (!QueueLane.IsValidDiscount(request.DiscountCategory))
            return QueueActionResult.Fail("Discount must be Senior or PWD.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.PatientId == request.PatientId, ct);
        if (patient is null) return QueueActionResult.Fail("Patient not found.");

        //JJQR - AI (21 Nov 2026) - ClinicNode.Core.Doctor namespace shadows the entity; do not use the bare name Doctor
        ClinicApp.Domain.Entities.Doctor? doctor;
        //JJQR - END
        if (request.DoctorId is { } doctorId)
            doctor = await db.Doctors.Include(d => d.StaffAccount).SingleOrDefaultAsync(d => d.DoctorId == doctorId, ct);
        else
            doctor = await db.Doctors.Include(d => d.StaffAccount).OrderBy(d => d.DoctorId).FirstOrDefaultAsync(ct);
        if (doctor is null) return QueueActionResult.Fail("No doctor configured.");

        var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
        if (settings is null) return QueueActionResult.Fail("Clinic settings missing.");

        var discount = QueueLane.NormalizeDiscount(request.DiscountCategory);
        var today = ClinicClock.Today;
        var arrival = ClinicClock.TimeNow;
        var now = DateTimeOffset.UtcNow;
        var fee = ClinicFees.Compute(settings, request.VisitType, request.MedCertRequested, discount);
        //JJQR - AI (Nov 22, 2026) - step 0.3: the caller's ids when given; the same booking id again for the same patient is a retry, so return that booking's ticket instead of a second booking
        var bookingId = request.BookingId is { } rb && rb != Guid.Empty ? rb : Guid.NewGuid();
        var paymentId = request.PaymentId is { } rp && rp != Guid.Empty ? rp : Guid.NewGuid();
        if (request.BookingId is { } retryId && retryId != Guid.Empty)
        {
            var existing = await db.Bookings.AsNoTracking().Include(b => b.Payment).SingleOrDefaultAsync(b => b.BookingId == retryId, ct);
            if (existing is not null)
            {
                if (existing.PatientId != patient.PatientId) return QueueActionResult.Fail("That booking id belongs to another patient.");
                var qn = existing.QueueNumber ?? "";
                return QueueActionResult.OkTicket(new WalkInTicket(
                    existing.BookingId, existing.Payment?.PaymentId ?? paymentId, qn,
                    QueueLane.IsPriority(qn) ? QueueLane.Priority : QueueLane.Regular, QueueLane.IsPriority(qn),
                    $"{patient.FirstName} {patient.LastName}".Trim(), patient.PatientCode, doctor.StaffAccount?.FullName ?? "",
                    settings.ClinicName, settings.Address, existing.VisitType, existing.TotalFee, existing.CreatedAt));
            }
        }
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - TASK C: numbers come only from this computer's reserved block; QueueSequencer only when no source is registered
        var queueNumber = _numbers is null
            ? await QueueSequencer.NextForPatientAsync(db, today, patient.PatientId, discount, ct)
            : await _numbers.NextForPatientAsync(db, today, patient.PatientId, discount, ct);
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 2.3 (owner Q1): no reserved number left = "Paper queue". The patient is added with NO number (staff keep the paper list, the doctor adds the patient when they
        // enter the room); the clinic server gives the booking a number when it syncs. The doctor is never blocked.
        // queueNumber stays null here; the ticket and the slip say "PAPER QUEUE".
        //JJQR - END
        //JJQR - END

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Bookings.Add(new Booking
        {
            BookingId = bookingId,
            PatientId = patient.PatientId,
            DoctorId = doctor.DoctorId,
            AppointmentDate = today,
            SlotStartTime = arrival,
            SlotEndTime = arrival,
            Status = BookingStatus.CheckedIn,
            PaymentMode = PaymentMode.PayAtClinic,
            QueueNumber = queueNumber,
            VisitType = request.VisitType,
            MedCertRequested = request.MedCertRequested,
            DiscountCategory = discount,
            DiscountAmount = fee.DiscountAmount,
            ConsultationFeeSnapshot = fee.Subtotal,
            TotalFee = fee.Total,
            AmountDue = fee.Total,
            IsWalkIn = true,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            CreatedByUserId = _auth.Current?.UserId,
            CreatedAt = now,
            UpdatedAt = now
        });
        db.Payments.Add(new Payment
        {
            PaymentId = paymentId,
            BookingId = bookingId,
            Amount = fee.Total,
            Status = PaymentStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now
        });
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "booking",
            EntityId = bookingId,
            OperationType = "create",
            PayloadJson = $$"""{"bookingId":"{{bookingId}}","queueNumber":"{{queueNumber}}"}""", //JJQR - AI (Nov 22, 2026) - step 2.3: "" when paper queue //JJQR - END
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync.RequestSync();
        //JJQR - END

        var ticket = new WalkInTicket(
            bookingId, paymentId, queueNumber ?? "",
            QueueLane.IsPriority(queueNumber) ? QueueLane.Priority : QueueLane.Regular,
            QueueLane.IsPriority(queueNumber),
            $"{patient.FirstName} {patient.LastName}".Trim(),
            patient.PatientCode,
            doctor.StaffAccount?.FullName ?? "",
            settings.ClinicName,
            settings.Address,
            request.VisitType,
            fee.Total,
            now);
        _events.Publish(new ClinicEvent(ClinicEventKind.PatientCheckedIn, bookingId, doctor.DoctorId, patient.PatientId));
        return QueueActionResult.OkTicket(ticket);
    }

    public Task<QueueActionResult> CallAsync(Guid bookingId, CancellationToken ct = default) =>
        SetStatusAsync(bookingId, BookingStatus.InProgress, ct);
    public Task<QueueActionResult> HoldAsync(Guid bookingId, CancellationToken ct = default) =>
        SetStatusAsync(bookingId, BookingStatus.OnHold, ct);
    public Task<QueueActionResult> CompleteAsync(Guid bookingId, CancellationToken ct = default) =>
        SetStatusAsync(bookingId, BookingStatus.Completed, ct);
    public Task<QueueActionResult> NoShowAsync(Guid bookingId, CancellationToken ct = default) =>
        SetStatusAsync(bookingId, BookingStatus.NoShow, ct);
    public Task<QueueActionResult> CheckInBookedAsync(Guid bookingId, CancellationToken ct = default) =>
        SetStatusAsync(bookingId, BookingStatus.CheckedIn, ct, from: [BookingStatus.Pending, BookingStatus.Confirmed]);
    public Task<QueueActionResult> UndoCheckInAsync(Guid bookingId, CancellationToken ct = default) =>
        SetStatusAsync(bookingId, BookingStatus.Confirmed, ct, from: [BookingStatus.CheckedIn]);

    public async Task<QueueActionResult> CancelAsync(Guid bookingId, string reason, CancellationToken ct = default)
    {
        var trimmed = reason?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(trimmed))
            return QueueActionResult.Fail("A cancellation reason is required.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.Include(b => b.Payment).SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is null) return QueueActionResult.Fail("Booking not found.");
        if (booking.Status is not (BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.CheckedIn or BookingStatus.OnHold))
            return QueueActionResult.Fail("Only a booking that hasn't been seen yet can be cancelled.");
        if (booking.Payment?.Status == PaymentStatus.Paid)
            return QueueActionResult.Fail("This booking has already been paid. Refund it before cancelling.");

        var now = DateTimeOffset.UtcNow;
        booking.Status = BookingStatus.Cancelled;
        booking.CancellationReason = trimmed;
        booking.CancelledByUserId = _auth.Current?.UserId;
        booking.UpdatedAt = now;
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "booking",
            EntityId = booking.BookingId,
            OperationType = "cancel",
            PayloadJson = $$"""{"bookingId":"{{booking.BookingId}}","reason":{{System.Text.Json.JsonSerializer.Serialize(trimmed)}}}""",
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync.RequestSync();
        //JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.QueueUpdated, booking.BookingId, booking.DoctorId, booking.PatientId));
        return QueueActionResult.Ok(booking.BookingId, booking.Status);
    }

    public async Task<WalkInTicket?> GetTicketAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await db.Bookings
            .Include(x => x.Patient)
            .Include(x => x.Doctor).ThenInclude(d => d.StaffAccount)
            .Include(x => x.Payment)
            .SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b is null) return null;
        var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct);
        return new WalkInTicket(
            b.BookingId,
            b.Payment?.PaymentId ?? Guid.Empty,
            b.QueueNumber ?? "",
            QueueLane.IsPriority(b.QueueNumber) ? QueueLane.Priority : QueueLane.Regular,
            QueueLane.IsPriority(b.QueueNumber),
            $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
            b.Patient.PatientCode,
            b.Doctor.StaffAccount?.FullName ?? "",
            settings?.ClinicName ?? "Clinic",
            settings?.Address,
            b.VisitType,
            b.AmountDue,
            b.CreatedAt);
    }

    async Task<QueueActionResult> SetStatusAsync(Guid bookingId, BookingStatus to, CancellationToken ct, BookingStatus[]? from = null)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is null) return QueueActionResult.Fail("Booking not found.");
        if (from is not null && !from.Contains(booking.Status))
            return QueueActionResult.Fail($"This booking cannot move from {booking.Status} to {to}.");
        var illegal = BookingStatusMachine.RejectReason(booking.Status, to);
        if (illegal is not null) return QueueActionResult.Fail(illegal);

        booking.Status = to;
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "booking",
            EntityId = booking.BookingId,
            OperationType = "status",
            PayloadJson = $$"""{"bookingId":"{{booking.BookingId}}","status":"{{to}}"}""",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
        await db.SaveChangesAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync.RequestSync();
        //JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.QueueUpdated, booking.BookingId, booking.DoctorId, booking.PatientId));
        return QueueActionResult.Ok(booking.BookingId, booking.Status);
    }

    static IQueryable<Booking> QueryDay(ClinicAppDbContext db, DateOnly day) =>
        db.Bookings.AsNoTracking()
            .Include(b => b.Patient)
            .Include(b => b.Payment)
            .Where(b => b.AppointmentDate == day);

    static IEnumerable<Booking> SortLane(IEnumerable<Booking> rows) =>
        rows.OrderBy(b => QueueLane.IsPriority(b.QueueNumber) ? 0 : 1)
            .ThenBy(b => b.CreatedAt);

    static QueueEntry ToEntry(Booking b) => new(
        b.BookingId,
        b.PatientId,
        b.DoctorId,
        b.Patient is null ? "" : $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
        b.Patient?.PatientCode ?? "",
        b.QueueNumber,
        QueueLane.IsPriority(b.QueueNumber),
        b.Status,
        b.Payment?.Status ?? PaymentStatus.Unpaid,
        b.VisitType,
        b.IsWalkIn,
        b.AmountDue,
        b.SlotStartTime,
        b.CreatedAt,
        b.DiscountCategory,
        b.CancellationReason);
}
// JJQR - END
