// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Admin;
using ClinicNode.Core.Patients;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Admin;

public class AdminBookingTests
{
    [Fact]
    public async Task Create_list_and_reschedule_a_booking()
    {
        await using var host = await AdminTestHost.StartAsync();
        var admin = await host.LoginAdminAsync();
        var patients = host.Get<IPatientService>();
        var bookings = host.Get<IAdminBookingService>();
        var doctorId = (await host.LoginDoctorAsync()).DoctorId!.Value;
        await host.LoginAdminAsync();

        var patient = await patients.CreateAsync(new NewPatientRequest("Lina", "Cruz", new DateOnly(1990, 5, 1), SexType.Female));
        var date = ClinicClock.Today.AddDays(7);
        // If the demo doctor has a schedule that closes that weekday, walk forward.
        for (var i = 0; i < 14; i++)
        {
            var reason = await bookings.ClosedReasonAsync(date, doctorId);
            if (reason is null) break;
            date = date.AddDays(1);
        }

        var created = await bookings.CreateAsync(new CreateAdminBookingRequest(
            patient.PatientId, doctorId, date, new TimeOnly(9, 0), new TimeOnly(9, 30),
            [], PaymentMode.PayAtClinic, IsWalkIn: false, Notes: null));
        Assert.True(created.Succeeded, created.Error);
        Assert.Equal(BookingStatus.Pending, created.Value!.Status);
        Assert.False(string.IsNullOrWhiteSpace(created.Value.QueueNumber));

        var list = await bookings.ListAsync(q: "Lina", doctorId: null, status: null, date: date, from: null, to: null);
        Assert.Contains(list.Items, b => b.BookingId == created.Value.BookingId);

        var next = date.AddDays(1);
        for (var i = 0; i < 14; i++)
        {
            if (await bookings.ClosedReasonAsync(next, doctorId) is null) break;
            next = next.AddDays(1);
        }
        var moved = await bookings.RescheduleAsync(created.Value.BookingId, next, "Patient asked to move");
        Assert.True(moved.Succeeded, moved.Error);
        Assert.Equal(BookingStatus.Rescheduled, moved.Value!.OldStatus);
        Assert.NotEqual(moved.Value.OldBookingId, moved.Value.NewBookingId);

        var old = await bookings.GetAsync(created.Value.BookingId);
        Assert.Equal(BookingStatus.Rescheduled, old.Value!.Booking.Status);
        var fresh = await bookings.GetAsync(moved.Value.NewBookingId);
        Assert.Equal(next, fresh.Value!.Booking.AppointmentDate);
        Assert.Equal(BookingStatus.Pending, fresh.Value.Booking.Status);
    }

    [Fact]
    public async Task Cannot_reschedule_to_a_past_date()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var patients = host.Get<IPatientService>();
        var bookings = host.Get<IAdminBookingService>();
        var doctorId = (await host.LoginDoctorAsync()).DoctorId!.Value;
        await host.LoginAdminAsync();
        var patient = await patients.CreateAsync(new NewPatientRequest("Omar", "Diaz", new DateOnly(1988, 2, 2), SexType.Male));
        var date = ClinicClock.Today.AddDays(2);
        for (var i = 0; i < 14; i++)
        {
            if (await bookings.ClosedReasonAsync(date, doctorId) is null) break;
            date = date.AddDays(1);
        }
        var created = await bookings.CreateAsync(new CreateAdminBookingRequest(
            patient.PatientId, doctorId, date, new TimeOnly(10, 0), new TimeOnly(10, 30),
            [], PaymentMode.PayAtClinic, IsWalkIn: false, Notes: null));
        Assert.True(created.Succeeded, created.Error);
        var past = await bookings.RescheduleAsync(created.Value!.BookingId, ClinicClock.Today.AddDays(-1), "too late");
        Assert.False(past.Succeeded);
        Assert.Contains("past date", past.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cannot_cancel_a_paid_booking_or_make_an_illegal_transition()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var patients = host.Get<IPatientService>();
        var bookings = host.Get<IAdminBookingService>();
        var doctorId = (await host.LoginDoctorAsync()).DoctorId!.Value;
        await host.LoginAdminAsync();
        var patient = await patients.CreateAsync(new NewPatientRequest("Pearl", "Ube", new DateOnly(1995, 9, 9), SexType.Female));
        var date = ClinicClock.Today.AddDays(3);
        for (var i = 0; i < 14; i++)
        {
            if (await bookings.ClosedReasonAsync(date, doctorId) is null) break;
            date = date.AddDays(1);
        }
        var created = await bookings.CreateAsync(new CreateAdminBookingRequest(
            patient.PatientId, doctorId, date, new TimeOnly(11, 0), new TimeOnly(11, 30),
            [], PaymentMode.PayAtClinic, IsWalkIn: false, Notes: null));
        Assert.True(created.Succeeded, created.Error);

        var illegal = await bookings.UpdateStatusAsync(created.Value!.BookingId, BookingStatus.InProgress, null);
        Assert.False(illegal.Succeeded);
        Assert.Contains("Illegal booking status transition", illegal.Error);

        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var pay = await db.Payments.SingleAsync(p => p.BookingId == created.Value.BookingId);
            pay.Status = PaymentStatus.Paid;
            pay.AmountReceived = pay.Amount;
            pay.ConfirmedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var cancel = await bookings.UpdateStatusAsync(created.Value.BookingId, BookingStatus.Cancelled, "changed mind");
        Assert.False(cancel.Succeeded);
        Assert.Contains("already been paid", cancel.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Second_active_booking_on_the_same_date_is_rejected()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var patients = host.Get<IPatientService>();
        var bookings = host.Get<IAdminBookingService>();
        var doctorId = (await host.LoginDoctorAsync()).DoctorId!.Value;
        await host.LoginAdminAsync();
        var patient = await patients.CreateAsync(new NewPatientRequest("Quin", "Yap", new DateOnly(2000, 1, 1), SexType.Male));
        var date = ClinicClock.Today.AddDays(5);
        for (var i = 0; i < 14; i++)
        {
            if (await bookings.ClosedReasonAsync(date, doctorId) is null) break;
            date = date.AddDays(1);
        }
        var first = await bookings.CreateAsync(new CreateAdminBookingRequest(
            patient.PatientId, doctorId, date, new TimeOnly(8, 0), new TimeOnly(8, 30),
            [], PaymentMode.PayAtClinic, IsWalkIn: false, Notes: null));
        Assert.True(first.Succeeded, first.Error);
        var second = await bookings.CreateAsync(new CreateAdminBookingRequest(
            patient.PatientId, doctorId, date, new TimeOnly(14, 0), new TimeOnly(14, 30),
            [], PaymentMode.PayAtClinic, IsWalkIn: false, Notes: null));
        Assert.False(second.Succeeded);
        Assert.Contains("already has a booking", second.Error, StringComparison.OrdinalIgnoreCase);
    }
}
// JJQR - END
