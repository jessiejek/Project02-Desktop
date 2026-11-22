//JJQR - AI (Nov 22, 2026) - offline-mode plan step 2.6 (D9, Q7): offline sign-in works for 7 days after the last successful sync; day 8 is locked with the dates; a never-synced computer is never locked
using System.Net;
using ClinicApp.Infrastructure;
using ClinicNode.Api.Auth;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Api.Tests;

public class OfflineLoginTests
{
    private static Task<ApiRig> StartAsync() => ApiRig.StartAsync(_ => { }, signedIn: false);

    /// <summary>The online-first sign-in on top of the rig's real local one (the rig itself signed in locally to set up).</summary>
    private static IAuthService MakeAuth(ApiRig rig)
    {
        var local = (AuthService)rig.Get<IAuthService>();
        return ActivatorUtilities.CreateInstance<OnlineFirstAuthService>(rig.Services, local);
    }

    private static async Task SetLastSyncAsync(ApiRig rig, DateTimeOffset? at)
    {
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        await SyncOutbox.GetOrCreateIdentityAsync(db);
        if (at is { } a) await SyncOutbox.SetLastSuccessfulSyncAsync(db, a);
    }

    [Fact]
    public async Task In_Offline_mode_sign_in_never_calls_the_server()
    {
        await using var rig = await StartAsync();
        await rig.GoOfflineAsync();
        rig.Http.Requests.Clear();
        var r = await MakeAuth(rig).LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, false);
        Assert.True(r.Succeeded, r.Error);
        Assert.DoesNotContain(rig.Http.Requests, x => x.PathAndQuery.StartsWith("/api/auth"));
    }

    [Fact]
    public async Task Six_days_and_23_hours_after_the_last_sync_is_still_allowed_day_eight_is_locked_with_the_dates()
    {
        await using var rig = await StartAsync();
        await rig.GoOfflineAsync();
        var auth = MakeAuth(rig);
        await SetLastSyncAsync(rig, DateTimeOffset.UtcNow - TimeSpan.FromDays(7) + TimeSpan.FromHours(1));
        Assert.True((await auth.LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, false)).Succeeded);
        await auth.LogoutAsync();

        var last = DateTimeOffset.UtcNow - TimeSpan.FromDays(7) - TimeSpan.FromHours(1);
        await SetLastSyncAsync(rig, last);
        rig.Http.Route = _ => throw new HttpRequestException("down");     // and the server is not reachable either
        var r = await auth.LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, false);
        Assert.False(r.Succeeded);
        Assert.True(r.Locked);
        Assert.Contains("last synced on " + last.ToOffset(TimeSpan.FromHours(8)).ToString("MMM d"), r.Error);
        Assert.Contains("had to sync by " + (last + TimeSpan.FromDays(7)).ToOffset(TimeSpan.FromHours(8)).ToString("MMM d"), r.Error);
        Assert.Contains("7 days", r.Error);
    }

    [Fact]
    public async Task A_computer_that_has_never_synced_is_never_locked()
    {
        await using var rig = await StartAsync();
        await rig.GoOfflineAsync();
        await SetLastSyncAsync(rig, null);
        Assert.True((await MakeAuth(rig).LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, false)).Succeeded);
    }

    [Fact]
    public async Task When_the_seven_days_are_over_the_way_back_is_one_sign_in_through_the_server()
    {
        await using var rig = await StartAsync();
        await rig.GoOfflineAsync();
        await SetLastSyncAsync(rig, DateTimeOffset.UtcNow - TimeSpan.FromDays(9));
        rig.Http.Requests.Clear();
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/api/auth/login" ? FakeHandler.Status(HttpStatusCode.Unauthorized)
            : FakeHandler.Status(HttpStatusCode.OK);
        var r = await MakeAuth(rig).LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, false);
        Assert.False(r.Succeeded);                                          // the fake server refused here; what matters is that it WAS asked
        Assert.Contains(rig.Http.Requests, x => x.PathAndQuery == "/api/auth/login");
    }
}
//JJQR - END
