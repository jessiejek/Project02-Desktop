//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.4: suggestions appear on schedule, repeat at most every 15 minutes, and NEVER change the mode
using ClinicNode.Api.Http;
using ClinicNode.Core.Sync;

namespace ClinicNode.Api.Tests;

public class ConnectionAdvisorTests
{
    private sealed class Rig
    {
        public DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);
        public readonly FakeWorkMode Mode;
        public readonly ConnectionMonitor Monitor = new(new Conn(), new NoProbeSource());
        public readonly ConnectionAdvisor Advisor;
        public readonly List<ConnectionSuggestion> Shown = [];

        public Rig(WorkMode mode)
        {
            Mode = new FakeWorkMode(mode);
            Advisor = new ConnectionAdvisor(Monitor, Mode, () => Now);
            Advisor.Suggested += Shown.Add;
        }

        public void Check(bool ok, int ms = 50, string? err = null)
            => Monitor.Record(new HealthSample(Now, ms, ok ? 200 : 503, ok ? true : null, ok ? null : err ?? "unreachable"));

        /// <summary>One check every 15 s for the given time.</summary>
        public void Run(TimeSpan span, bool ok, int ms = 50)
        {
            for (var t = TimeSpan.Zero; t < span; t += TimeSpan.FromSeconds(15)) { Check(ok, ms); Now += TimeSpan.FromSeconds(15); }
        }
    }

    private sealed class Conn : IApiConnectivity
    {
        public bool IsOnline => true;
        public event Action<bool>? Changed { add { } remove { } }
        public Task<bool> EnsureCheckedAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CheckNowAsync(CancellationToken ct = default) => Task.FromResult(true);
        public void ReportNetworkFailure() { } public void ReportSuccess() { } public void Start() { }
    }

    [Fact]
    public void Online_and_Down_for_a_minute_suggests_Offline_but_not_before()
    {
        var r = new Rig(WorkMode.Online);
        r.Run(TimeSpan.FromSeconds(45), ok: false);
        Assert.Empty(r.Shown);                                   // the status turned Down at the 3rd failed check (30 s); only 15 s of Down so far
        r.Run(TimeSpan.FromSeconds(75), ok: false);              // 60 s of Down reached at ~90 s after the first failure
        var s = Assert.Single(r.Shown);
        Assert.Equal(SuggestionKind.SwitchToOffline, s.Kind);
        Assert.Equal("Connection is poor. Switch to Offline mode?", s.Message);
    }

    [Fact]
    public void Online_and_Slow_for_three_minutes_suggests_Offline()
    {
        var r = new Rig(WorkMode.Online);
        r.Run(TimeSpan.FromMinutes(2.5), ok: true, ms: 800);
        Assert.Empty(r.Shown);
        r.Run(TimeSpan.FromMinutes(1), ok: true, ms: 800);
        Assert.Equal(SuggestionKind.SwitchToOffline, Assert.Single(r.Shown).Kind);
    }

    [Fact]
    public void A_good_connection_online_never_suggests_anything()
    {
        var r = new Rig(WorkMode.Online);
        r.Run(TimeSpan.FromHours(1), ok: true, ms: 40);
        Assert.Empty(r.Shown);
    }

    [Fact]
    public void Offline_and_Good_for_five_minutes_suggests_syncing_then_going_Online()
    {
        var r = new Rig(WorkMode.Offline);
        r.Run(TimeSpan.FromMinutes(4.5), ok: true);
        Assert.Empty(r.Shown);
        r.Run(TimeSpan.FromMinutes(1), ok: true);
        var s = Assert.Single(r.Shown);
        Assert.Equal(SuggestionKind.SyncThenGoOnline, s.Kind);
        Assert.Equal("Connection looks good. Sync, then go Online?", s.Message);
    }

    [Fact]
    public void A_bad_moment_restarts_the_clock()
    {
        var r = new Rig(WorkMode.Offline);
        r.Run(TimeSpan.FromMinutes(4), ok: true);
        r.Run(TimeSpan.FromSeconds(45), ok: false);            // not Good any more: the five minutes start again
        r.Run(TimeSpan.FromMinutes(4), ok: true);
        Assert.Empty(r.Shown);
    }

    [Fact]
    public void The_same_suggestion_is_not_repeated_for_fifteen_minutes()
    {
        var r = new Rig(WorkMode.Online);
        r.Run(TimeSpan.FromMinutes(2), ok: false);
        Assert.Single(r.Shown);
        r.Run(TimeSpan.FromMinutes(13), ok: false);            // still down, 15 min not over
        Assert.Single(r.Shown);
        r.Run(TimeSpan.FromMinutes(3), ok: false);
        Assert.Equal(2, r.Shown.Count);
    }

    [Fact]
    public void The_work_mode_is_identical_before_and_after_every_suggestion()
    {
        var down = new Rig(WorkMode.Online);
        down.Run(TimeSpan.FromMinutes(20), ok: false);
        Assert.NotEmpty(down.Shown);
        Assert.Equal(WorkMode.Online, down.Mode.Mode);          // it only suggested

        var good = new Rig(WorkMode.Offline);
        good.Run(TimeSpan.FromMinutes(20), ok: true);
        Assert.NotEmpty(good.Shown);
        Assert.Equal(WorkMode.Offline, good.Mode.Mode);
    }

    [Fact]
    public void Switching_the_mode_restarts_the_clock()
    {
        var r = new Rig(WorkMode.Online);
        r.Run(TimeSpan.FromSeconds(45), ok: false);
        _ = r.Mode.SetAsync(WorkMode.Offline).Result;           // the doctor switched by hand
        r.Run(TimeSpan.FromMinutes(2), ok: false);
        Assert.Empty(r.Shown);                                  // Offline + Down: nothing to suggest
    }
}
//JJQR - END
