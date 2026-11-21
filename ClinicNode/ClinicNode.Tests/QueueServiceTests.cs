// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Queue;

namespace ClinicNode.Tests;

public class QueueServiceTests
{
    [Fact]
    public async Task Walk_in_issues_R_and_S_numbers_and_sorts_priority_first()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginStaffAsync();
        var patients = host.Get<IPatientService>();
        var queue = host.Get<IQueueService>();
        var doctors = await queue.ListDoctorsAsync();
        var doctorId = doctors[0].DoctorId;

        var regular = await patients.CreateAsync(new NewPatientRequest("Rita", "Gomez", new DateOnly(1995, 3, 3), SexType.Female));
        var senior = await patients.CreateAsync(new NewPatientRequest("Lola", "Perez", new DateOnly(1950, 4, 4), SexType.Female, DiscountCategory: "Senior"));

        var r = await queue.CheckInWalkInAsync(new WalkInRequest(regular.PatientId, doctorId));
        var s = await queue.CheckInWalkInAsync(new WalkInRequest(senior.PatientId, doctorId, DiscountCategory: "Senior"));
        Assert.True(r.Succeeded, r.Error);
        Assert.True(s.Succeeded, s.Error);
        Assert.StartsWith("R-", r.Ticket!.QueueNumber);
        Assert.StartsWith("S-", s.Ticket!.QueueNumber);
        Assert.True(s.Ticket.ProvisionalFee > 0);

        var board = await queue.GetBoardAsync();
        var ours = board.Items.Where(i => i.BookingId == r.Ticket.BookingId || i.BookingId == s.Ticket.BookingId).ToList();
        Assert.Equal(2, ours.Count);
        Assert.Equal(s.Ticket.QueueNumber, ours[0].QueueNumber);
        Assert.True(ours[0].IsPriority);
        Assert.Equal(r.Ticket.QueueNumber, ours[1].QueueNumber);
        var indexS = board.Items.ToList().FindIndex(i => i.BookingId == s.Ticket.BookingId);
        var indexR = board.Items.ToList().FindIndex(i => i.BookingId == r.Ticket.BookingId);
        Assert.True(indexS < indexR, "S-lane must sort ahead of R-lane");
    }

    [Fact]
    public async Task Invalid_discount_is_rejected()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var patients = host.Get<IPatientService>();
        var queue = host.Get<IQueueService>();
        var doctorId = (await queue.ListDoctorsAsync())[0].DoctorId;
        var patient = await patients.CreateAsync(new NewPatientRequest("Ned", "Lim", new DateOnly(1990, 1, 1), SexType.Male));
        var result = await queue.CheckInWalkInAsync(new WalkInRequest(patient.PatientId, doctorId, DiscountCategory: "Employee"));
        Assert.False(result.Succeeded);
        Assert.Contains("Senior or PWD", result.Error);
    }

    [Fact]
    public async Task Cancel_requires_a_reason_and_refuses_a_paid_booking()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var patients = host.Get<IPatientService>();
        var queue = host.Get<IQueueService>();
        var payments = host.Get<ClinicNode.Core.Payments.IPaymentService>();
        var doctorId = (await queue.ListDoctorsAsync())[0].DoctorId;
        var patient = await patients.CreateAsync(new NewPatientRequest("Cara", "Diaz", new DateOnly(1992, 2, 2), SexType.Female));
        var ticket = (await queue.CheckInWalkInAsync(new WalkInRequest(patient.PatientId, doctorId))).Ticket!;

        var missing = await queue.CancelAsync(ticket.BookingId, "  ");
        Assert.False(missing.Succeeded);

        var pay = await payments.ConfirmAsync(ticket.PaymentId, new ClinicNode.Core.Payments.ConfirmPaymentRequest(PaymentMethod.Cash));
        Assert.True(pay.Succeeded, pay.Error);
        var paid = await queue.CancelAsync(ticket.BookingId, "Changed mind");
        Assert.False(paid.Succeeded);
        Assert.Contains("paid", paid.Error, StringComparison.OrdinalIgnoreCase);

        var other = await patients.CreateAsync(new NewPatientRequest("Cora", "Diaz", new DateOnly(1993, 3, 3), SexType.Female));
        var open = (await queue.CheckInWalkInAsync(new WalkInRequest(other.PatientId, doctorId))).Ticket!;
        var cancelled = await queue.CancelAsync(open.BookingId, "Patient left");
        Assert.True(cancelled.Succeeded, cancelled.Error);
        Assert.Equal(BookingStatus.Cancelled, cancelled.Status);
        var board = await queue.GetBoardAsync();
        Assert.DoesNotContain(board.Items, i => i.BookingId == open.BookingId);
    }

    [Fact]
    public async Task Call_hold_and_no_show_follow_the_booking_status_machine()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var patients = host.Get<IPatientService>();
        var queue = host.Get<IQueueService>();
        var doctorId = (await queue.ListDoctorsAsync())[0].DoctorId;
        var patient = await patients.CreateAsync(new NewPatientRequest("Ike", "Tan", new DateOnly(1980, 8, 8), SexType.Male));
        var ticket = (await queue.CheckInWalkInAsync(new WalkInRequest(patient.PatientId, doctorId))).Ticket!;

        Assert.True((await queue.CallAsync(ticket.BookingId)).Succeeded);
        var hold = await queue.HoldAsync(ticket.BookingId);
        Assert.True(hold.Succeeded, hold.Error);
        Assert.Equal(BookingStatus.OnHold, hold.Status);
        var noShow = await queue.NoShowAsync(ticket.BookingId);
        Assert.True(noShow.Succeeded, noShow.Error);
        Assert.Equal(BookingStatus.NoShow, noShow.Status);
        var again = await queue.CallAsync(ticket.BookingId);
        Assert.False(again.Succeeded);
    }
}
// JJQR - END
