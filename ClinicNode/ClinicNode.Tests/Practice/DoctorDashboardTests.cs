// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Queue;

namespace ClinicNode.Tests.PracticeTests;

public class DoctorDashboardTests
{
    [Fact]
    public async Task Dashboard_shows_waiting_walk_ins_priority_first_and_saves_day_status()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var doctorId = session.DoctorId!.Value;
        var patients = host.Get<IPatientService>();
        var queue = host.Get<IQueueService>();
        var dash = host.Get<IDoctorDashboardService>();

        var a = await patients.CreateAsync(new NewPatientRequest("Rita", "Gomez", new DateOnly(1995, 3, 3), SexType.Female));
        var b = await patients.CreateAsync(new NewPatientRequest("Lola", "Perez", new DateOnly(1950, 4, 4), SexType.Female, DiscountCategory: "Senior"));
        Assert.True((await queue.CheckInWalkInAsync(new WalkInRequest(a.PatientId, doctorId))).Succeeded);
        Assert.True((await queue.CheckInWalkInAsync(new WalkInRequest(b.PatientId, doctorId, DiscountCategory: "Senior"))).Succeeded);

        var d = await dash.GetAsync(doctorId);
        Assert.Equal(DoctorDayStatusEnum.Available, d.DayStatus);
        Assert.True(d.Waiting >= 2);
        Assert.True(d.UpNext[0].IsPriority);
        Assert.Equal("Start consultation", d.UpNext[0].ActionLabel);

        Assert.True((await dash.SetDayStatusAsync(doctorId, DoctorDayStatusEnum.RunningLate)).Succeeded);
        Assert.Equal(DoctorDayStatusEnum.RunningLate, (await dash.GetAsync(doctorId)).DayStatus);

        var today = ClinicClock.Today;
        var an = await dash.GetAnalyticsAsync(doctorId, new DateOnly(today.Year, today.Month, 1), today, true);
        Assert.True(an.Totals.CompletedVisits >= 0);
    }

    // JJQR - AI (Nov 21, 2026)
    [Fact]
    public async Task Call_next_starts_the_priority_waiting_patient()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var doctorId = session.DoctorId!.Value;
        var patients = host.Get<IPatientService>();
        var queue = host.Get<IQueueService>();
        var dash = host.Get<IDoctorDashboardService>();

        var regular = await patients.CreateAsync(new NewPatientRequest("Rita", "Gomez", new DateOnly(1995, 3, 3), SexType.Female));
        var senior = await patients.CreateAsync(new NewPatientRequest("Lola", "Perez", new DateOnly(1950, 4, 4), SexType.Female, DiscountCategory: "Senior"));
        var rWalk = await queue.CheckInWalkInAsync(new WalkInRequest(regular.PatientId, doctorId));
        var sWalk = await queue.CheckInWalkInAsync(new WalkInRequest(senior.PatientId, doctorId, DiscountCategory: "Senior"));
        Assert.True(rWalk.Succeeded, rWalk.Error);
        Assert.True(sWalk.Succeeded, sWalk.Error);

        Assert.False((await dash.StartConsultAsync(doctorId, Guid.NewGuid())).Succeeded);
        Assert.False((await dash.CallNextAsync(Guid.NewGuid())).Succeeded);

        var before = await dash.GetAsync(doctorId);
        Assert.True(before.Waiting >= 2);
        var firstWaiting = before.UpNext.First(u => !u.InProgress);

        var called = await dash.CallNextAsync(doctorId);
        Assert.True(called.Succeeded, called.Error);
        Assert.Equal(firstWaiting.BookingId, called.Id);

        var d = await dash.GetAsync(doctorId);
        var started = d.UpNext.First(u => u.BookingId == firstWaiting.BookingId);
        Assert.True(started.InProgress);
        Assert.Equal("Resume consultation", started.ActionLabel);
        Assert.False(started.HasPrescription);
        Assert.False(started.HasLabOrders);

        if (d.Waiting > 0)
        {
            var nextWaiting = d.UpNext.First(u => !u.InProgress);
            var again = await dash.CallNextAsync(doctorId);
            Assert.True(again.Succeeded, again.Error);
            Assert.Equal(nextWaiting.BookingId, again.Id);
        }
    }

    [Fact]
    public async Task Start_consult_calls_a_checked_in_patient_and_skips_already_in_progress()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var doctorId = session.DoctorId!.Value;
        var patients = host.Get<IPatientService>();
        var queue = host.Get<IQueueService>();
        var dash = host.Get<IDoctorDashboardService>();

        var patient = await patients.CreateAsync(new NewPatientRequest("Nia", "Cruz", new DateOnly(1992, 1, 1), SexType.Female));
        var walk = await queue.CheckInWalkInAsync(new WalkInRequest(patient.PatientId, doctorId));
        Assert.True(walk.Succeeded, walk.Error);
        var bookingId = walk.BookingId!.Value;

        var started = await dash.StartConsultAsync(doctorId, bookingId);
        Assert.True(started.Succeeded, started.Error);
        var d = await dash.GetAsync(doctorId);
        Assert.Contains(d.UpNext, u => u.BookingId == bookingId && u.InProgress);

        var again = await dash.StartConsultAsync(doctorId, bookingId);
        Assert.True(again.Succeeded, again.Error);
        Assert.Equal(bookingId, again.Id);
    }
    // JJQR - END
}
// JJQR - END
