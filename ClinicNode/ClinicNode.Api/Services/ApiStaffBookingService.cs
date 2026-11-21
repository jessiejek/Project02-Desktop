//JJQR - AI (21 Nov 2026) - Api implementation of IStaffBookingService, the same calls the web /staff/bookings/[id] makes:
//   GET /api/bookings/{id}                                             (patient / doctor / payment embeds)
//   PUT /api/bookings/{id}/status { status, reason }                   (Check in = CheckedIn, Undo check-in = Confirmed, Cancel = Cancelled + reason, Mark no-show = NoShow)
//   PUT /api/bookings/{id}/reschedule { appointment_date, reason }     (→ { old_booking_id, new_booking_id })
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Staff;
using static ClinicNode.Api.Services.ApiStaff;

namespace ClinicNode.Api.Services;

public sealed class ApiStaffBookingService(IApiClient api) : IStaffBookingService
{
    internal sealed record RescheduleDto(Guid OldBookingId, Guid NewBookingId);

    public async Task<StaffBookingDetail?> GetAsync(Guid bookingId, CancellationToken ct = default)
    {
        BookingDto? b;
        try { b = await api.GetAsync<BookingDto>($"/api/bookings/{bookingId:D}", null, ct); }
        catch (ApiNotFoundException) { return null; }
        if (b is null) return null;
        var pay = b.Payments;
        var payStatus = pay?.Status ?? PaymentStatus.Unpaid;
        return new StaffBookingDetail(
            b.BookingId, pay?.PaymentId,
            PatientName(b.Patients), b.PatientId, b.Patients?.PatientCode ?? "", b.Patients?.Sex, b.Patients?.DateOfBirth,
            b.Patients?.ContactNumber ?? "", b.Patients?.Email ?? "",
            b.Doctors?.StaffAccounts?.FullName ?? "", b.Doctors?.Specialization ?? "",
            b.AppointmentDate, b.SlotStartTime ?? TimeOnly.MinValue, b.QueueNumber, b.Status,
            payStatus, b.PaymentMode ?? PaymentMode.PayAtClinic, b.AmountDue ?? 0,
            StaffRules.DisplayOrNumber(pay?.OrNumber, b.BookingId, payStatus is PaymentStatus.Paid or PaymentStatus.Waived),
            pay?.WaivedReason);
    }

    Task<StaffResult> Status(Guid id, BookingStatus status, string? reason, string fallback, CancellationToken ct) =>
        Write(() => api.SendAsync(HttpMethod.Put, $"/api/bookings/{id:D}/status", new { status = status.ToString(), reason }, null, ct), id, fallback);

    public Task<StaffResult> CheckInAsync(Guid bookingId, CancellationToken ct = default) =>
        Status(bookingId, BookingStatus.CheckedIn, null, "Could not update the booking.", ct);

    public Task<StaffResult> UndoCheckInAsync(Guid bookingId, CancellationToken ct = default) =>
        Status(bookingId, BookingStatus.Confirmed, null, "Could not update the booking.", ct);

    public Task<StaffResult> CancelAsync(Guid bookingId, string? reason, CancellationToken ct = default)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length < StaffRules.MinDialogReasonLength) return Task.FromResult(StaffResult.Fail("At least 5 characters."));
        return Status(bookingId, BookingStatus.Cancelled, trimmed, "Could not cancel the booking.", ct);
    }

    public Task<StaffResult> MarkNoShowAsync(Guid bookingId, string? reason, CancellationToken ct = default) =>
        Status(bookingId, BookingStatus.NoShow, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(), "Could not update booking.", ct);

    public async Task<StaffResult> RescheduleAsync(Guid bookingId, DateOnly newDate, string? reason, CancellationToken ct = default)
    {
        try
        {
            var r = await api.SendAsync<RescheduleDto>(HttpMethod.Put, $"/api/bookings/{bookingId:D}/reschedule",
                new { appointment_date = Day(newDate), reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() }, null, ct);
            return r is null ? StaffResult.Fail("Could not reschedule.") : StaffResult.Ok(r.NewBookingId);
        }
        catch (Exception ex) when (IsServerAnswer(ex))
        {
            return StaffResult.Fail(string.IsNullOrWhiteSpace(ex.Message) ? "Could not reschedule." : ex.Message);
        }
    }
}
//JJQR - END
