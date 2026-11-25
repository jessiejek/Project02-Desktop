//JJQR - AI (2026-11-25) - why: GAV-4 "Not finished on an earlier day": grouping rules (same as web), the local list (offline), the local no-show,
// Move to today refused offline (needs the clinic server), and "Continue" / Start consultation working on an earlier-day patient (the old today-only check).
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.PracticeTests;

public class LeftoverQueueTests
{
    static LeftoverRow Row(DateOnly date, BookingStatus status, string q = "R-001", bool priority = false, int minute = 0, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), date, q, priority, Guid.NewGuid(), "Test Patient", "P-0001", Guid.NewGuid(), status, VisitType.New,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(9, minute)), TimeSpan.FromHours(8)), new DateTimeOffset(date.ToDateTime(new TimeOnly(16, 52)), TimeSpan.FromHours(8)));

    [Fact]
    public void Groups_earlier_days_newest_first_priority_then_arrival_and_drops_today_finished_old_and_repeats()
    {
        var today = new DateOnly(2026, 11, 25);
        var dup = Guid.NewGuid();
        var rows = new[]
        {
            Row(today.AddDays(-2), BookingStatus.CheckedIn, "R-004"),
            Row(today.AddDays(-1), BookingStatus.CheckedIn, "R-007", minute: 30),
            Row(today.AddDays(-1), BookingStatus.InProgress, "S-002", priority: true, minute: 40, id: dup),
            Row(today.AddDays(-1), BookingStatus.InProgress, "S-002", priority: true, minute: 40, id: dup),
            Row(today.AddDays(-1), BookingStatus.OnHold, "R-005", minute: 10),
            Row(today, BookingStatus.CheckedIn),
            Row(today.AddDays(-1), BookingStatus.Completed),
            Row(today.AddDays(-1), BookingStatus.NoShow),
            Row(today.AddDays(-31), BookingStatus.CheckedIn),
            Row(today.AddDays(1), BookingStatus.CheckedIn),
        };
        var g = LeftoverGroups.Group(rows, today);
        Assert.Equal(2, g.Count);
        Assert.Equal("Tue, Nov 24", g[0].Header);
        Assert.Equal("3 patients", g[0].CountLabel);
        Assert.Equal("Nov 24", g[0].Chip);
        Assert.Equal(["S-002", "R-005", "R-007"], g[0].Items.Select(i => i.QueueNumber).ToArray());
        Assert.Equal("1 patient", g[1].CountLabel);

        var none = new Dictionary<DateOnly, bool>();
        Assert.True(LeftoverGroups.IsOpen(g, g[0].Date, none));
        Assert.False(LeftoverGroups.IsOpen(g, g[1].Date, none));
        Assert.True(LeftoverGroups.IsOpen(g, g[1].Date, new Dictionary<DateOnly, bool> { [g[1].Date] = true }));

        Assert.Equal(("In progress", "since Nov 24 4:52 PM"), LeftoverGroups.StatusText(g[0].Items[0], today));
        Assert.Equal(("On hold", "since Nov 24"), LeftoverGroups.StatusText(g[0].Items[1], today));
        Assert.Equal(("Waiting", "since Nov 24"), LeftoverGroups.StatusText(g[0].Items[2], today));
        Assert.Equal("Nov 24, 2025", LeftoverGroups.Chip(new DateOnly(2025, 11, 24), today));
    }

    static async Task<Guid> LeftoverAsync(TestHost host, Guid doctorId, string first, int daysAgo, BookingStatus status)
    {
        var p = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest(first, "Leftover", new DateOnly(1990, 1, 1), SexType.Female));
        var t = await host.Get<IQueueService>().CheckInWalkInAsync(new WalkInRequest(p.PatientId, doctorId));
        Assert.True(t.Succeeded, t.Error);
        var id = t.Ticket!.BookingId;
        await using var db = await host.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        var b = await db.Bookings.SingleAsync(x => x.BookingId == id);
        b.AppointmentDate = ClinicClock.Today.AddDays(-daysAgo);
        b.Status = status;
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Local_list_scope_no_show_and_move_refused_offline()
    {
        await using var host = await TestHost.StartAsync();
        var doctorId = (await host.LoginDoctorAsync()).DoctorId!.Value;
        var svc = new LocalLeftoverQueueService(host.Get<IDbContextFactory<ClinicAppDbContext>>(), host.Get<IQueueService>());
        var waiting = await LeftoverAsync(host, doctorId, "Ana", 1, BookingStatus.CheckedIn);
        var seeing = await LeftoverAsync(host, doctorId, "Bea", 2, BookingStatus.InProgress);
        var done = await LeftoverAsync(host, doctorId, "Cora", 1, BookingStatus.Completed);

        var mine = await svc.ListAsync(doctorId);
        Assert.Contains(mine, r => r.BookingId == waiting && r.Status == BookingStatus.CheckedIn && r.PatientName == "Ana Leftover");
        Assert.Contains(mine, r => r.BookingId == seeing);
        Assert.DoesNotContain(mine, r => r.BookingId == done);
        Assert.DoesNotContain(await svc.ListAsync(Guid.NewGuid()), r => r.BookingId == waiting); // another doctor's list
        Assert.Contains(await svc.ListAsync(null), r => r.BookingId == waiting);                  // staff: the clinic's

        var move = await svc.MoveToTodayAsync(waiting);
        Assert.False(move.Succeeded);
        Assert.Equal(LeftoverGroups.MoveNeedsServer, move.Error);

        var ns = await svc.MarkNoShowAsync(waiting);
        Assert.True(ns.Succeeded, ns.Error);
        Assert.DoesNotContain(await svc.ListAsync(doctorId), r => r.BookingId == waiting);
    }

    [Fact]
    public async Task Start_consultation_works_on_an_earlier_day_patient()
    {
        await using var host = await TestHost.StartAsync();
        var doctorId = (await host.LoginDoctorAsync()).DoctorId!.Value;
        var dash = host.Get<IDoctorDashboardService>();
        var waiting = await LeftoverAsync(host, doctorId, "Dina", 1, BookingStatus.CheckedIn);
        var seeing = await LeftoverAsync(host, doctorId, "Eva", 1, BookingStatus.InProgress);
        var tooOld = await LeftoverAsync(host, doctorId, "Fe", 40, BookingStatus.CheckedIn);

        var start = await dash.StartConsultAsync(doctorId, waiting);
        Assert.True(start.Succeeded, start.Error);
        await using (var db = await host.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync())
            Assert.Equal(BookingStatus.InProgress, (await db.Bookings.SingleAsync(b => b.BookingId == waiting)).Status);

        Assert.True((await dash.StartConsultAsync(doctorId, seeing)).Succeeded);
        Assert.False((await dash.StartConsultAsync(doctorId, tooOld)).Succeeded);
        Assert.False((await dash.StartConsultAsync(Guid.NewGuid(), seeing)).Succeeded); // not this doctor's
    }
}
//JJQR - END
