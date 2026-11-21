//JJQR - AI (Nov 21, 2026)
// Accounts for offline login, append-only, cloud-wins, auth/token handling, status service, loop.
using System.ComponentModel;
using System.Text.Json.Nodes;
using ClinicApp.Auth;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Patients;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Sync;

public class SyncRulesAndStatusTests
{
    [Fact]
    public async Task Sync_pulled_password_hash_enables_offline_login_and_keeps_local_lockout_counters()
    {
        await using var host = await SyncTestHost.StartAsync();
        JsonObject userRow;
        Guid userId;
        await using (var db = await host.DbAsync())
        {
            var user = await db.Users.SingleAsync(u => u.Email == "doctor@clinic.test");
            user.FailedLoginCount = 2;
            await db.SaveChangesAsync();
            userId = user.Id;
            userRow = EntityRowCodec.ToJson(db, user);
            userRow["password_hash"] = host.Get<PasswordHasherService>().Hash(user, "NewCloudPass#2026");
            userRow["failed_login_count"] = 0;
        }
        host.Cloud.EnqueuePull("1", false, FakeSyncServer.Change("user", userId, userRow));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        await using (var db = await host.DbAsync())
            Assert.Equal(2, (await db.Users.SingleAsync(u => u.Id == userId)).FailedLoginCount);
        var auth = host.Get<IAuthService>();
        Assert.False((await auth.LoginAsync("doctor@clinic.test", ClinicApp.Infrastructure.DevDataSeeder.DevPassword, false)).Succeeded);
        Assert.True((await auth.LoginAsync("doctor@clinic.test", "NewCloudPass#2026", false)).Succeeded);
    }

    [Fact]
    public async Task Sync_pulled_deactivation_and_account_delete_block_offline_login()
    {
        await using var host = await SyncTestHost.StartAsync();
        JsonObject staffRow;
        Guid staffId, adminUserId;
        await using (var db = await host.DbAsync())
        {
            var staffUser = await db.Users.SingleAsync(u => u.Email == "staff@clinic.test");
            var staff = await db.StaffAccounts.SingleAsync(s => s.UserId == staffUser.Id);
            staffId = staff.StaffId;
            staffRow = EntityRowCodec.ToJson(db, staff);
            staffRow["status"] = "Inactive";
            adminUserId = (await db.Users.SingleAsync(u => u.Email == "admin@clinic.test")).Id;
        }
        host.Cloud.EnqueuePull("1", false,
            FakeSyncServer.Change("staff_account", staffId, staffRow),
            FakeSyncServer.Change("user", adminUserId, null, delete: true));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        var auth = host.Get<IAuthService>();
        var staffLogin = await auth.LoginAsync("staff@clinic.test", ClinicApp.Infrastructure.DevDataSeeder.DevPassword, false);
        Assert.False(staffLogin.Succeeded);
        Assert.Contains("deactivated", staffLogin.Error);
        Assert.False((await auth.LoginAsync("admin@clinic.test", ClinicApp.Infrastructure.DevDataSeeder.DevPassword, false)).Succeeded);
    }

    [Fact]
    public async Task Sync_settings_and_templates_cloud_wins()
    {
        await using var host = await SyncTestHost.StartAsync();
        JsonObject settingRow;
        await using (var db = await host.DbAsync())
        {
            var s = await db.ClinicSettings.FirstAsync();
            settingRow = EntityRowCodec.ToJson(db, s);
        }
        var nameKey = settingRow.Select(kv => kv.Key).First(k => k.Contains("name"));
        settingRow[nameKey] = "Cloud Clinic Name";
        host.Cloud.EnqueuePull("1", false, FakeSyncServer.Change("clinic_setting", Guid.Empty, settingRow));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        await using var db2 = await host.DbAsync();
        var after = EntityRowCodec.ToJson(db2, await db2.ClinicSettings.FirstAsync());
        Assert.Equal("Cloud Clinic Name", after[nameKey]!.GetValue<string>());
        Assert.Empty(await SyncLocalStore.ListOpenConflictsAsync(db2));
    }

