//JJQR - AI (Nov 21, 2026) - worked example, part 2: doctor dashboard (read composed from several endpoints, a write, a method the API does not serve yet).
using System.Net;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Core.Practice;

namespace ClinicNode.Api.Tests;

public class DoctorDashboardOnlineFirstTests
{
    private static Task<ApiRig> Rig(bool healthy = true) => ApiRig.StartAsync(s => s.AddOnlineFirstDoctorDashboard(), healthy: healthy);

    private static void ServeDashboard(ApiRig rig)
    {
        var healthy = rig.Http.Route;
        var bookingId = Guid.NewGuid();
        rig.Http.Route = r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (p == $"/api/doctors/{rig.DoctorId:D}") return FakeHandler.Json(new { doctor_id = rig.DoctorId, staff_accounts = new { full_name = "Dr. Online" } });
            if (p.EndsWith("/day-status") && r.Method == HttpMethod.Get) return FakeHandler.Status(HttpStatusCode.NoContent);
            if (p.EndsWith("/day-status") && r.Method == HttpMethod.Put) return FakeHandler.Json(new { doctor_id = rig.DoctorId, status = "RunningLate" });
            if (p == "/api/bookings/doctor/today") return FakeHandler.Json(new[]
            {
                new { booking_id = bookingId, patient_id = Guid.NewGuid(), appointment_date = "2026-11-21", slot_start_time = "09:30:00", status = "CheckedIn", queue_number = "R-003",
                      visit_type = "New", discount_category = (string?)null, updated_at = DateTimeOffset.UtcNow.AddMinutes(-12), patients = new { first_name = "Ben", last_name = "Reyes" } },
                new { booking_id = Guid.NewGuid(), patient_id = Guid.NewGuid(), appointment_date = "2026-11-21", slot_start_time = "09:00:00", status = "Completed", queue_number = "R-001",
                      visit_type = "New", discount_category = (string?)null, updated_at = DateTimeOffset.UtcNow.AddHours(-1), patients = new { first_name = "Cara", last_name = "Diaz" } },
            });
            if (p == "/api/doctor/performance") return FakeHandler.Json(new
            {
                doctor_id = rig.DoctorId, currency = "PHP", week_starts_on = "monday",
                period = new { name = "today", from = "2026-11-21", to = "2026-11-21" }, previous_period = new { name = "today", from = "2026-11-20", to = "2026-11-20" },
                current = new { patients_seen = 1, collected = 0, billed = 0 }, previous = new { patients_seen = 0, collected = 0, billed = 0 },
                series = Array.Empty<object>(), top_diagnoses = Array.Empty<object>(),
                unpaid = new { count = 2, total = 800, items = new[] { new { booking_id = Guid.NewGuid(), patient_id = Guid.NewGuid(), patient_name = "Dan Lim", appointment_date = "2026-11-19", amount_due = 800 } } },
                follow_ups_due = new { count = 1, items = Array.Empty<object>() },
            });
            if (p == "/api/announcements") return FakeHandler.Json(new[] { new { title = "Holiday hours", body = "Closed Nov 30", is_active = true, created_at = DateTimeOffset.UtcNow } });
            return healthy(r);
        };
    }

    [Fact]
    public async Task Online_the_dashboard_is_composed_from_the_web_endpoints()
    {
        await using var rig = await Rig();
        ServeDashboard(rig);
        var d = await rig.Get<IDoctorDashboardService>().GetAsync(rig.DoctorId);

        Assert.Equal("Dr. Online", d.DoctorName);
        Assert.Equal(DoctorDayStatusEnum.Available, d.DayStatus);   // 204 = no row = Available
        Assert.Equal(1, d.Waiting);
        Assert.Equal(1, d.CompletedToday);
        Assert.Equal(2, d.TotalToday);
        Assert.InRange(d.LongestWaitMinutes, 11, 14);
        Assert.Equal("Ben Reyes", Assert.Single(d.UpNext).PatientName);
        Assert.Equal(2, d.UnpaidCompletedMonth);
        Assert.Equal("Dan Lim", Assert.Single(d.Unpaid).PatientName);
        Assert.Equal("Holiday hours", Assert.Single(d.Announcements).Title);
        Assert.Equal(0, rig.Sync.Calls);
    }

    [Fact]
    public async Task Offline_the_dashboard_comes_from_the_local_database()
    {
        await using var rig = await Rig(healthy: false);
        var d = await rig.Get<IDoctorDashboardService>().GetAsync(rig.DoctorId);
        Assert.False(string.IsNullOrWhiteSpace(d.DoctorName));
        Assert.NotEqual("Dr. Online", d.DoctorName);
        Assert.False(rig.Http.Saw("/api/bookings"));
    }

    [Fact]
    public async Task Network_failure_on_one_of_the_composed_calls_falls_back_to_local()
    {
        await using var rig = await Rig();
        ServeDashboard(rig);
        var served = rig.Http.Route;
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/api/bookings/doctor/today" ? throw new HttpRequestException("reset") : served(r);
        var d = await rig.Get<IDoctorDashboardService>().GetAsync(rig.DoctorId);
        Assert.NotEqual("Dr. Online", d.DoctorName);
        Assert.False(rig.Connectivity.IsOnline);
    }

    [Fact]
    public async Task Online_write_goes_to_the_api_only_and_asks_the_sync_loop_for_a_pull()
    {
        await using var rig = await Rig();
        ServeDashboard(rig);
        var before = await PendingAsync(rig);
        var r = await rig.Get<IDoctorDashboardService>().SetDayStatusAsync(rig.DoctorId, DoctorDayStatusEnum.RunningLate);
        Assert.True(r.Succeeded, r.Error);
        var put = Assert.Single(rig.Http.Requests, x => x.Method == HttpMethod.Put);
        Assert.Contains("\"status\":\"RunningLate\"", put.Body);
        Assert.Contains("\"status_date\":", put.Body);
        Assert.Equal(1, rig.Sync.Calls);
        // and nothing was written locally / to the outbox by the API path
        Assert.Equal(before, await PendingAsync(rig));
    }

    [Fact]
    public async Task Offline_write_uses_the_local_service_which_queues_it_in_the_outbox()
    {
        await using var rig = await Rig(healthy: false);
        var before = await PendingAsync(rig);
        var r = await rig.Get<IDoctorDashboardService>().SetDayStatusAsync(rig.DoctorId, DoctorDayStatusEnum.UnavailableToday);
        Assert.True(r.Succeeded, r.Error);
        Assert.DoesNotContain(rig.Http.Requests, x => x.Method == HttpMethod.Put);
        Assert.True(await PendingAsync(rig) > before, "the offline write must be queued in the outbox");
    }

    [Fact]
    public async Task A_server_validation_answer_on_a_write_is_returned_as_a_failed_result_not_retried_locally()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r => r.Method == HttpMethod.Put
            ? FakeHandler.Text("""{"title":"Validation","errors":{"status":["Choose a valid status."]}}""", HttpStatusCode.BadRequest) : healthy(r);
        var before = await PendingAsync(rig);
        var r = await rig.Get<IDoctorDashboardService>().SetDayStatusAsync(rig.DoctorId, DoctorDayStatusEnum.Available);
        Assert.False(r.Succeeded);
        Assert.Equal("Choose a valid status.", r.Error);
        Assert.Equal(before, await PendingAsync(rig));
    }

    [Fact]
    public async Task Methods_the_api_cannot_serve_yet_fall_back_to_local_without_error()
    {
        await using var rig = await Rig();
        ServeDashboard(rig);
        var r = await rig.Get<IDoctorDashboardService>().CallNextAsync(rig.DoctorId); // ApiNotSupportedException → Local
        Assert.NotNull(r);
        Assert.DoesNotContain(rig.Http.Paths, p => p.Contains("queue", StringComparison.OrdinalIgnoreCase));
        Assert.True(rig.Connectivity.IsOnline); // not a network failure
    }

    [Fact]
    public async Task Analytics_for_a_month_use_v_doctor_earnings_and_other_ranges_stay_local()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/api/reports/doctor-earnings"
            ? FakeHandler.Json(new[]
            {
                new { doctor_id = rig.DoctorId, period = "2026-10", completed_visits = 10, gross_billed = 5000, collected = 4000, waived = 100 },
                new { doctor_id = rig.DoctorId, period = "2026-11", completed_visits = 12, gross_billed = 6000, collected = 5000, waived = 0 },
            }) : healthy(r);
        var svc = rig.Get<IDoctorDashboardService>();
        var month = await svc.GetAnalyticsAsync(rig.DoctorId, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), isMonthRange: true);
        Assert.Equal(12, month.Totals.CompletedVisits);
        Assert.Equal(5000m, month.Totals.Collected);
        Assert.Equal(25, month.CollectedDeltaPercent);                 // 4000 → 5000
        Assert.Equal(["2026-10", "2026-11"], month.Trend.Select(t => t.Period));

        rig.Http.Requests.Clear();
        _ = await svc.GetAnalyticsAsync(rig.DoctorId, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 9), isMonthRange: false);
        Assert.False(rig.Http.Saw("/api/reports/doctor-earnings"));
    }

    private static async Task<int> PendingAsync(ApiRig rig)
    {
        await using var db = await rig.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>().CreateDbContextAsync();
        return (await rig.Get<ClinicNode.Data.ISyncOutbox>().ListPendingAsync(db, 500)).Count;
    }
}
//JJQR - END
