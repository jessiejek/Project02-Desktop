//JJQR - AI (21 Nov 2026) - DI for online-first sign-in. Call AFTER AddClinicNodeCore (it replaces IAuthService) and after AddClinicNodeApi /
// AddClinicNodeOnlineFirst (it replaces the in-memory IApiTokenStore with the SecureStorage-backed one).
using ClinicNode.Api.Http;
using ClinicNode.Core.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.Api.Auth;

public static class AuthServiceCollectionExtensions
{
    /// <param name="vault">Where the refresh token is kept between runs (the App passes SecureStorage). null → in memory (tests).</param>
    public static IServiceCollection AddOnlineFirstAuth(this IServiceCollection services, Func<IServiceProvider, IRefreshTokenVault>? vault = null)
    {
        services.RemoveAll<IRefreshTokenVault>();
        if (vault is null) services.AddSingleton<IRefreshTokenVault, InMemoryRefreshTokenVault>();
        else services.AddSingleton(vault);

        services.RemoveAll<IApiTokenStore>();
        services.AddSingleton<PersistentApiTokenStore>(sp => new PersistentApiTokenStore(sp.GetRequiredService<IRefreshTokenVault>(), sp.GetService<TimeProvider>()));
        services.AddSingleton<IApiTokenStore>(sp => sp.GetRequiredService<PersistentApiTokenStore>());

        // the local Core service stays available as itself; IAuthService becomes the online-first wrapper
        services.TryAddSingleton<AuthService>(sp => ActivatorUtilities.CreateInstance<AuthService>(sp));
        services.RemoveAll<IAuthService>();
        services.AddSingleton<OnlineFirstAuthService>(sp => ActivatorUtilities.CreateInstance<OnlineFirstAuthService>(sp));
        services.AddSingleton<IAuthService>(sp => sp.GetRequiredService<OnlineFirstAuthService>());
        services.AddSingleton<IOnlineAuthSession>(sp => sp.GetRequiredService<OnlineFirstAuthService>());
        return services;
    }
}
//JJQR - END
