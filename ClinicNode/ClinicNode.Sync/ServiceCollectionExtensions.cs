// JJQR - AI (Nov 21, 2026)
//JJQR - AI (Nov 21, 2026) - allen2: real HTTP client when a cloud URL is set (NodeOptions.CloudBaseUrl
// or configure), credential store, ISyncStatus, background loop. Signature stays compatible.
using ClinicNode.Data;
using ClinicNode.Sync.Auth;
using ClinicNode.Sync.Wire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ClinicNode.Sync;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the sync client. Register your own <see cref="INodeCredentialStore"/> (e.g. SecureStorage)
    /// before calling this to replace the default 0600 file store.
    /// </summary>
    public static IServiceCollection AddClinicNodeSync(this IServiceCollection services, Action<SyncOptions>? configure = null)
    {
        services.AddSingleton(sp =>
        {
            var cfg = sp.GetService<NodeOptions>()?.CloudBaseUrl;
            var o = new SyncOptions { BaseUrl = string.IsNullOrWhiteSpace(cfg) ? SyncOptions.DefaultLocalBackendUrl : cfg };
            configure?.Invoke(o);
            return o;
        });
        services.TryAddSingleton<INodeCredentialStore>(sp => new FileNodeCredentialStore(sp.GetRequiredService<NodeOptions>()));
        //JJQR - AI (Nov 21, 2026) - the URL comes from ISyncEndpoint (saved override > NodeOptions.CloudBaseUrl > default local backend)
        services.AddSingleton<ISyncEndpoint>(sp => new FileSyncEndpoint(sp.GetRequiredService<SyncOptions>(), sp.GetRequiredService<NodeOptions>()));
        services.AddSingleton(sp => new HttpCloudSyncClient(
            sp.GetRequiredService<SyncOptions>(),
            sp.GetRequiredService<INodeCredentialStore>(),
            sp.GetRequiredService<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>(),
            sp.GetRequiredService<ILogger<HttpCloudSyncClient>>(),
            sp.GetRequiredService<ISyncEndpoint>()));
        services.AddSingleton<ISyncNodeRegistration>(sp => sp.GetRequiredService<HttpCloudSyncClient>());
        services.AddSingleton<ICloudSyncClient>(sp => sp.GetRequiredService<HttpCloudSyncClient>());
        //JJQR - END
        services.AddSingleton<SyncService>();
        services.AddSingleton<ISyncService>(sp => sp.GetRequiredService<SyncService>());
        services.AddSingleton<ISyncStatusSource>(sp => sp.GetRequiredService<SyncService>());
        services.AddSingleton<ISyncStatus>(sp => sp.GetRequiredService<SyncService>());
        //JJQR - AI (Nov 21, 2026) - replaces Core's no-op ISyncBridge (immediate sync after saves; revoked login lock)
        services.Replace(ServiceDescriptor.Singleton<ClinicNode.Core.Sync.ISyncBridge>(sp => sp.GetRequiredService<SyncService>()));
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - queue numbers from reserved blocks (Core's booking paths use it; see docs/ONLINE-FIRST.md §10)
        services.TryAddSingleton<ClinicNode.Sync.Local.QueueBlockNumberSource>();
        //JJQR - AI (21 Nov 2026) - B: Core's booking paths ask IQueueNumberSource (wiring of the callers = step C, on hold)
        services.TryAddSingleton<ClinicNode.Core.Queue.IQueueNumberSource>(sp => sp.GetRequiredService<ClinicNode.Sync.Local.QueueBlockNumberSource>());
        //JJQR - END
        //JJQR - END
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.1
        services.AddSingleton<ClinicNode.Core.Sync.IWorkModeService>(sp => new WorkModeService(
            sp.GetRequiredService<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>(),
            (Func<ClinicNode.Core.Auth.IAuthService>)(() => sp.GetRequiredService<ClinicNode.Core.Auth.IAuthService>()), sp.GetService<ClinicNode.Core.Sync.ISyncBridge>()));
        //JJQR - END
        services.AddSingleton<SyncLoop>();
        services.AddSingleton<ISyncLoop>(sp => sp.GetRequiredService<SyncLoop>());
        return services;
    }
}
//JJQR - END
// JJQR - END
