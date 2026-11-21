//JJQR - AI (Nov 21, 2026) - doctor extra services against a temp local SQLite node.
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.DoctorExtra;

sealed class CountingSync : ISyncBridge
{
    public int Calls;
    public void RequestSync() => Calls++;
    public Task<bool> IsRevokedAsync(CancellationToken ct = default) => Task.FromResult(false);
}

static class DoctorKit
{
    public static T Make<T>(this TestHost h, params object[] args) where T : class => ActivatorUtilities.CreateInstance<T>(h.Services, args);

    public static async Task<(Guid DoctorId, WalkInTicket Ticket, Guid PatientId)> MyWalkInAsync(this TestHost h, string first = "Ana")
    {
        var session = await h.LoginDoctorAsync();
        var doctorId = session.DoctorId!.Value;
        var p = await h.Get<IPatientService>().CreateAsync(new NewPatientRequest(first, "Reyes", new DateOnly(1990, 1, 1), SexType.Female, ContactNumber: "0917 123 4567"));
        var r = await h.Get<IQueueService>().CheckInWalkInAsync(new WalkInRequest(p.PatientId, doctorId));
        Assert.True(r.Succeeded, r.Error);
        return (doctorId, r.Ticket!, p.PatientId);
    }

    public static async Task<ClinicAppDbContext> DbAsync(this TestHost h) => await h.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
}

public class DoctorAppointmentsServiceTests
{
    [Fact]
    public async Task Today_lists_only_my_bookings_and_detail_hides_other_doctors()
    {
        await using var host = await TestHost.StartAsync();
        var (doctorId, t, _) = await host.MyWalkInAsync();
        var svc = host.Make<DoctorAppointmentsService>();

        var today = await svc.ListAsync(doctorId, AppointmentScope.Today);
        var mine = Assert.Single(today, r => r.BookingId == t.BookingId);
        Assert.Equal(BookingStatus.CheckedIn, mine.Status);
        Assert.Equal("Ana Reyes", mine.PatientName);
        Assert.All(today, r => Assert.Equal(ClinicClock.Today, r.AppointmentDate));
        Assert.True((await svc.ListAsync(doctorId, AppointmentScope.All)).Count >= today.Count);

        var detail = await svc.GetAsync(doctorId, t.BookingId);
        Assert.NotNull(detail);
        Assert.Equal(AppointmentState.Active, detail!.State);
        Assert.False(detail.HasConsultation);
        Assert.Null(await svc.GetAsync(Guid.NewGuid(), t.BookingId)); // another doctor → not found
        Assert.Null(await svc.GetAsync(doctorId, Guid.NewGuid()));
    }
}

public class DoctorPatientsServiceTests
{
    [Fact]
    public async Task List_has_one_row_per_patient_and_chart_shows_my_visit()
    {
        await using var host = await TestHost.StartAsync();
        var (doctorId, t, patientId) = await host.MyWalkInAsync("Rosa");
        var svc = host.Make<DoctorPatientsService>();
        var rows = await svc.ListAsync(doctorId);
        var row = Assert.Single(rows, r => r.PatientId == patientId);
        Assert.Equal(t.BookingId, row.LatestBookingId);
        Assert.Equal(rows.Count, rows.Select(r => r.PatientId).Distinct().Count());
        Assert.Single(svc.Filter(rows, "rosa reyes"));
        Assert.Contains(svc.Filter(rows, row.PatientCode), r => r.PatientId == patientId);

        var chart = await svc.GetChartAsync(doctorId, patientId);
        Assert.NotNull(chart);
        Assert.Equal("Rosa Reyes", chart!.Patient.FullName);
        Assert.Single(chart.Bookings);
        Assert.Empty(chart.Consultations);
        Assert.NotEmpty(chart.VitalTemplates);
        Assert.Empty(await svc.ListAsync(Guid.NewGuid()));
        Assert.Null(await svc.GetChartAsync(doctorId, Guid.NewGuid()));
        // another doctor sees the patient but none of my bookings
        Assert.Empty((await svc.GetChartAsync(Guid.NewGuid(), patientId))!.Bookings);
    }
}

