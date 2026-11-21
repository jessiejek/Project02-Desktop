// JJQR - AI (Nov 21, 2026) - ported from BookingsController (list, detail, create, status, cancel, reschedule, closed dates).
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Events;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core.Sync;
// JJQR - END

namespace ClinicNode.Core.Admin;

public sealed record AdminBookingListItem(
    Guid BookingId,
    Guid PatientId,
    string PatientName,
    string PatientCode,
    Guid DoctorId,
    string DoctorName,
    DateOnly AppointmentDate,
    TimeOnly SlotStart,
    TimeOnly SlotEnd,
    BookingStatus Status,
    PaymentStatus? PaymentStatus,
    string? QueueNumber,
    bool IsWalkIn);

public sealed record AdminBookingDetail(
    AdminBookingListItem Booking,
    string? PatientContact,
    string? PatientEmail,
    string DoctorSpecialty,
    PaymentMode PaymentMode,
    decimal AmountDue,
    decimal TotalFee,
    string? OrNumber,
    string? CancellationReason,
    string? Notes,
    VisitType VisitType,
    string? DiscountCategory,
    IReadOnlyList<string> Services,
    Guid? PaymentId = null);

public sealed record CreateAdminBookingRequest(
    Guid PatientId,
    Guid DoctorId,
    DateOnly AppointmentDate,
    TimeOnly SlotStartTime,
    TimeOnly SlotEndTime,
    IReadOnlyList<Guid> ServiceIds,
    PaymentMode PaymentMode,
    bool IsWalkIn,
    string? Notes,
    string? DiscountCategory = null,
    VisitType VisitType = VisitType.New);

public sealed record RescheduleResult(Guid OldBookingId, Guid NewBookingId, BookingStatus OldStatus, BookingStatus NewBookingStatus);

public sealed record ClosedDateInfo(DateOnly Date, string Reason);

