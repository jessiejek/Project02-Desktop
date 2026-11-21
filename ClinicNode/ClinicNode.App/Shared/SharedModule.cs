//JJQR - AI (21 Nov 2026) - allen2 shared module: online-first sign-in, live sync (API reads → local DB, hub events, 60 s safety net, catch-up),
// the SignalR event service, the top-bar status indicator, forgot password and the shared UI services.
// ONE line in MauiProgram.AddClinicNodeAppServices, AFTER AddClinicNodeOnlineFirst(...):
//     services.AddClinicNodeShared(host.UsePlatformStores);
using ClinicNode.Api.Auth;
using ClinicNode.Api.Live;
using ClinicNode.Api.OnlineFirst;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.App.Shared;

public static class SharedModule
{
    public static IServiceCollection AddClinicNodeShared(this IServiceCollection services, bool usePlatformStores = true)
    {
        // item 1: API login first, refresh token in SecureStorage (Keychain / Windows vault)
        Func<IServiceProvider, IRefreshTokenVault>? vault = usePlatformStores ? static _ => new SecureStorageRefreshTokenVault() : null;
        services.AddOnlineFirstAuth(vault);

        // owner rule 1: every API read also lands in the local DB (OnlineFirstProxy picks this up)
        services.TryAddSingleton<IOnlineReadMirror>(sp => ActivatorUtilities.CreateInstance<OnlineReadMirror>(sp));

        // item 4: /hubs/clinic event service (+ 60 s poll fallback)
        services.TryAddSingleton<ClinicHubClient>(sp => ActivatorUtilities.CreateInstance<ClinicHubClient>(sp));
        services.TryAddSingleton<IClinicLiveEvents>(sp => sp.GetRequiredService<ClinicHubClient>());

        // item 5: forgot password (online only)
        services.TryAddSingleton<IPasswordResetClient>(sp => ActivatorUtilities.CreateInstance<PasswordResetClient>(sp));

        // owner rules 2–6: live sync + status line
        services.TryAddSingleton<LiveSyncCoordinator>();
        services.TryAddSingleton<StandaloneVaccinationPush>(); //JJQR - AI (21 Nov 2026) - TASK STANDALONE VACCINE PUSH //JJQR - END
        services.TryAddSingleton<OnlineStatusIndicator>();
        return services;
    }
}
//JJQR - END
