//JJQR - AI (Nov 21, 2026) - staff booking detail (/staff/bookings/[id]): Next step actions, patient / doctor / payment cards, check-in / undo, cancel (≥5 chars), no-show, reschedule (BookingsController.Reschedule rules).
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Events;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Staff;

public sealed record StaffBookingDetail(
    Guid BookingId,
    Guid? PaymentId,
    string PatientName,
    Guid PatientId,
    string PatientCode,
    SexType? PatientSex,
    DateOnly? PatientDob,
    string PatientContact,
    string PatientEmail,
    string DoctorName,
    string DoctorSpecialization,
    DateOnly AppointmentDate,
    TimeOnly SlotStart,
    string? QueueNumber,
    BookingStatus Status,
    PaymentStatus PaymentStatus,
    PaymentMode PaymentMode,
    decimal AmountDue,
    string? OrNumber,
    string? WaivedReason)
{
    public BookingDetailActions Actions => StaffRules.BookingActionsFor(Status, PaymentStatus);
}

public interface IStaffBookingService
{
    Task<StaffBookingDetail?> GetAsync(Guid bookingId, CancellationToken ct = default);
    Task<StaffResult> CheckInAsync(Guid bookingId, CancellationToken ct = default);
    Task<StaffResult> UndoCheckInAsync(Guid bookingId, CancellationToken ct = default);
    Task<StaffResult> CancelAsync(Guid bookingId, string? reason, CancellationToken ct = default);
    Task<StaffResult> MarkNoShowAsync(Guid bookingId, string? reason, CancellationToken ct = default);
    Task<StaffResult> RescheduleAsync(Guid bookingId, DateOnly newDate, string? reason, CancellationToken ct = default);
}

public sealed class StaffBookingService : IStaffBookingService
{
    static readonly BookingStatus[] ActiveStatuses =
        [BookingStatus.Pending, BookingStatus.ProofSubmitted, BookingStatus.Confirmed, BookingStatus.CheckedIn, BookingStatus.InProgress, BookingStatus.OnHold];

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IQueueService _queue;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;
    private readonly IClinicEventBus _events;
    //JJQR - AI (Nov 21, 2026) - optional sync hook: after a successful save, ask the background loop to push now.
    private readonly ISyncBridge? _sync;
    private readonly ClinicNode.Core.Queue.IQueueNumberSource? _numbers; //JJQR - AI (21 Nov 2026) - TASK C //JJQR - END

    public StaffBookingService(IDbContextFactory<ClinicAppDbContext> factory, IQueueService queue, ISyncOutbox outbox, IAuthService auth, IClinicEventBus events, ISyncBridge? sync = null,
        ClinicNode.Core.Queue.IQueueNumberSource? numbers = null) //JJQR - AI (21 Nov 2026) - TASK C: optional, filled by the Sync DI registration //JJQR - END
    {
        _numbers = numbers; //JJQR - AI (21 Nov 2026) //JJQR - END
        _factory = factory;
        _queue = queue;
        _outbox = outbox;
        _auth = auth;
        _events = events;
        _sync = sync;
    }
    //JJQR - END

