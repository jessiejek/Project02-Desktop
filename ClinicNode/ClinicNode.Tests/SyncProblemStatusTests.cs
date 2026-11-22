//JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.5: the "Sync problem" state
using ClinicApp.Infrastructure;
using ClinicNode.Data;
using ClinicNode.Sync;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests;

public class SyncProblemStatusTests
{
    [Theory]
    [InlineData(SyncPhase.Idle, 0, false)]
    [InlineData(SyncPhase.Offline, 0, false)]       // server unreachable is Offline, never a problem
    [InlineData(SyncPhase.Syncing, 0, false)]
    [InlineData(SyncPhase.NotRegistered, 0, false)] // has its own wording
    [InlineData(SyncPhase.Error, 0, true)]
    [InlineData(SyncPhase.Revoked, 0, true)]
    [InlineData(SyncPhase.NeedsUpdate, 0, true)]
    [InlineData(SyncPhase.Idle, 1, true)]           // something stuck
    [InlineData(SyncPhase.Offline, 2, true)]
    public void HasSyncProblem_follows_the_plan(SyncPhase phase, int stuck, bool expected)
        => Assert.Equal(expected, new SyncStatus { Phase = phase, StuckCount = stuck }.HasSyncProblem);

    [Fact]
    public void Banner_names_the_problem_when_something_is_stuck()
    {
        var banner = new SyncStatus { Phase = SyncPhase.Idle, StuckCount = 2, PendingCount = 2 }.Banner;
        Assert.StartsWith("Sync problem", banner);
        Assert.Contains("2 changes", banner);
    }

    [Fact]
    public async Task A_change_the_server_keeps_refusing_becomes_a_sync_problem_after_ten_tries()
    {
        await using var host = await TestHost.StartAsync();
        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        var outbox = host.Get<ISyncOutbox>();
        var status = host.Get<ISyncStatus>();
        await using var db = await factory.CreateDbContextAsync();
        var id = Guid.NewGuid();
        await outbox.EnqueueAsync(db, new SyncOutboxEntry { OperationId = id, Aggregate = "consultation", EntityId = Guid.NewGuid(), OperationType = "update", PayloadJson = "{}", CreatedAt = DateTimeOffset.UtcNow });

        for (var i = 0; i < SyncOutbox.StuckAttempts - 1; i++) await outbox.MarkAttemptAsync(db, id, "retry");
        await status.RefreshAsync();
        Assert.Equal(0, status.StuckCount);                                      // 9 tries: still just waiting
        Assert.False(status.HasSyncProblem);

        await outbox.MarkAttemptAsync(db, id, "retry");                          // the 10th
        await status.RefreshAsync();
        Assert.Equal(1, status.StuckCount);
        Assert.True(status.HasSyncProblem);

        await outbox.MarkAckedAsync(db, id);                                     // once it goes through, the problem is gone
        await status.RefreshAsync();
        Assert.Equal(0, status.StuckCount);
        Assert.False(status.HasSyncProblem);
    }
}
//JJQR - END
