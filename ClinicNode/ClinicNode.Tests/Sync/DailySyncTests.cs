//JJQR - AI (Nov 22, 2026) - offline-mode plan step 3.3 (D5): the first sign-in of the day syncs; "done today" only when nothing is left waiting
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;

namespace ClinicNode.Tests.Sync;

public class DailySyncTests
{
    private static readonly DateOnly Today = new(2026, 11, 22);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("garbage", true)]
    [InlineData("2026-11-22", false)]   // done today
    [InlineData("2026-11-21", true)]    // done yesterday: today is new
    [InlineData("2026-11-23", true)]    // a stored date later than today (the clock or server date went back) counts as not done
    public void The_stored_day_decides(string? stored, bool needed) => Assert.Equal(needed, DailySyncRules.Needs(stored, Today));

    private static async Task AddChangeAsync(SyncTestHost host)
    {
        await host.LoginAsync();
        await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Daily", "Sync", new DateOnly(1990, 1, 1), SexType.Female));
    }

    [Fact]
    public async Task A_fresh_computer_needs_the_sync_and_does_not_after_a_run_that_left_nothing_waiting()
    {
        await using var host = await SyncTestHost.StartAsync();
        host.Cloud.ServerNow = DateTimeOffset.UtcNow;
        var svc = host.Get<ISyncService>();
        await AddChangeAsync(host);
        Assert.Equal(DailySyncDecision.Needed, await svc.GetDailySyncDecisionAsync());

        var r = await svc.RunManualAsync("daily");
        Assert.True(r.Succeeded, r.Error);
        Assert.Equal(0, r.StillWaiting);
        Assert.Equal(DailySyncDecision.NotNeeded, await svc.GetDailySyncDecisionAsync());
    }

    [Fact]
    public async Task A_run_that_left_changes_waiting_does_not_count_as_done_today()
    {
        await using var host = await SyncTestHost.StartAsync();
        host.Cloud.ServerNow = DateTimeOffset.UtcNow;
        var svc = host.Get<ISyncService>();
        await AddChangeAsync(host);
        host.Cloud.OnPush = op => host.Cloud.Applied(op, "retry");      // the server asks us to try again later for everything

        var r = await svc.RunManualAsync("daily");
        Assert.True(r.StillWaiting > 0);
        Assert.Equal(DailySyncDecision.Needed, await svc.GetDailySyncDecisionAsync());
    }

    [Fact]
    public async Task A_failed_run_does_not_count_as_done_today()
    {
        await using var host = await SyncTestHost.StartAsync();
        host.Cloud.ServerNow = DateTimeOffset.UtcNow;
        var svc = host.Get<ISyncService>();
        await AddChangeAsync(host);
        host.Cloud.Offline = true;
        Assert.False((await svc.RunManualAsync("daily")).Succeeded);
        host.Cloud.Offline = false;
        Assert.Equal(DailySyncDecision.Needed, await svc.GetDailySyncDecisionAsync());
    }

    [Fact]
    public async Task A_date_stored_in_the_future_is_treated_as_not_done()
    {
        await using var host = await SyncTestHost.StartAsync();
        host.Cloud.ServerNow = DateTimeOffset.UtcNow;
        var svc = host.Get<ISyncService>();
        await svc.RunManualAsync("daily");
        Assert.Equal(DailySyncDecision.NotNeeded, await svc.GetDailySyncDecisionAsync());
        await using (var db = await host.DbAsync())
            await SyncLocalStore.SetStateAsync(db, DailySyncRules.StateKey, DailySyncRules.Format(ClinicApp.Domain.ClinicClock.Today.AddDays(3)));
        Assert.Equal(DailySyncDecision.Needed, await svc.GetDailySyncDecisionAsync());
    }

    [Fact]
    public async Task An_unregistered_or_unconfigured_computer_does_not_run_it()
    {
        await using (var host = await SyncTestHost.StartAsync(registered: false))
            Assert.Equal(DailySyncDecision.NotRegistered, await host.Get<ISyncService>().GetDailySyncDecisionAsync());
        await using (var host = await SyncTestHost.StartAsync(baseUrl: ""))
            Assert.Equal(DailySyncDecision.NotConfigured, await host.Get<ISyncService>().GetDailySyncDecisionAsync());
    }

    [Fact]
    public async Task The_manual_run_event_fires_with_the_result()
    {
        await using var host = await SyncTestHost.StartAsync();
        host.Cloud.ServerNow = DateTimeOffset.UtcNow;
        var svc = (SyncService)host.Get<ISyncService>();
        SyncCycleResult? seen = null;
        svc.ManualRunCompleted += r => seen = r;
        await svc.RunManualAsync("manual");
        Assert.NotNull(seen);
        Assert.True(seen!.Succeeded);
    }
}
//JJQR - END