public interface IAdminBookingService
{
    Task<AdminPage<AdminBookingListItem>> ListAsync(string? q, Guid? doctorId, BookingStatus? status, DateOnly? date, DateOnly? from, DateOnly? to, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<IReadOnlyList<AdminBookingListItem>> ListWeekAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task<AdminResult<AdminBookingDetail>> GetAsync(Guid bookingId, CancellationToken ct = default);
    Task<AdminResult<AdminBookingListItem>> CreateAsync(CreateAdminBookingRequest request, CancellationToken ct = default);
    Task<AdminResult<AdminBookingListItem>> CreateWalkInAsync(CreateAdminBookingRequest request, CancellationToken ct = default);
    Task<AdminResult> UpdateStatusAsync(Guid bookingId, BookingStatus status, string? reason, CancellationToken ct = default);
    // JJQR - AI (Nov 21, 2026) - admin payment actions matching PaymentsController + /admin/bookings/[id].
    Task<AdminResult> ConfirmPaymentAsync(Guid bookingId, CancellationToken ct = default);
    Task<AdminResult> WaivePaymentAsync(Guid bookingId, string reason, CancellationToken ct = default);
    Task<AdminResult> RefundPaymentAsync(Guid bookingId, decimal amount, string reason, CancellationToken ct = default);
    // JJQR - END
    Task<AdminResult<RescheduleResult>> RescheduleAsync(Guid bookingId, DateOnly appointmentDate, string? reason, CancellationToken ct = default);
    Task<string?> ClosedReasonAsync(DateOnly date, Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<ClosedDateInfo>> ClosedDatesAsync(Guid? doctorId, int days = 90, CancellationToken ct = default);
}

public sealed class AdminBookingService : IAdminBookingService
{
    private static readonly BookingStatus[] ActiveStatuses =
        [BookingStatus.Pending, BookingStatus.ProofSubmitted, BookingStatus.Confirmed, BookingStatus.CheckedIn, BookingStatus.InProgress, BookingStatus.OnHold];

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;
    private readonly IClinicEventBus _events;

    // JJQR - AI (Nov 21, 2026)
    private readonly ISyncBridge? _sync;
    // JJQR - END
    private readonly ClinicNode.Core.Queue.IQueueNumberSource? _numbers; //JJQR - AI (21 Nov 2026) - TASK C ADMIN: reserved queue blocks (null = QueueSequencer) //JJQR - END

    public AdminBookingService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth, IClinicEventBus events, ISyncBridge? sync = null,
        ClinicNode.Core.Queue.IQueueNumberSource? numbers = null) //JJQR - AI (21 Nov 2026) - TASK C ADMIN: optional, filled by the Sync DI registration //JJQR - END
    {
        _numbers = numbers; //JJQR - AI (21 Nov 2026) //JJQR - END
        // JJQR - AI (Nov 21, 2026)
        _sync = sync;
        // JJQR - END
        _factory = factory;
        _outbox = outbox;
        _auth = auth;
        _events = events;
    }

    //JJQR - AI (21 Nov 2026) - TASK C ADMIN: QueueSequencer only when no IQueueNumberSource is registered (tests / no sync)
    private async Task<string?> NextQueueNumberAsync(ClinicAppDbContext db, DateOnly day, Guid patientId, string? discount, CancellationToken ct) =>
        _numbers is null
            ? await QueueSequencer.NextForPatientAsync(db, day, patientId, discount, ct)
            : await _numbers.NextForPatientAsync(db, day, patientId, discount, ct);
    //JJQR - END

    public async Task<AdminPage<AdminBookingListItem>> ListAsync(string? q, Guid? doctorId, BookingStatus? status, DateOnly? date, DateOnly? from, DateOnly? to, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = WithEmbeds(db);
        if (doctorId is not null) query = query.Where(b => b.DoctorId == doctorId);
        if (status is not null) query = query.Where(b => b.Status == status);
        if (date is not null) query = query.Where(b => b.AppointmentDate == date);
        if (from is not null) query = query.Where(b => b.AppointmentDate >= from);
        if (to is not null) query = query.Where(b => b.AppointmentDate <= to);
        query = Search(query, q);
        var ordered = query.OrderByDescending(b => b.AppointmentDate).ThenBy(b => b.SlotStartTime);
        var total = await ordered.CountAsync(ct);
        var rows = await ordered.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new AdminPage<AdminBookingListItem>(rows.Select(MapList).ToList(), total, page, pageSize);
    }

    public async Task<IReadOnlyList<AdminBookingListItem>> ListWeekAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await WithEmbeds(db)
            .Where(b => b.AppointmentDate >= from && b.AppointmentDate <= to
                        && b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired)
            .OrderBy(b => b.AppointmentDate).ThenBy(b => b.SlotStartTime)
            .ToListAsync(ct);
        return rows.Select(MapList).ToList();
    }

