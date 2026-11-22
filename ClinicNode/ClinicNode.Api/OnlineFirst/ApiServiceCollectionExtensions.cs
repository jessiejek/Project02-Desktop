//JJQR - AI (Nov 21, 2026) - DI: AddClinicNodeApi() once; then ONE line per Core service: AddOnlineFirst<IService, ApiImpl, LocalImpl>().
using ClinicNode.Api.Http;
using ClinicNode.Api.Services;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.Api.OnlineFirst;

public static class ApiServiceCollectionExtensions
{
    /// <summary>Registers the token store, connectivity monitor, auth client and the typed API client. <paramref name="baseUrl"/> is usually <c>sp =&gt; sp.GetRequiredService&lt;ISyncEndpoint&gt;().BaseUrl</c>.</summary>
    public static IServiceCollection AddClinicNodeApi(this IServiceCollection services, Func<IServiceProvider, string?>? baseUrl = null, Action<ApiOptions>? configure = null)
    {
        services.TryAddSingleton(sp =>
        {
            var o = new ApiOptions();
            if (baseUrl is not null) o.BaseUrl = () => baseUrl(sp);
            configure?.Invoke(o);
            return o;
        });
        services.TryAddSingleton<IApiTokenStore, InMemoryApiTokenStore>();
        services.TryAddSingleton<IApiConnectivity>(sp =>
        {
            var o = sp.GetRequiredService<ApiOptions>();
            var c = new ApiConnectivity(o);
            if (o.AutoStart) c.Start(); // background health loop: no App.xaml.cs change needed
            return c;
        });
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.3: the connection monitor (observes only; a connectivity without probe events just gives it nothing to record)
        services.TryAddSingleton<ConnectionMonitor>(sp => new ConnectionMonitor(
            sp.GetRequiredService<IApiConnectivity>(), sp.GetRequiredService<IApiConnectivity>() as IApiProbeSource ?? new NoProbeSource()));
        services.TryAddSingleton<IConnectionMonitor>(sp => sp.GetRequiredService<ConnectionMonitor>());
        //JJQR - END
        services.TryAddSingleton<IApiAuthClient>(sp => new ApiAuthClient(sp.GetRequiredService<ApiOptions>(), sp.GetRequiredService<IApiTokenStore>(), sp.GetRequiredService<IApiConnectivity>()));
        services.TryAddSingleton<IApiClient>(sp => new ApiClient(sp.GetRequiredService<ApiOptions>(), sp.GetRequiredService<IApiTokenStore>(), sp.GetRequiredService<IApiAuthClient>(), sp.GetRequiredService<IApiConnectivity>()));
        return services;
    }

    /// <summary>
    /// Routes <typeparamref name="TService"/> online-first. <typeparamref name="TLocal"/> is the existing Core implementation (created from DI if it is not registered
    /// as itself); <typeparamref name="TApi"/> is built from DI (it takes <see cref="IApiClient"/>). The interface registration is REPLACED by the router, so call this
    /// after the Core registration.
    /// </summary>
    public static IServiceCollection AddOnlineFirst<TService, TApi, TLocal>(this IServiceCollection services)
        where TService : class where TApi : class, TService where TLocal : class, TService
    {
        services.TryAddSingleton<TLocal>(sp => ActivatorUtilities.CreateInstance<TLocal>(sp));
        services.TryAddSingleton<TApi>(sp => ActivatorUtilities.CreateInstance<TApi>(sp));
        services.RemoveAll<TService>();
        services.AddSingleton<TService>(sp => OnlineFirstProxy<TService>.Create(
            sp.GetRequiredService<TApi>(), sp.GetRequiredService<TLocal>(), sp.GetRequiredService<IApiConnectivity>(), sp.GetService<ISyncBridge>(),
            hasApiSession: () => sp.GetRequiredService<IApiTokenStore>().AccessToken is { Length: > 0 },
            dropApiSession: () => sp.GetRequiredService<IApiTokenStore>().Clear(),
            //JJQR - AI (21 Nov 2026) - API reads → local DB (registered by AddClinicNodeLiveSync; null = no mirroring)
            mirror: sp.GetService<IOnlineReadMirror>(),
            workMode: sp.GetService<IWorkModeService>())); //JJQR - AI (Nov 22, 2026) - step 1.2 //JJQR - END
            //JJQR - END
        return services;
    }

    // ── converted services (add one line per lane service here, or in your own extension) ──────────
    public static IServiceCollection AddOnlineFirstDoctorPerformance(this IServiceCollection services) =>
        services.AddOnlineFirst<IDoctorPerformanceService, ApiDoctorPerformanceService, DoctorPerformanceService>();

    public static IServiceCollection AddOnlineFirstDoctorDashboard(this IServiceCollection services) =>
        services.AddOnlineFirst<IDoctorDashboardService, ApiDoctorDashboardService, DoctorDashboardService>();

    /// <summary>Everything converted so far.</summary>
    public static IServiceCollection AddClinicNodeOnlineFirst(this IServiceCollection services, Func<IServiceProvider, string?>? baseUrl = null, Action<ApiOptions>? configure = null) =>
        services.AddClinicNodeApi(baseUrl, configure).AddOnlineFirstDoctorPerformance().AddOnlineFirstDoctorDashboard();
}
//JJQR - END