public class DoctorScheduleServiceTests
{
    [Fact]
    public async Task Save_validates_upserts_seven_days_and_blocked_dates_round_trip()
    {
        await using var host = await TestHost.StartAsync();
        var (doctorId, _, _) = await host.MyWalkInAsync();
        var sync = new CountingSync();
        var svc = host.Make<DoctorScheduleService>(sync);

        var current = await svc.GetAsync(doctorId);
        Assert.NotNull(current);
        Assert.Equal(7, current!.Days.Count);
        Assert.Null(await svc.GetAsync(Guid.NewGuid()));

        var days = current.Days.Select(d => d with { IsActive = d.DayOfWeek is >= 1 and <= 5, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(17, 0) }).ToList();
        var bad = days.Select(d => d.DayOfWeek == 3 ? d with { EndTime = new TimeOnly(7, 0) } : d).ToList();
        Assert.Equal("Wednesday: end time must be after start time.", (await svc.SaveAsync(doctorId, bad, "")).Error);
        Assert.Equal("Daily patient limit must be a non-negative whole number, or blank for no limit.", (await svc.SaveAsync(doctorId, days, "x")).Error);
        Assert.Equal(0, sync.Calls);

        Assert.True((await svc.SaveAsync(doctorId, days, "40")).Succeeded);
        Assert.True((await svc.SaveAsync(doctorId, days, "40")).Succeeded); // upsert, no duplicates
        Assert.Equal(2, sync.Calls);
        var saved = (await svc.GetAsync(doctorId))!;
        Assert.Equal(40, saved.DailyPatientLimit);
        Assert.Equal(5, saved.Days.Count(d => d.IsActive));
        await using (var db = await host.DbAsync())
            Assert.Equal(7, await db.DoctorSchedules.CountAsync(s => s.DoctorId == doctorId));

        Assert.True(await svc.CountAffectedBookingsAsync(doctorId, ClinicClock.Today) >= 1); // demo seed adds more today
        Assert.Equal(0, await svc.CountAffectedBookingsAsync(doctorId, ClinicClock.Today.AddDays(400)));
        var date = ClinicClock.Today.AddDays(10);
        var added = await svc.AddBlockedDateAsync(doctorId, date, " Conference ");
        Assert.True(added.Succeeded);
        var blocked = Assert.Single((await svc.GetAsync(doctorId))!.BlockedDates);
        Assert.Equal("Conference", blocked.Reason);
        Assert.False((await svc.RemoveBlockedDateAsync(Guid.NewGuid(), blocked.Id)).Succeeded); // not mine
        Assert.True((await svc.RemoveBlockedDateAsync(doctorId, blocked.Id)).Succeeded);
        Assert.Empty((await svc.GetAsync(doctorId))!.BlockedDates);
        Assert.Equal(4, sync.Calls);
    }
}

public class DoctorProfileServiceTests
{
    [Fact]
    public async Task Save_profile_and_change_password()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginDoctorAsync();
        var sync = new CountingSync();
        var staffProfile = host.Make<StaffProfileService>(sync);
        var svc = host.Make<DoctorProfileService>((IStaffProfileService)staffProfile, sync);

        var me = await svc.GetMineAsync();
        Assert.NotNull(me);
        Assert.Equal("doctor@clinic.test", me!.Email);

        var edit = new DoctorProfileEdit("Dr. Ana Santos", "Pediatrics", "Kids first", "650", "LIC-1", "PTR-2", "");
        Assert.Equal("Enter your full name and a valid non-negative consultation fee.", (await svc.SaveAsync(edit with { ConsultationFee = "-5" })).Error);
        Assert.Equal(0, sync.Calls);
        Assert.True((await svc.SaveAsync(edit)).Succeeded);
        Assert.Equal(1, sync.Calls);
        var saved = (await svc.GetMineAsync())!;
        Assert.Equal("Dr. Ana Santos", saved.FullName);
        Assert.Equal(650m, saved.ConsultationFee);
        Assert.Equal("", saved.S2Number);

        const string next = "violet harbor lantern 42";
        Assert.Equal("Current password is incorrect.", (await svc.ChangePasswordAsync("wrong-password-1", next, next)).Error);
        Assert.True((await svc.ChangePasswordAsync(DevDataSeeder.DevPassword, next, next)).Succeeded);
        Assert.Equal(2, sync.Calls);
        var auth = host.Get<IAuthService>();
        await auth.LogoutAsync();
        Assert.True((await auth.LoginAsync("doctor@clinic.test", next, false)).Succeeded);
    }
}

public class DoctorTemplatesServiceTests
{
    [Fact]
    public async Task Templates_are_read_only_and_list_mine_plus_system()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var doctorId = session.DoctorId!.Value;
        await using (var db = await host.DbAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.MedicalCertificateTemplates.Add(new MedicalCertificateTemplate { Id = Guid.NewGuid(), DoctorId = doctorId, Title = "Fit to work", CreatedAt = now, UpdatedAt = now });
            db.MedicalCertificateTemplates.Add(new MedicalCertificateTemplate { Id = Guid.NewGuid(), DoctorId = Guid.NewGuid(), Title = "Someone else", CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }
        var t = await host.Make<DoctorTemplatesService>().GetAsync(doctorId);
        Assert.Contains(t.Certificates, c => c.Title == "Fit to work");
        Assert.DoesNotContain(t.Certificates, c => c.Title == "Someone else");
        Assert.True(TemplateTabs.ReadOnlyOnNode);
        Assert.Equal(6, TemplateTabs.All.Length);
        // the service exposes no write methods (cloud-owned, SYNC-API-CONTRACT §6)
        Assert.DoesNotContain(typeof(IDoctorTemplatesService).GetMethods(), m => m.Name != nameof(IDoctorTemplatesService.GetAsync));
    }
}
//JJQR - END