    public async Task<AdminResult<AdminBookingDetail>> GetAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await WithEmbeds(db).SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b is null) return AdminResult<AdminBookingDetail>.Fail("Booking not found.");
        var item = MapList(b);
        var detail = new AdminBookingDetail(
            item,
            b.Patient.ContactNumber,
            string.IsNullOrWhiteSpace(b.Patient.Email) ? null : b.Patient.Email,
            b.Doctor?.Specialization ?? "",
            b.PaymentMode,
            b.AmountDue,
            b.TotalFee,
            b.Payment?.OrNumber,
            b.CancellationReason,
            b.Notes,
            b.VisitType,
            b.DiscountCategory,
            b.BookingServices.Select(s => s.Service?.Name ?? "").Where(n => n.Length > 0).ToList(),
            b.Payment?.PaymentId);
        return AdminResult<AdminBookingDetail>.Ok(detail);
    }

    public Task<AdminResult<AdminBookingListItem>> CreateWalkInAsync(CreateAdminBookingRequest request, CancellationToken ct = default) =>
        CreateAsync(request with { IsWalkIn = true }, ct);

    public async Task<AdminResult<AdminBookingListItem>> CreateAsync(CreateAdminBookingRequest request, CancellationToken ct = default)
    {
        if (_auth.Current is null) return AdminResult<AdminBookingListItem>.Fail("Not signed in.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.DoctorId == request.DoctorId, ct);
        if (doctor is null) return AdminResult<AdminBookingListItem>.Fail("Doctor not found.");
        var serviceIds = request.ServiceIds ?? [];
        var services = await db.Services.Where(s => serviceIds.Contains(s.ServiceId)).ToListAsync(ct);
        if (services.Count != serviceIds.Count) return AdminResult<AdminBookingListItem>.Fail("One or more services not found.");

        if (!request.IsWalkIn)
        {
            var closed = await ClosedReasonCoreAsync(db, request.AppointmentDate, doctor.DoctorId, ct);
            if (closed is not null) return AdminResult<AdminBookingListItem>.Fail(closed);
            if (await HasActiveBookingOnAsync(db, request.PatientId, request.AppointmentDate, null, ct))
                return AdminResult<AdminBookingListItem>.Fail("This patient already has a booking for that date.");
        }

        //JJQR - AI (21 Nov 2026) - TASK C ADMIN: number from the reserved block before anything is written
        var queueNumber = await NextQueueNumberAsync(db, request.AppointmentDate, request.PatientId, request.DiscountCategory, ct);
        if (queueNumber is null) return AdminResult<AdminBookingListItem>.Fail(ClinicNode.Core.Queue.QueueNumberMessages.NoNumbersLeft);
        //JJQR - END
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var totalFee = doctor.ConsultationFee + services.Sum(s => s.Price);
        var booking = new Booking
        {
            BookingId = Guid.NewGuid(),
            PatientId = request.PatientId,
            DoctorId = request.DoctorId,
            AppointmentDate = request.AppointmentDate,
            SlotStartTime = request.SlotStartTime,
            SlotEndTime = request.SlotEndTime,
            Status = request.IsWalkIn ? BookingStatus.CheckedIn : BookingStatus.Pending,
            PaymentMode = request.PaymentMode,
            QueueNumber = queueNumber, //JJQR - AI (21 Nov 2026) - TASK C ADMIN //JJQR - END
            ConsultationFeeSnapshot = doctor.ConsultationFee,
            TotalFee = totalFee,
            AmountDue = totalFee,
            IsWalkIn = request.IsWalkIn,
            VisitType = request.VisitType,
            DiscountCategory = request.DiscountCategory,
            Notes = request.Notes,
            CreatedByUserId = _auth.Current.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Bookings.Add(booking);
        foreach (var service in services)
            db.BookingServices.Add(new BookingService { BookingId = booking.BookingId, ServiceId = service.ServiceId, PriceAtBooking = service.Price });
        db.Payments.Add(new Payment
        {
            PaymentId = Guid.NewGuid(),
            BookingId = booking.BookingId,
            Amount = totalFee,
            Status = PaymentStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now,
        });
        AdminAudit.Add(db, AuditEntityType.Booking, booking.BookingId,
            request.IsWalkIn ? "Booked (walk-in)" : "Booked",
            booking.CreatedByUserId,
            $"Doctor: {doctor.DoctorId}; Date: {booking.AppointmentDate:yyyy-MM-dd} {booking.SlotStartTime}; Fee: {totalFee:0.00}");
        await _outbox.EnqueueAsync(db, Outbox("booking", booking.BookingId, "create", now), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.QueueUpdated, BookingId: booking.BookingId, DoctorId: booking.DoctorId));

        var created = await WithEmbeds(db).SingleAsync(b => b.BookingId == booking.BookingId, ct);
        return AdminResult<AdminBookingListItem>.Ok(MapList(created));
    }

    public async Task<AdminResult> UpdateStatusAsync(Guid bookingId, BookingStatus status, string? reason, CancellationToken ct = default)
    {
        if (_auth.Current is null) return AdminResult.Fail("Not signed in.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is null) return AdminResult.Fail("Booking not found.");

        var oldStatus = booking.Status;
        var illegal = BookingStatusMachine.RejectReason(oldStatus, status);
        if (illegal is not null) return AdminResult.Fail(illegal);

        if (status == BookingStatus.Cancelled
            && await db.Payments.AsNoTracking().AnyAsync(p => p.BookingId == bookingId && p.Status == PaymentStatus.Paid, ct))
            return AdminResult.Fail("This booking has already been paid. Refund it before cancelling.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        booking.Status = status;
        if (status == BookingStatus.Cancelled)
        {
            booking.CancellationReason = reason;
            booking.CancelledByUserId = _auth.Current.UserId;
        }
        booking.UpdatedAt = DateTimeOffset.UtcNow;
        var details = status == BookingStatus.Cancelled && !string.IsNullOrWhiteSpace(reason)
            ? $"Status: {oldStatus} → {status}; Reason: {reason}"
            : $"Status: {oldStatus} → {status}";
        AdminAudit.Add(db, AuditEntityType.Booking, booking.BookingId, status.ToString(), _auth.Current.UserId, details);
        await _outbox.EnqueueAsync(db, Outbox("booking", booking.BookingId, "update", booking.UpdatedAt), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.QueueUpdated, BookingId: booking.BookingId, DoctorId: booking.DoctorId));
        return AdminResult.Ok();
    }

    public async Task<AdminResult<RescheduleResult>> RescheduleAsync(Guid bookingId, DateOnly appointmentDate, string? reason, CancellationToken ct = default)
    {
        if (_auth.Current is null) return AdminResult<RescheduleResult>.Fail("Not signed in.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.Include(b => b.BookingServices).SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is null) return AdminResult<RescheduleResult>.Fail("Booking not found.");

        var oldStatus = booking.Status;
        var illegal = BookingStatusMachine.RejectReason(oldStatus, BookingStatus.Rescheduled);
        if (illegal is not null) return AdminResult<RescheduleResult>.Fail(illegal);
        if (appointmentDate < ClinicClock.Today)
            return AdminResult<RescheduleResult>.Fail("Cannot reschedule to a past date.");
        var closedOn = await ClosedReasonCoreAsync(db, appointmentDate, booking.DoctorId, ct);
        if (closedOn is not null) return AdminResult<RescheduleResult>.Fail(closedOn);
        if (await HasActiveBookingOnAsync(db, booking.PatientId, appointmentDate, booking.BookingId, ct))
            return AdminResult<RescheduleResult>.Fail("This patient already has a booking for that date.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var userId = _auth.Current.UserId;
        var now = DateTimeOffset.UtcNow;
        booking.Status = BookingStatus.Rescheduled;
        booking.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var note = $"Rescheduled to {appointmentDate:yyyy-MM-dd}: {reason.Trim()}";
            booking.Notes = string.IsNullOrWhiteSpace(booking.Notes) ? note : $"{booking.Notes}; {note}";
        }

        var newStatus = oldStatus switch
        {
            BookingStatus.Pending => BookingStatus.Pending,
            BookingStatus.ProofSubmitted => BookingStatus.ProofSubmitted,
            _ => BookingStatus.Confirmed,
        };
        //JJQR - AI (21 Nov 2026) - TASK C ADMIN: a null number saves nothing (no SaveChanges / commit has run for this reschedule yet)
        var newQueueNumber = await NextQueueNumberAsync(db, appointmentDate, booking.PatientId, booking.DiscountCategory, ct);
        if (newQueueNumber is null) return AdminResult<RescheduleResult>.Fail(ClinicNode.Core.Queue.QueueNumberMessages.NoNumbersLeft);
        //JJQR - END
        var newId = Guid.NewGuid();
        var newBooking = new Booking
        {
            BookingId = newId,
            PatientId = booking.PatientId,
            DoctorId = booking.DoctorId,
            AppointmentDate = appointmentDate,
            SlotStartTime = booking.SlotStartTime,
            SlotEndTime = booking.SlotEndTime,
            Status = newStatus,
            PaymentMode = booking.PaymentMode,
            QueueNumber = newQueueNumber, //JJQR - AI (21 Nov 2026) - TASK C ADMIN //JJQR - END
            ConsultationFeeSnapshot = booking.ConsultationFeeSnapshot,
            TotalFee = booking.TotalFee,
            AmountDue = booking.AmountDue,
            IsWalkIn = booking.IsWalkIn,
            VisitType = booking.VisitType,
            DiscountCategory = booking.DiscountCategory,
            DiscountAmount = booking.DiscountAmount,
            MedCertRequested = booking.MedCertRequested,
            Notes = null,
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Bookings.Add(newBooking);
        foreach (var line in booking.BookingServices)
        {
            db.BookingServices.Add(new BookingService
            {
                BookingId = newId,
                ServiceId = line.ServiceId,
                PriceAtBooking = line.PriceAtBooking,
            });
        }
        db.Payments.Add(new Payment
        {
            PaymentId = Guid.NewGuid(),
            BookingId = newId,
            Amount = booking.TotalFee,
            Status = PaymentStatus.Unpaid,
            CreatedAt = now,
            UpdatedAt = now,
        });
        AdminAudit.Add(db, AuditEntityType.Booking, booking.BookingId, "Rescheduled", userId,
            $"→ {appointmentDate:yyyy-MM-dd}; new booking {newId}");
        AdminAudit.Add(db, AuditEntityType.Booking, newId, "Booked (reschedule)", userId,
            $"From {booking.BookingId}; Queue: {newBooking.QueueNumber}");
        await _outbox.EnqueueAsync(db, Outbox("booking", booking.BookingId, "update", now), ct);
        await _outbox.EnqueueAsync(db, Outbox("booking", newId, "create", now), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.QueueUpdated, BookingId: booking.BookingId, DoctorId: booking.DoctorId));
        _events.Publish(new ClinicEvent(ClinicEventKind.QueueUpdated, BookingId: newId, DoctorId: newBooking.DoctorId));
        return AdminResult<RescheduleResult>.Ok(new RescheduleResult(booking.BookingId, newId, BookingStatus.Rescheduled, newStatus));
    }

    public async Task<string?> ClosedReasonAsync(DateOnly date, Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await ClosedReasonCoreAsync(db, date, doctorId, ct);
    }

    public async Task<IReadOnlyList<ClosedDateInfo>> ClosedDatesAsync(Guid? doctorId, int days = 90, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 366);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var doctor = doctorId is { } id
            ? await db.Doctors.AsNoTracking().SingleOrDefaultAsync(d => d.DoctorId == id, ct)
            : await db.Doctors.AsNoTracking().OrderBy(d => d.DoctorId).FirstOrDefaultAsync(ct);
        if (doctor is null) return [];
        var result = new List<ClosedDateInfo>();
        var start = ClinicClock.Today;
        for (var i = 0; i < days; i++)
        {
            var date = start.AddDays(i);
            var reason = await ClosedReasonCoreAsync(db, date, doctor.DoctorId, ct);
            if (reason is not null) result.Add(new ClosedDateInfo(date, reason));
        }
        return result;
    }


    // JJQR - AI (Nov 21, 2026) - Confirm payment / waive / refund as on /admin/bookings/[id] and PaymentsController.
    public async Task<AdminResult> ConfirmPaymentAsync(Guid bookingId, CancellationToken ct = default)
    {
        if (_auth.Current is null) return AdminResult.Fail("Not signed in.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.Include(b => b.Payment).SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is null) return AdminResult.Fail("Booking not found.");
        if (booking.Status != BookingStatus.ProofSubmitted)
            return AdminResult.Fail("Only a booking with payment proof submitted can be confirmed as paid here.");
        var payment = booking.Payment;
        if (payment is null) return AdminResult.Fail("Payment not found.");
        if (payment.Status == PaymentStatus.Waived)
            return AdminResult.Fail("The doctor waived this professional fee, so there is nothing to collect.");
        if (payment.Status == PaymentStatus.Paid)
            return AdminResult.Fail("This payment has already been confirmed.");
        var illegalPay = PaymentStatusMachine.RejectReason(payment.Status, PaymentStatus.Paid);
        if (illegalPay is not null) return AdminResult.Fail(illegalPay);
        var illegalBook = BookingStatusMachine.RejectReason(booking.Status, BookingStatus.Confirmed);
        if (illegalBook is not null) return AdminResult.Fail(illegalBook);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        booking.Status = BookingStatus.Confirmed;
        booking.UpdatedAt = now;
        payment.Status = PaymentStatus.Paid;
        payment.PaymentMethod ??= PaymentMethod.Cash;
        // JJQR - AI (Nov 21, 2026) - unique sequential OR (not a colliding booking-id prefix); safe inside this transaction
        payment.OrNumber = await StaffPaymentsService.AllocateOrNumberAsync(db, payment, ct);
        // JJQR - END
        payment.AmountReceived = payment.AmountReceived ?? payment.Amount;
        payment.ConfirmedByUserId = _auth.Current.UserId;
        payment.ConfirmedAt = now;
        payment.UpdatedAt = now;
        booking.AmountDue = 0;
        AdminAudit.Add(db, AuditEntityType.Payment, payment.PaymentId, "Confirmed payment", _auth.Current.UserId,
            $"OR {payment.OrNumber}; booking confirmed.");
        await _outbox.EnqueueAsync(db, Outbox("payment", payment.PaymentId, "confirm", now), ct);
        await _outbox.EnqueueAsync(db, Outbox("booking", booking.BookingId, "update", now), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _sync?.RequestSync();
        _events.Publish(new ClinicEvent(ClinicEventKind.PaymentUpdated, booking.BookingId, booking.DoctorId));
        _events.Publish(new ClinicEvent(ClinicEventKind.QueueUpdated, booking.BookingId, booking.DoctorId));
        return AdminResult.Ok();
    }

    public async Task<AdminResult> WaivePaymentAsync(Guid bookingId, string reason, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult.Fail("Only an administrator can waive a payment from this screen.");
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0) return AdminResult.Fail("A waiver reason is required.");
        if (trimmed.Length > 1000) return AdminResult.Fail("Waiver reason is too long (max 1000 characters).");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var payment = await db.Payments.Include(p => p.Booking).SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
        if (payment is null) return AdminResult.Fail("Payment not found.");
        if (payment.Status == PaymentStatus.Waived)
            return AdminResult.Fail("This payment has already been waived.");
        var illegal = PaymentStatusMachine.RejectReason(payment.Status, PaymentStatus.Waived);
        if (illegal is not null) return AdminResult.Fail(illegal);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        payment.Status = PaymentStatus.Waived;
        payment.WaivedReason = trimmed;
        payment.WaivedByUserId = _auth.Current.UserId;
        payment.WaivedAt = now;
        payment.ConfirmedByUserId = _auth.Current.UserId;
        payment.ConfirmedAt = now;
        //JJQR - AI (21 Nov 2026) - the admin waiver also gets this computer's next OR number (OR-XXXX-YYYYMMDD-NNNN), like /admin/bookings/[id] shows one after a waive;
        // allocated inside this transaction, so it cannot collide with a confirmation running at the same time.
        payment.OrNumber = await StaffPaymentsService.AllocateOrNumberAsync(db, payment, ct);
        //JJQR - END
        payment.UpdatedAt = now;
        payment.Booking.AmountDue = 0;
        payment.Booking.UpdatedAt = now;
        AdminAudit.Add(db, AuditEntityType.Payment, payment.PaymentId, "Waived", _auth.Current.UserId, /*JJQR - AI (21 Nov 2026)*/ $"OR {payment.OrNumber}; {trimmed}" /*JJQR - END*/);
        await _outbox.EnqueueAsync(db, Outbox("payment", payment.PaymentId, "waive", now), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _sync?.RequestSync();
        _events.Publish(new ClinicEvent(ClinicEventKind.PaymentUpdated, payment.BookingId, payment.Booking.DoctorId));
        return AdminResult.Ok();
    }

    public async Task<AdminResult> RefundPaymentAsync(Guid bookingId, decimal amount, string reason, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult.Fail("Only an administrator can refund a payment.");
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0) return AdminResult.Fail("A refund reason is required.");
        if (trimmed.Length > 1000) return AdminResult.Fail("Refund reason is too long (max 1000 characters).");
        if (amount <= 0) return AdminResult.Fail("Refund amount must be greater than zero.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var payment = await db.Payments.Include(p => p.Booking).SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
        if (payment is null) return AdminResult.Fail("Payment not found.");
        if (payment.Status == PaymentStatus.Refunded)
            return AdminResult.Fail("This payment has already been refunded.");
        var illegal = PaymentStatusMachine.RejectReason(payment.Status, PaymentStatus.Refunded);
        if (illegal is not null) return AdminResult.Fail(illegal);
        if (amount > payment.Amount)
            return AdminResult.Fail($"Refund amount cannot be more than the amount paid (₱{payment.Amount:0.##}).");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        payment.Status = PaymentStatus.Refunded;
        payment.RefundAmount = amount;
        payment.RefundReason = trimmed;
        payment.RefundedByUserId = _auth.Current.UserId;
        payment.RefundedAt = now;
        payment.UpdatedAt = now;
        payment.Booking.UpdatedAt = now;
        AdminAudit.Add(db, AuditEntityType.Payment, payment.PaymentId, "Refunded", _auth.Current.UserId,
            $"₱{amount:0.##}; {trimmed}");
        await _outbox.EnqueueAsync(db, Outbox("payment", payment.PaymentId, "refund", now), ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _sync?.RequestSync();
        _events.Publish(new ClinicEvent(ClinicEventKind.PaymentUpdated, payment.BookingId, payment.Booking.DoctorId));
        return AdminResult.Ok();
    }
    // JJQR - END

    static async Task<string?> ClosedReasonCoreAsync(ClinicAppDbContext db, DateOnly date, Guid doctorId, CancellationToken ct)
    {
        var dow = (short)date.DayOfWeek;
        var hours = await db.ClinicOperatingHours.AsNoTracking().SingleOrDefaultAsync(h => h.DayOfWeek == dow, ct);
        if (hours is { IsClosed: true })
            return $"The clinic is closed on {date.DayOfWeek}s. Please choose another day.";
        if (await db.DoctorBlockedDates.AsNoTracking().AnyAsync(b => b.DoctorId == doctorId && b.BlockedDate == date, ct))
            return "The doctor is not available on that date. Please choose another day.";
        var schedule = await db.DoctorSchedules.AsNoTracking().Where(x => x.DoctorId == doctorId).ToListAsync(ct);
        if (schedule.Any(x => x.IsActive))
        {
            var day = schedule.FirstOrDefault(x => x.DayOfWeek == dow);
            if (day is null || !day.IsActive)
                return $"The doctor does not see patients on {date.DayOfWeek}s. Please choose another day.";
        }
        return null;
    }

    static Task<bool> HasActiveBookingOnAsync(ClinicAppDbContext db, Guid patientId, DateOnly date, Guid? exceptBookingId, CancellationToken ct) =>
        db.Bookings.AsNoTracking().AnyAsync(b => b.PatientId == patientId && b.AppointmentDate == date
            && b.BookingId != exceptBookingId && ActiveStatuses.Contains(b.Status), ct);

    static IQueryable<Booking> WithEmbeds(ClinicAppDbContext db) =>
        db.Bookings.AsNoTracking()
            .Include(b => b.Patient)
            .Include(b => b.Doctor)!.ThenInclude(d => d.StaffAccount)
            .Include(b => b.BookingServices).ThenInclude(bs => bs.Service)
            .Include(b => b.Payment);

    static IQueryable<Booking> Search(IQueryable<Booking> query, string? q)
    {
        if (string.IsNullOrWhiteSpace(q)) return query;
        var s = q.Trim();
        if (Guid.TryParse(s, out var id))
            return query.Where(b => b.BookingId == id
                || (b.Patient.FirstName + " " + b.Patient.LastName).Contains(s)
                || b.Patient.PatientCode.Contains(s)
                || (b.QueueNumber != null && b.QueueNumber.Contains(s)));
        return query.Where(b =>
            (b.Patient.FirstName + " " + b.Patient.LastName).Contains(s) ||
            b.Patient.PatientCode.Contains(s) ||
            (b.QueueNumber != null && b.QueueNumber.Contains(s)));
    }

    static AdminBookingListItem MapList(Booking b) => new(
        b.BookingId,
        b.PatientId,
        $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
        b.Patient.PatientCode,
        b.DoctorId,
        b.Doctor?.StaffAccount?.FullName ?? "",
        b.AppointmentDate,
        b.SlotStartTime,
        b.SlotEndTime,
        b.Status,
        b.Payment?.Status,
        b.QueueNumber,
        b.IsWalkIn);

    static SyncOutboxEntry Outbox(string aggregate, Guid id, string op, DateTimeOffset now) => new()
    {
        OperationId = Guid.NewGuid(),
        Aggregate = aggregate,
        EntityId = id,
        OperationType = op,
        PayloadJson = $$"""{"id":"{{id}}"}""",
        CreatedAt = now,
    };
}
// JJQR - END
