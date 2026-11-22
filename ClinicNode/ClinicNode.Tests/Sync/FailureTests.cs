//JJQR - AI (Nov 22, 2026) - offline-mode plan step 5.1 (the failures that can be simulated exactly): nothing is lost, nothing is sent twice by accident, the next run finishes the job
using System.Net;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Sync;

namespace ClinicNode.Tests.Sync;

public class FailureTests
{
    private static async Task<int> AddChangesAsync(SyncTestHost host, string last, int n = 6)
    {
        await host.LoginAsync();
        for (var i = 0; i < n; i++)
            await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Fail" + i, last, new DateOnly(1990, 1, 1 + i), SexType.Female));
        return await host.PendingAsync();
    }

    [Fact]
    public async Task The_server_dies_half_way_through_sending_then_the_next_run_sends_the_rest_exactly_once()
    {
        await using var host = await SyncTestHost.StartAsync(configure: o => o.PushBatchSize = 1);
        var total = await AddChangesAsync(host, "Halfway");
        var seen = 0;
        host.Cloud.OnPush = op => { var r = host.Cloud.Applied(op); if (++seen == 2) host.Cloud.Offline = true; return r; };   // the network drops right after the 2nd change is accepted
        var svc = host.Get<ISyncService>();

        var first = await svc.RunManualAsync();
        Assert.False(first.Succeeded);
        Assert.Equal(total - 2, await host.PendingAsync());                    // two went through, the rest are safely waiting

        host.Cloud.Offline = false; host.Cloud.OnPush = null;
        var second = await svc.RunManualAsync();
        Assert.True(second.Succeeded, second.Error);
        Assert.Equal(0, await host.PendingAsync());
        var ids = host.Cloud.PushedOperations.Select(o => o["operation_id"]!.GetValue<string>()).ToList();
        Assert.Equal(total, ids.Distinct().Count());                           // every change reached the server
        Assert.Equal(ids.Count, ids.Distinct().Count());                       // and none was sent a second time
    }

    [Fact]
    public async Task The_server_stops_answering_after_the_push_so_the_pull_fails_and_nothing_is_sent_again_later()
    {
        await using var host = await SyncTestHost.StartAsync();
        var total = await AddChangesAsync(host, "Pullfail");
        host.Cloud.PullStatus = HttpStatusCode.ServiceUnavailable;
        var svc = host.Get<ISyncService>();

        var first = await svc.RunManualAsync();
        Assert.False(first.Succeeded);                                         // the run did not finish...
        Assert.Equal(0, await host.PendingAsync());                            // ...but everything it pushed is acknowledged
        Assert.Equal(DailySyncDecision.Needed, await svc.GetDailySyncDecisionAsync());   // and the day is NOT marked as synced

        host.Cloud.PullStatus = null;
        var second = await svc.RunManualAsync();
        Assert.True(second.Succeeded, second.Error);
        Assert.Equal(0, second.Pushed);                                        // nothing to send again
        Assert.Equal(total, host.Cloud.PushedOperations.Select(o => o["operation_id"]!.GetValue<string>()).Distinct().Count());
    }

    [Fact]
    public async Task A_computer_clock_one_day_ahead_makes_the_morning_sync_run_again_not_skip()
    {
        await using var host = await SyncTestHost.StartAsync();
        var svc = host.Get<ISyncService>();
        host.Cloud.ServerNow = DateTimeOffset.UtcNow.AddDays(1);               // the server says it is already tomorrow
        await svc.RunManualAsync();
        // "done" was recorded for the server's tomorrow; this computer's today is different, so it is not treated as done
        Assert.Equal(DailySyncDecision.Needed, await svc.GetDailySyncDecisionAsync());
    }

    [Fact]
    public async Task A_computer_clock_one_day_behind_does_not_skip_the_morning_sync_either()
    {
        await using var host = await SyncTestHost.StartAsync();
        var svc = host.Get<ISyncService>();
        host.Cloud.ServerNow = DateTimeOffset.UtcNow.AddDays(-1);
        await svc.RunManualAsync();
        Assert.Equal(DailySyncDecision.Needed, await svc.GetDailySyncDecisionAsync());
        host.Cloud.ServerNow = DateTimeOffset.UtcNow;                          // once the clocks agree again, a good run marks today as done
        await svc.RunManualAsync();
        Assert.Equal(DailySyncDecision.NotNeeded, await svc.GetDailySyncDecisionAsync());
    }

    [Fact]
    public async Task A_refused_login_is_not_the_same_as_a_dead_server_the_work_stays_queued_either_way()
    {
        await using var host = await SyncTestHost.StartAsync();
        var total = await AddChangesAsync(host, "Auth");
        host.Cloud.Revoked = true;                                             // an admin revoked this computer
        var r = await host.Get<ISyncService>().RunManualAsync();
        Assert.False(r.Succeeded);
        Assert.Equal(total, await host.PendingAsync());                        // nothing lost, nothing sent
    }
}
//JJQR - END
