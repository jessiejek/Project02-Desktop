// JJQR - AI (Nov 21, 2026)
using ClinicApp.Infrastructure;
using ClinicNode.Data;
using ClinicNode.Sync;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests;

public class SyncOutboxTests
{
    [Fact]
    public async Task Outbox_enqueue_is_idempotent_by_operation_id()
    {
        await using var host = await TestHost.StartAsync();
        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        var outbox = host.Get<ISyncOutbox>();
        await using var db = await factory.CreateDbContextAsync();
        var id = Guid.NewGuid();
        var entry = new SyncOutboxEntry
        {
            OperationId = id,
            Aggregate = "consultation",
            EntityId = Guid.NewGuid(),
            OperationType = "update",
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await outbox.EnqueueAsync(db, entry);
        await outbox.EnqueueAsync(db, entry);
        var pending = await outbox.ListPendingAsync(db);
        Assert.Equal(1, pending.Count(p => p.OperationId == id));
        await outbox.MarkAckedAsync(db, id);
        Assert.DoesNotContain((await outbox.ListPendingAsync(db)), p => p.OperationId == id);
    }

    [Fact]
    public async Task Sync_loop_leaves_pending_rows_when_the_cloud_api_is_not_configured()
    {
        await using var host = await TestHost.StartAsync();
        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        var outbox = host.Get<ISyncOutbox>();
        await using var db = await factory.CreateDbContextAsync();
        await outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "patient",
            EntityId = Guid.NewGuid(),
            OperationType = "create",
            PayloadJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        var sync = host.Get<ISyncService>();
        await sync.RunOnceAsync();
        var status = await sync.RefreshStatusAsync();
        Assert.True(status.PendingCount >= 1);
        Assert.NotEqual(SyncPhase.Syncing, status.Phase);
        Assert.True(status.Banner.Contains("pending", StringComparison.OrdinalIgnoreCase) || status.Banner.Contains("waiting to sync", StringComparison.OrdinalIgnoreCase), status.Banner);
    }
}
// JJQR - END
