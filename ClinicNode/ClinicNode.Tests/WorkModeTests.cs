//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.1: work mode switch, persistence across a restart, who may switch, audit row
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Sync;
using ClinicNode.Sync;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests;

public class WorkModeTests
{
    private sealed class Blocked(string why) : ISyncBridge
    {
        public void RequestSync() { }
        public Task<bool> IsRevokedAsync(CancellationToken ct = default) => Task.FromResult(false);
        public Task<string?> LocalSaveBlockedReasonAsync(CancellationToken ct = default) => Task.FromResult<string?>(why);
    }

    // no sync bridge here: these tests are about the switch itself (the unregistered-computer refusal has its own test below)
    private static WorkModeService Make(TestHost host) => new(host.Get<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>(), host.Get<IAuthService>());

    [Fact]
    public async Task Starts_online_and_a_doctor_can_switch_both_ways()
    {
        await using var host = await TestHost.StartAsync();
        var mode = Make(host);
        await mode.InitializeAsync();
        Assert.Equal(WorkMode.Online, mode.Mode);
        await host.LoginDoctorAsync();
        Assert.True(mode.CanSwitch);

        var raised = 0;
        mode.Changed += (_, _) => raised++;
        Assert.True((await mode.SetAsync(WorkMode.Offline)).Succeeded);
        Assert.Equal(WorkMode.Offline, mode.Mode);
        Assert.True((await mode.SetAsync(WorkMode.Online)).Succeeded);
        Assert.Equal(WorkMode.Online, mode.Mode);
        Assert.Equal(2, raised);
        Assert.True((await mode.SetAsync(WorkMode.Online)).Succeeded);   // already there: nothing happens
        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task The_mode_survives_a_restart()
    {
        await using var host = await TestHost.StartAsync();
        var doctor = await host.LoginDoctorAsync();
        var first = Make(host);
        await first.InitializeAsync();
        await first.SetAsync(WorkMode.Offline);

        // a fresh service on the same local database = the app started again
        var second = Make(host);
        Assert.Equal(WorkMode.Online, second.Mode);            // before it loads it knows nothing
        await second.InitializeAsync();
        Assert.Equal(WorkMode.Offline, second.Mode);
        Assert.Equal(doctor.UserId, second.Info.ChangedByUserId);
        Assert.NotNull(second.Info.ChangedAt);
    }

    [Fact]
    public async Task Staff_and_signed_out_users_cannot_switch()
    {
        await using var host = await TestHost.StartAsync();
        var mode = Make(host);
        await mode.InitializeAsync();
        Assert.False(mode.CanSwitch);
        Assert.False((await mode.SetAsync(WorkMode.Offline)).Succeeded);   // nobody signed in

        await host.LoginStaffAsync();
        Assert.False(mode.CanSwitch);
        var r = await mode.SetAsync(WorkMode.Offline);
        Assert.False(r.Succeeded);
        Assert.Equal(WorkMode.Online, mode.Mode);
    }

    [Fact]
    public async Task An_admin_cannot_switch_because_Offline_mode_would_lock_the_admin_out()
    {
        await using var host = await TestHost.StartAsync();
        var mode = Make(host);
        await mode.InitializeAsync();
        await host.LoginAdminAsync();
        Assert.False(mode.CanSwitch);
        Assert.False((await mode.SetAsync(WorkMode.Offline)).Succeeded);
        Assert.Equal(WorkMode.Online, mode.Mode);
    }

    [Fact]
    public async Task Offline_is_refused_on_a_computer_that_cannot_sync_but_going_online_is_always_allowed()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginDoctorAsync();
        var mode = new WorkModeService(host.Get<IDbContextFactory<ClinicAppDbContext>>(), host.Get<IAuthService>(), new Blocked("Not registered."));
        await mode.InitializeAsync();
        var r = await mode.SetAsync(WorkMode.Offline);
        Assert.False(r.Succeeded);
        Assert.Contains("registered", r.Error);
        Assert.Equal(WorkMode.Online, mode.Mode);
    }

    [Fact]
    public async Task Every_change_writes_one_audit_row()
    {
        await using var host = await TestHost.StartAsync();
        var doctor = await host.LoginDoctorAsync();
        var mode = Make(host);
        await mode.InitializeAsync();
        await mode.SetAsync(WorkMode.Offline);
        await mode.SetAsync(WorkMode.Online);

        await using var db = await host.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        var rows = await db.AuditLogs.Where(a => a.Action == "WorkModeChanged").OrderBy(a => a.PerformedAt).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Online -> Offline", rows[0].Details);
        Assert.Equal("Offline -> Online", rows[1].Details);
        Assert.All(rows, r => { Assert.Equal(doctor.UserId, r.PerformedByUserId); Assert.Equal(AuditEntityType.Settings, r.EntityType); });
    }

    [Theory]
    [InlineData(null, WorkMode.Online)]
    [InlineData("", WorkMode.Online)]
    [InlineData("garbage", WorkMode.Online)]
    [InlineData("Offline", WorkMode.Offline)]
    [InlineData("Offline|not-a-guid|x", WorkMode.Offline)]
    public void Unreadable_saved_values_mean_Online(string? raw, WorkMode expected) => Assert.Equal(expected, WorkModeService.Parse(raw).Mode);
}
//JJQR - END
