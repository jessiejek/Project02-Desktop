//JJQR - AI (Nov 21, 2026) - the worked example (docs/ONLINE-FIRST.md §6): doctor Performance, online / offline / network-failure / session / error paths.
using System.Net;
using ClinicNode.Api.Http;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.Api.Tests;

public class DoctorPerformanceOnlineFirstTests
{
    private static object Stats(decimal collected, int seen) => new
    {
        patients_seen = seen, unique_patients = seen, new_visits = seen, follow_up_visits = 0, first_time_patients = 0,
        collected, billed = collected, waived = 0, refunded = 0, unpaid_total = 0,
        rx_groups = 0, med_certs = 0, lab_orders = 0, no_shows = 0, cancellations = 0, avg_consult_minutes = (double?)null, avg_consult_samples = 0,
    };

    private static HttpResponseMessage PerfJson(Guid doctorId, decimal collected = 1234.5m) => FakeHandler.Json(new
    {
        doctor_id = doctorId, currency = "PHP", week_starts_on = "monday",
        period = new { name = "week", from = "2026-11-16", to = "2026-11-22" },
        previous_period = new { name = "week", from = "2026-11-09", to = "2026-11-15" },
        current = Stats(collected, 7), previous = Stats(500, 3),
        series = new[] { new { date = "2026-11-21", patients_seen = 7, collected, billed = collected } },
        top_diagnoses = new[] { new { icd10_code = "J06.9", label = "Acute URI", count = 4 } },
        unpaid = new { count = 1, total = 300, items = new[] { new { booking_id = Guid.NewGuid(), patient_id = Guid.NewGuid(), patient_name = "Ana Cruz", appointment_date = "2026-11-20", amount_due = 300 } } },
        follow_ups_due = new { count = 0, items = Array.Empty<object>() },
    });

    private static Task<ApiRig> Rig(bool signedIn = true, bool healthy = true) => ApiRig.StartAsync(s => s.AddOnlineFirstDoctorPerformance(), signedIn, healthy);

    [Fact]
    public async Task Online_reads_from_the_api_with_the_bearer_token_and_maps_the_dto()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/api/doctor/performance" ? PerfJson(rig.DoctorId) : healthy(r);

        var report = await rig.Get<IDoctorPerformanceService>().GetAsync(rig.DoctorId, PerformancePeriodName.Week);