    public async Task<StaffBookingDetail?> GetAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await db.Bookings.AsNoTracking()
            .Include(x => x.Patient).Include(x => x.Payment)
            .Include(x => x.Doctor).ThenInclude(d => d.StaffAccount)
            .SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b is null) return null;
        return new StaffBookingDetail(
            b.BookingId, b.Payment?.PaymentId,
            b.Patient is null ? "" : $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
            b.PatientId, b.Patient?.PatientCode ?? "", b.Patient?.Sex, b.Patient?.DateOfBirth,
            b.Patient?.ContactNumber ?? "", b.Patient?.Email ?? "",
            b.Doctor?.StaffAccount?.FullName ?? "", b.Doctor?.Specialization ?? "",
            b.AppointmentDate, b.SlotStartTime, b.QueueNumber, b.Status,
            b.Payment?.Status ?? PaymentStatus.Unpaid, b.PaymentMode, b.AmountDue,
            StaffRules.DisplayOrNumber(b.Payment?.OrNumber, b.BookingId, b.Payment is { Status: PaymentStatus.Paid or PaymentStatus.Waived }),
            b.Payment?.WaivedReason);
    }

    public async Task<StaffResult> CheckInAsync(Guid bookingId, CancellationToken ct = default) =>
        From(await _queue.CheckInBookedAsync(bookingId, ct), "Could not update the booking.");

    public async Task<StaffResult> UndoCheckInAsync(Guid bookingId, CancellationToken ct = default) =>
        From(await _queue.UndoCheckInAsync(bookingId, ct), "Could not update the booking.");

    public async Task<StaffResult> CancelAsync(Guid bookingId, string? reason, CancellationToken ct = default)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length < StaffRules.MinDialogReasonLength) return StaffResult.Fail("At least 5 characters.");
        return From(await _queue.CancelAsync(bookingId, trimmed, ct), "Could not cancel the booking.");
    }

    /// <summary>The web sends the optional reason, but the API only keeps a reason on Cancelled, so it is not stored.</summary>
    public async Task<StaffResult> MarkNoShowAsync(Guid bookingId, string? reason, CancellationToken ct = default) =>
        From(await _queue.NoShowAsync(bookingId, ct), "Could not update booking.");

    public async Task<StaffResult> RescheduleAsync(Guid bookingId, DateOnly newDate, string? reason, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.Include(b => b.BookingServices).SingleOrDefaultAsync(b => b.BookingId == bookingId, ct);
        if (booking is null) return StaffResult.Fail("Booking not found.");
        var oldStatus = booking.Status;
        if (BookingStatusMachine.RejectReason(oldStatus, BookingStatus.Rescheduled) is { } illegal) return StaffResult.Fail(illegal);
        if (newDate < ClinicClock.Today) return StaffResult.Fail("Cannot reschedule to a past date.");
        if (await ClosedReasonAsync(db, newDate, booking.DoctorId, ct) is { } closed) return StaffResult.Fail(closed);
        if (await db.Bookings.AsNoTracking().AnyAsync(b => b.PatientId == booking.PatientId && b.AppointmentDate == newDate
                && b.BookingId != booking.BookingId && ActiveStatuses.Contains(b.Status), ct))
            return StaffResult.Fail("This patient already has a booking for that date.");

        var userId = _auth.Current?.UserId;
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        booking.Status = BookingStatus.Rescheduled;
        booking.UpdatedAt = now;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var note = $"Rescheduled to {newDate:yyyy-MM-dd}: {reason.Trim()}";
            booking.Notes = string.IsNullOrWhiteSpace(booking.Notes) ? note : $"{booking.Notes}; {note}";
        }
        var newStatus = oldStatus switch
        {
            BookingStatus.Pending => BookingStatus.Pending,
            BookingStatus.ProofSubmitted => BookingStatus.ProofSubmitted,
            _ => BookingStatus.Confirmed,
        };
        var newId = Guid.NewGuid();
        //JJQR - AI (21 Nov 2026) - TASK C: reserved block for the new date; QueueSequencer only when no source is registered. A null number saves
        // nothing (the transaction is not committed and the tracked changes are never saved).
        var queueNumber = _numbers is null
            ? await QueueSequencer.NextForPatientAsync(db, newDate, booking.PatientId, booking.DiscountCategory, ct)
            : await _numbers.NextForPatientAsync(db, newDate, booking.PatientId, booking.DiscountCategory, ct);
        if (queueNumber is null) return StaffResult.Fail(ClinicNode.Core.Queue.QueueNumberMessages.NoNumbersLeft);
        //JJQR - END
        db.Bookings.Add(new Booking
        {
            BookingId = newId,
            PatientId = booking.PatientId,
            DoctorId = booking.DoctorId,
            AppointmentDate = newDate,
            SlotStartTime = booking.SlotStartTime,
            SlotEndTime = booking.SlotEndTime,
            Status = newStatus,
            PaymentMode = booking.PaymentMode,
            QueueNumber = queueNumber,
            ConsultationFeeSnapshot = booking.ConsultationFeeSnapshot,
            TotalFee = booking.TotalFee,
            AmountDue = booking.AmountDue,
            IsWalkIn = booking.IsWalkIn,
            VisitType = booking.VisitType,
            DiscountCategory = booking.DiscountCategory,
            DiscountAmount = booking.DiscountAmount,
            MedCertRequested = booking.MedCertRequested,
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        });
        foreach (var line in booking.BookingServices)
            db.BookingServices.Add(new BookingService { BookingId = newId, ServiceId = line.ServiceId, PriceAtBooking = line.PriceAtBooking });
        db.Payments.Add(new Payment { PaymentId = Guid.NewGuid(), BookingId = newId, Amount = booking.TotalFee, Status = PaymentStatus.Unpaid, CreatedAt = now, UpdatedAt = now });
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "booking",
            EntityId = booking.BookingId,
            OperationType = "reschedule",
            PayloadJson = $$"""{"bookingId":"{{booking.BookingId}}","newBookingId":"{{newId}}","date":"{{newDate:yyyy-MM-dd}}","queueNumber":"{{queueNumber}}"}""",
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        //JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.QueueUpdated, booking.BookingId, booking.DoctorId, booking.PatientId));
        return StaffResult.Ok(newId);
    }

    /// <summary>BookingsController.ClosedReasonAsync: clinic closed that weekday, doctor blocked the date, or doctor's weekly schedule has that day off.</summary>
    static async Task<string?> ClosedReasonAsync(ClinicAppDbContext db, DateOnly date, Guid doctorId, CancellationToken ct)
    {
        var dow = (short)date.DayOfWeek;
        var hours = await db.ClinicOperatingHours.AsNoTracking().SingleOrDefaultAsync(h => h.DayOfWeek == dow, ct);
        if (hours is { IsClosed: true }) return $"The clinic is closed on {date.DayOfWeek}s. Please choose another day.";
        if (await db.DoctorBlockedDates.AsNoTracking().AnyAsync(b => b.DoctorId == doctorId && b.BlockedDate == date, ct))
            return "The doctor is not available on that date. Please choose another day.";
        var schedule = await db.DoctorSchedules.AsNoTracking().Where(x => x.DoctorId == doctorId).ToListAsync(ct);
        if (schedule.Any(x => x.IsActive))
        {
            var day = schedule.FirstOrDefault(x => x.DayOfWeek == dow);
            if (day is null || !day.IsActive) return $"The doctor does not see patients on {date.DayOfWeek}s. Please choose another day.";
        }
        return null;
    }

    //JJQR - AI (Nov 21, 2026) - queue actions already saved inside IQueueService; request a push on success.
    StaffResult From(QueueActionResult r, string fallback)
    {
        if (!r.Succeeded) return StaffResult.Fail(r.Error ?? fallback);
        _sync?.RequestSync();
        return StaffResult.Ok(r.BookingId);
    }
    //JJQR - END
}
//JJQR - END