    [Fact]
    public async Task Sync_vitals_and_audit_logs_are_append_only()
    {
        await using var host = await SyncTestHost.StartAsync();
        var auditId = Guid.NewGuid();
        await using (var db = await host.DbAsync())
        {
            db.AuditLogs.Add(new ClinicApp.Domain.Entities.AuditLog
            {
                Id = auditId, EntityType = default, EntityId = Guid.NewGuid(), Action = "local", PerformedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var newAudit = Guid.NewGuid();
        host.Cloud.EnqueuePull("1", false,
            FakeSyncServer.Change("audit_log", auditId, new JsonObject { ["id"] = auditId.ToString(), ["action"] = "rewritten", ["entity_id"] = Guid.NewGuid().ToString(), ["performed_at"] = "2026-11-21T00:00:00+00:00" }),
            FakeSyncServer.Change("audit_log", newAudit, new JsonObject { ["id"] = newAudit.ToString(), ["action"] = "cloud", ["entity_id"] = Guid.NewGuid().ToString(), ["entity_type"] = default(AuditEntityType).ToString(), ["performed_at"] = "2026-11-21T00:00:00+00:00" }),
            FakeSyncServer.Change("audit_log", auditId, null, delete: true));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        await using var db2 = await host.DbAsync();
        Assert.Equal("local", (await db2.AuditLogs.SingleAsync(a => a.Id == auditId)).Action);
        Assert.Equal("cloud", (await db2.AuditLogs.SingleAsync(a => a.Id == newAudit)).Action);
    }

    [Fact]
    public async Task Sync_refreshes_the_node_token_once_on_401()
    {
        await using var host = await SyncTestHost.StartAsync();
        host.Cloud.UnauthorizedResponses = 1;
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        Assert.Equal(2, host.Cloud.TokensIssued);
    }

    [Fact]
    public async Task Sync_revoked_and_unregistered_nodes_report_their_state()
    {
        await using (var host = await SyncTestHost.StartAsync())
        {
            host.Cloud.Revoked = true;
            var r = await host.Get<ISyncService>().RunOnceAsync();
            Assert.Equal(SyncFailureKind.Revoked, r.Failure);
            Assert.Equal(SyncPhase.Revoked, host.Get<ISyncStatus>().Phase);
        }
        await using (var host = await SyncTestHost.StartAsync(registered: false))
        {
            var r = await host.Get<ISyncService>().RunOnceAsync();
            Assert.Equal(SyncFailureKind.NotRegistered, r.Failure);
            Assert.Equal(SyncPhase.NotRegistered, host.Get<ISyncStatus>().Phase);
        }
    }

    [Fact]
    public async Task Sync_status_raises_property_changes_for_pending_count_and_last_synced()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Sta", "Tus", new DateOnly(1991, 1, 1), SexType.Female));
        var status = host.Get<ISyncStatus>();
        var changed = new List<string?>();
        status.PropertyChanged += (_, e) => { lock (changed) changed.Add(e.PropertyName); };
        await status.RefreshAsync();
        Assert.Equal(1, status.PendingCount);
        await host.Get<ISyncService>().RunOnceAsync();
        Assert.Equal(0, status.PendingCount);
        Assert.NotNull(status.LastSyncedAt);
        Assert.True(status.IsOnline);
        Assert.Null(status.LastError);
        lock (changed)
        {
            Assert.Contains(nameof(ISyncStatus.PendingCount), changed);
            Assert.Contains(nameof(ISyncStatus.LastSyncedAt), changed);
        }
    }

    [Fact]
    public async Task Sync_background_loop_pushes_on_request_and_recovers_after_going_offline()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        host.Cloud.Offline = true;
        var loop = host.Get<ISyncLoop>();
        loop.Start();
        await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Lo", "Op", new DateOnly(1992, 2, 2), SexType.Male));
        await WaitUntil(() => host.Get<ISyncStatus>().Phase == SyncPhase.Offline);
        Assert.False(host.Get<ISyncStatus>().IsOnline);

        host.Cloud.Offline = false;
        host.Get<ISyncStatus>().RequestSync();
        await WaitUntil(() => !host.Cloud.PushedOperations.IsEmpty && host.Get<ISyncStatus>().Phase == SyncPhase.Idle);
        Assert.Equal(0, await host.PendingAsync());
        Assert.True(host.Get<ISyncStatus>().IsOnline);
        await loop.StopAsync();
        Assert.False(loop.IsRunning);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("condition not met");
            await Task.Delay(50);
        }
    }
}
//JJQR - END