        Assert.NotNull(report);
        Assert.Equal(1234.5m, report!.Current.Collected);
        Assert.Equal(7, report.Current.PatientsSeen);
        Assert.Equal("Acute URI", Assert.Single(report.TopDiagnoses).Label);
        Assert.Equal("Ana Cruz", Assert.Single(report.Unpaid.Items).PatientName);
        var call = rig.Http.Requests.Single(r => r.PathAndQuery.StartsWith("/api/doctor/performance"));
        Assert.Equal("access-1", call.Auth);
        Assert.Contains("period=week", call.PathAndQuery);
        Assert.Contains($"doctorId={rig.DoctorId:D}", call.PathAndQuery);
        Assert.Equal(0, rig.Sync.Calls); // a read never asks for a sync
    }

    [Fact]
    public async Task Offline_health_ping_fails_so_the_local_database_answers_and_the_api_is_never_called()
    {
        await using var rig = await Rig(healthy: false);
        var report = await rig.Get<IDoctorPerformanceService>().GetAsync(rig.DoctorId, PerformancePeriodName.Week);

        Assert.NotNull(report);                                   // answered locally
        Assert.Equal(0m, report!.Current.Collected);              // the demo node has no payments
        Assert.False(rig.Http.Saw("/api/doctor/performance"));
        Assert.False(rig.Connectivity.IsOnline);
    }

    [Fact]
    public async Task Network_failure_in_the_middle_of_the_call_falls_back_to_local_and_flips_offline()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/api/doctor/performance" ? throw new HttpRequestException("connection reset") : healthy(r);
        var changes = new List<bool>();
        rig.Connectivity.Changed += changes.Add;

        var report = await rig.Get<IDoctorPerformanceService>().GetAsync(rig.DoctorId, PerformancePeriodName.Month);

        Assert.NotNull(report);
        Assert.Equal(0m, report!.Current.Collected);              // local answer, not the api's
        Assert.True(rig.Http.Saw("/api/doctor/performance"));     // it did try the api first
        Assert.False(rig.Connectivity.IsOnline);
        Assert.Equal([true, false], changes);                     // health ping said online, then the failure flipped it
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    public async Task Gateway_errors_count_as_offline(HttpStatusCode code)
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/api/doctor/performance" ? FakeHandler.Status(code) : healthy(r);
        var report = await rig.Get<IDoctorPerformanceService>().GetAsync(rig.DoctorId, PerformancePeriodName.Today);
        Assert.Equal(0m, report!.Current.Collected);
        Assert.False(rig.Connectivity.IsOnline);
    }

    [Fact]
    public async Task Timeout_counts_as_offline()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/api/doctor/performance" ? throw new TaskCanceledException("timeout") : healthy(r);
        var report = await rig.Get<IDoctorPerformanceService>().GetAsync(rig.DoctorId, PerformancePeriodName.Today);
        Assert.NotNull(report);
        Assert.False(rig.Connectivity.IsOnline);
    }

    [Fact]
    public async Task A_real_server_answer_is_not_swallowed_a_403_reaches_the_caller()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/api/doctor/performance"
            ? FakeHandler.Text("""{"message":"Doctors can only see their own performance."}""", HttpStatusCode.Forbidden) : healthy(r);
        var ex = await Assert.ThrowsAsync<ApiForbiddenException>(() => rig.Get<IDoctorPerformanceService>().GetAsync(rig.DoctorId, PerformancePeriodName.Week));
        Assert.Equal("Doctors can only see their own performance.", ex.Message);
        Assert.True(rig.Connectivity.IsOnline);
    }

    [Fact]
    public async Task Admin_asking_for_an_unknown_doctor_gets_null_from_a_404()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/api/doctor/performance" ? FakeHandler.Text("""{"message":"Doctor not found"}""", HttpStatusCode.NotFound) : healthy(r);
        Assert.Null(await rig.Get<IDoctorPerformanceService>().GetAsync(Guid.NewGuid(), PerformancePeriodName.Week));
    }

    [Fact]
    public async Task No_api_session_means_offline_style_local_answers_even_when_the_server_is_up()
    {
        await using var rig = await Rig(signedIn: false);
        var report = await rig.Get<IDoctorPerformanceService>().GetAsync(rig.DoctorId, PerformancePeriodName.Week);
        Assert.NotNull(report);
        Assert.False(rig.Http.Saw("/api/doctor/performance"));
    }

    [Fact]
    public async Task Expired_access_token_is_refreshed_once_and_the_call_is_retried()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (p == "/api/auth/refresh-token") return FakeHandler.Json(new { accessToken = "access-2", refreshToken = "refresh-2" }, camel: true);
            if (p == "/api/doctor/performance")
                return r.Headers.Authorization?.Parameter == "access-2" ? PerfJson(rig.DoctorId, 99m) : FakeHandler.Status(HttpStatusCode.Unauthorized);
            return healthy(r);
        };
        var report = await rig.Get<IDoctorPerformanceService>().GetAsync(rig.DoctorId, PerformancePeriodName.Week);
        Assert.Equal(99m, report!.Current.Collected);
        Assert.Equal("access-2", rig.Tokens.AccessToken);
        Assert.Equal(1, rig.Http.Requests.Count(r => r.PathAndQuery == "/api/auth/refresh-token"));
        Assert.Equal(2, rig.Http.Requests.Count(r => r.PathAndQuery.StartsWith("/api/doctor/performance")));
    }

    [Fact]
    public async Task When_the_refresh_token_is_rejected_the_node_carries_on_locally_and_drops_the_api_session()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (p == "/api/auth/refresh-token") return FakeHandler.Text("""{"message":"Invalid refresh token"}""", HttpStatusCode.Unauthorized);
            if (p == "/api/doctor/performance") return FakeHandler.Status(HttpStatusCode.Unauthorized);
            return healthy(r);
        };
        var report = await rig.Get<IDoctorPerformanceService>().GetAsync(rig.DoctorId, PerformancePeriodName.Week);
        Assert.NotNull(report);
        Assert.Equal(0m, report!.Current.Collected);
        Assert.Null(rig.Tokens.AccessToken);
    }

    [Fact]
    public async Task Local_implementation_alone_still_works_without_any_api_registration()
    {
        // the pre-existing local service is untouched: the proxy is only a router on top of it
        await using var rig = await ApiRig.StartAsync(_ => { }, signedIn: false);
        var local = Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<DoctorPerformanceService>(rig.Services);
        Assert.NotNull(await local.GetAsync(rig.DoctorId, PerformancePeriodName.Today));
    }
}
//JJQR - END
