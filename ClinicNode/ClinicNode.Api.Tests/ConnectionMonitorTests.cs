//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.3: scripted latencies and failures give exactly the expected word (thresholds = Q6)
using System.Net;
using ClinicNode.Api.Http;

namespace ClinicNode.Api.Tests;

public class ConnectionMonitorTests
{
    private static HealthSample Ok(int ms) => new(DateTimeOffset.UtcNow, ms, 200, true, null);
    private static HealthSample Fail(string kind = "unreachable") => new(DateTimeOffset.UtcNow, 2000, null, null, kind);
    private static IReadOnlyList<HealthSample> Seq(params HealthSample[] s) => s;
    private static IEnumerable<HealthSample> Many(int n, int ms) => Enumerable.Range(0, n).Select(_ => Ok(ms));

    [Fact] public void No_checks_yet_is_Unknown() => Assert.Equal(ConnectionQuality.Unknown, ConnectionRules.Evaluate([]).Quality);

    [Fact] public void Ten_fast_checks_are_Good() => Assert.Equal(ConnectionQuality.Good, ConnectionRules.Evaluate(Many(10, 80).ToList()).Quality);

    [Theory]
    [InlineData(299, ConnectionQuality.Good)]
    [InlineData(300, ConnectionQuality.Slow)]
    [InlineData(1500, ConnectionQuality.Slow)]
    public void Latency_decides_Good_or_Slow(int ms, ConnectionQuality expected) => Assert.Equal(expected, ConnectionRules.Evaluate(Many(10, ms).ToList()).Quality);

    [Fact]
    public void One_slow_reply_among_fast_ones_stays_Good_because_the_typical_time_counts()
        => Assert.Equal(ConnectionQuality.Good, ConnectionRules.Evaluate(Many(9, 90).Append(Ok(2000)).ToList()).Quality);

    [Fact]
    public void One_failed_check_in_the_last_ten_is_Slow()
        => Assert.Equal(ConnectionQuality.Slow, ConnectionRules.Evaluate(Many(4, 80).Append(Fail()).Concat(Many(5, 80)).ToList()).Quality);

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Two_to_four_failures_not_in_a_row_are_Unstable(int failures)
    {
        var s = new List<HealthSample>();
        for (var i = 0; i < 10; i++) s.Add(i % 2 == 1 && failures-- > 0 ? Fail() : Ok(80));   // F at 1,3,5,7: never three in a row
        Assert.Equal(ConnectionQuality.Unstable, ConnectionRules.Evaluate(s).Quality);
    }

    [Fact]
    public void Three_failures_in_a_row_are_Down()
    {
        var r = ConnectionRules.Evaluate(Many(7, 80).Concat([Fail(), Fail(), Fail()]).ToList());
        Assert.Equal(ConnectionQuality.Down, r.Quality);
        Assert.Contains("3 failed checks in a row", r.Reason);
    }

    [Fact]
    public void Two_failures_in_a_row_are_not_yet_Down()
        => Assert.Equal(ConnectionQuality.Unstable, ConnectionRules.Evaluate(Many(8, 80).Concat([Fail(), Fail()]).ToList()).Quality);

    [Fact]
    public void Five_failures_in_the_last_ten_are_Down_even_if_never_three_in_a_row()
    {
        var s = new List<HealthSample>();
        for (var i = 0; i < 10; i++) s.Add(i % 2 == 0 ? Fail() : Ok(80));   // F O F O F O F O F O = 5 failures, the newest is OK
        Assert.Equal(ConnectionQuality.Down, ConnectionRules.Evaluate(s).Quality);
    }

    [Fact]
    public void Database_not_ready_is_Down_at_once_with_that_reason()
    {
        var r = ConnectionRules.Evaluate(Many(9, 80).Append(Fail("database_not_ready")).ToList());
        Assert.Equal(ConnectionQuality.Down, r.Quality);
        Assert.Contains("Database not ready", r.Reason);
    }

    [Fact]
    public void Only_the_last_ten_count_so_an_old_outage_is_forgotten()
        => Assert.Equal(ConnectionQuality.Good, ConnectionRules.Evaluate(Many(3, 80).Select(_ => Fail()).Concat(Many(10, 80)).ToList()).Quality);

    [Fact]
    public void The_monitor_keeps_60_samples_and_raises_Changed()
    {
        var m = new ConnectionMonitor(new Conn(), new NoProbeSource());
        var raised = 0; m.Changed += () => raised++;
        for (var i = 0; i < 75; i++) m.Record(Ok(i));
        Assert.Equal(60, m.Samples.Count);
        Assert.Equal(15, m.Samples[0].Ms);          // the oldest 15 were dropped
        Assert.Equal(74, m.Samples[^1].Ms);
        Assert.Equal(75, raised);
        Assert.Equal(ConnectionQuality.Good, m.Status.Quality);
    }

    [Fact]
    public void It_checks_every_15_seconds_and_every_5_while_the_panel_is_open()
    {
        var m = new ConnectionMonitor(new Conn(), new NoProbeSource());
        Assert.Equal(TimeSpan.FromSeconds(15), m.CheckInterval);
        m.PanelOpen = true;
        Assert.Equal(TimeSpan.FromSeconds(5), m.CheckInterval);
        m.PanelOpen = false;
        Assert.Equal(TimeSpan.FromSeconds(15), m.CheckInterval);
    }

    // ── the real health check leaves the right samples ────────────────────────
    [Fact]
    public async Task A_real_check_records_the_time_status_and_the_reason_for_each_kind_of_failure()
    {
        var http = new FakeHandler();
        var conn = new ApiConnectivity(new ApiOptions { BaseUrl = () => "http://api.test", HttpHandlerFactory = () => http, AutoStart = false });
        var m = new ConnectionMonitor(conn, conn);

        http.Route = _ => FakeHandler.Status(HttpStatusCode.OK);
        await conn.CheckNowAsync();
        http.Route = _ => FakeHandler.Status(HttpStatusCode.ServiceUnavailable);       // /health/ready: database not answering
        await conn.CheckNowAsync();
        http.Route = _ => throw new HttpRequestException("refused");
        await conn.CheckNowAsync();
        http.Route = _ => throw new TaskCanceledException("slow");
        await conn.CheckNowAsync();

        Assert.Equal([null, "database_not_ready", "unreachable", "timeout"], m.Samples.Select(s => s.ErrorKind).ToArray());
        Assert.Equal(200, m.Samples[0].HttpStatus);
        Assert.Equal(true, m.Samples[0].DatabaseOk);
        Assert.Equal(503, m.Samples[1].HttpStatus);
        Assert.Equal(false, m.Samples[1].DatabaseOk);
        conn.Dispose(); m.Dispose();
    }

    private sealed class Conn : IApiConnectivity
    {
        public bool IsOnline => true;
        public event Action<bool>? Changed { add { } remove { } }
        public Task<bool> EnsureCheckedAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CheckNowAsync(CancellationToken ct = default) => Task.FromResult(true);
        public void ReportNetworkFailure() { }
        public void ReportSuccess() { }
        public void Start() { }
    }
}
//JJQR - END
