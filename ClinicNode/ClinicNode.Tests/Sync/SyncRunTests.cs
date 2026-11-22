//JJQR - AI (Nov 22, 2026) - offline-mode plan step 3.1: an awaited manual sync run with checks first and a full result
using System.Text.Json.Nodes;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Visits;
using ClinicNode.Sync;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Sync;

public class SyncRunTests
{
    private static async Task MakeVisitAsync(SyncTestHost host, string last)
    {
        var session = await host.LoginAsync();
        var patient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Run", last, new DateOnly(1985, 3, 1), SexType.Female));
        await host.Get<IVisitStarter>().StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        for (var i = 0; i < 3; i++)   // a few more changes so there is something to count and a "half way"
            await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Extra" + i, last, new DateOnly(1990, 1, 1 + i), SexType.Male));
    }

    [Fact]
    public async Task A_manual_run_sends_everything_and_reports_every_count()
    {
        await using var host = await SyncTestHost.StartAsync();
        await MakeVisitAsync(host, "One");
        var waiting = await host.PendingAsync();
        Assert.True(waiting >= 3);

        var r = await host.Get<ISyncService>().RunManualAsync();

        Assert.True(r.Succeeded, r.Error);
        Assert.Equal(waiting, r.Pushed);
        Assert.Equal(waiting, r.Accepted);            // the fake server answers "applied" to all
        Assert.Equal(0, r.Merged); Assert.Equal(0, r.Conflicts); Assert.Equal(0, r.Rejected);
        Assert.Equal(0, r.StillWaiting);
        Assert.True(r.FinishedAt >= r.StartedAt);
        Assert.Equal(0, await host.PendingAsync());
    }

    [Fact]
    public async Task Merged_conflict_and_rejected_answers_are_counted_apart_and_become_review_items()
    {
        await using var host = await SyncTestHost.StartAsync();
        await MakeVisitAsync(host, "Two");
        var n = 0;
        host.Cloud.OnPush = op => (n++ % 3) switch
        {
            0 => host.Cloud.Applied(op, "merged", "queue_number_reassigned"),
            1 => host.Cloud.Applied(op, "rejected", "invalid_payload"),
            _ => host.Cloud.Applied(op, "applied"),
        };

        var r = await host.Get<ISyncService>().RunManualAsync();

        Assert.True(r.Succeeded, r.Error);
        Assert.True(r.Merged >= 1 && r.Rejected >= 1 && r.Accepted >= 1, $"merged {r.Merged} rejected {r.Rejected} accepted {r.Accepted}");
        Assert.Equal(r.Merged + r.Rejected + r.Conflicts, r.NewReviewItems);   // each of those is on the review list
    }

    [Fact]
    public async Task An_unregistered_computer_is_told_so_and_nothing_is_sent()
    {
        await using var host = await SyncTestHost.StartAsync(registered: false);
        await MakeVisitAsync(host, "Three");
        var r = await host.Get<ISyncService>().RunManualAsync();
        Assert.False(r.Succeeded);
        Assert.Equal(SyncFailureKind.NotRegistered, r.Failure);
        Assert.Contains("registered", r.Error);
        Assert.DoesNotContain(host.Cloud.Requests, q => q.RequestUri!.AbsolutePath.StartsWith("/api/sync/push"));
        Assert.True(await host.PendingAsync() > 0);       // everything is still safely here
    }

    [Fact]
    public async Task When_the_server_does_not_answer_nothing_is_sent_and_the_changes_stay()
    {
        await using var host = await SyncTestHost.StartAsync();
        await MakeVisitAsync(host, "Four");
        var waiting = await host.PendingAsync();
        host.Cloud.Offline = true;
        var r = await host.Get<ISyncService>().RunManualAsync();
        Assert.False(r.Succeeded);
        Assert.Equal(SyncFailureKind.Offline, r.Failure);
        Assert.Contains("safe on this computer", r.Error);
        Assert.Equal(waiting, await host.PendingAsync());
    }

    [Fact]
    public async Task A_run_that_is_cancelled_half_way_is_simply_continued_next_time_without_duplicates()
    {
        await using var host = await SyncTestHost.StartAsync(configure: o => o.PushBatchSize = 1);   // one change per batch so there is a "half way"
        await MakeVisitAsync(host, "Five");
        var total = await host.PendingAsync();
        Assert.True(total >= 3);
        using var cts = new CancellationTokenSource();
        var seen = 0;
        host.Cloud.OnPush = op => { if (++seen == 1) cts.Cancel(); return host.Cloud.Applied(op); };   // cancel while the first change is being sent

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Get<ISyncService>().RunManualAsync(cts.Token));
        var left = await host.PendingAsync();
        Assert.InRange(left, 1, total);                                   // something may have gone, nothing was lost

        host.Cloud.OnPush = null;
        var again = await host.Get<ISyncService>().RunManualAsync();
        Assert.True(again.Succeeded, again.Error);
        Assert.Equal(0, await host.PendingAsync());
        var ids = host.Cloud.PushedOperations.Select(o => o["operation_id"]!.GetValue<string>()).ToList();
        Assert.True(ids.Count >= total);
        Assert.Equal(total, ids.Distinct().Count());                      // every change reached the server; a repeat is the same operation id, which the server treats as already known
    }
}
//JJQR - END
