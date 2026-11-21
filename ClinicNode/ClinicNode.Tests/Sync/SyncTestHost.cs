//JJQR - AI (Nov 21, 2026)
// Temp SQLite node wired to a FakeSyncServer (same pattern as TestHost, plus sync options).
using ClinicApp.Infrastructure;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.Sync;

public sealed class SyncTestHost : IAsyncDisposable
{
    public string DataDirectory { get; }
    public ServiceProvider Services { get; }
    public FakeSyncServer Cloud { get; }

    private SyncTestHost(string dir, ServiceProvider sp, FakeSyncServer cloud)
    {
        DataDirectory = dir;
        Services = sp;
        Cloud = cloud;
    }

    public static async Task<SyncTestHost> StartAsync(bool registered = true, string? baseUrl = "http://fake.cloud", Action<SyncOptions>? configure = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "clinic-node-sync-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var cloud = new FakeSyncServer();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClinicNodeCore(new NodeOptions { DataDirectory = dir, Demo = true });
        services.AddSingleton<INodeCredentialStore>(new InMemoryNodeCredentialStore(
            registered ? new NodeCredential(Guid.NewGuid(), "secret-for-tests") : null));
        services.AddClinicNodeSync(o =>
        {
            o.BaseUrl = baseUrl ?? "";
            o.HttpHandlerFactory = () => cloud;
            o.PollInterval = TimeSpan.FromMilliseconds(200);
            o.OfflineProbeCap = TimeSpan.FromMilliseconds(200);
            configure?.Invoke(o);
        });
        var sp = services.BuildServiceProvider();
        await sp.GetRequiredService<LocalDatabase>().InitializeAsync();
        return new SyncTestHost(dir, sp, cloud);
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public async Task<ClinicAppDbContext> DbAsync() =>
        await Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();

    public async Task<NodeSession> LoginAsync(string email = "doctor@clinic.test", string? password = null)
    {
        var r = await Get<IAuthService>().LoginAsync(email, password ?? DevDataSeeder.DevPassword, rememberEmail: false);
        Assert.True(r.Succeeded, r.Error);
        return r.Session!;
    }

    public async Task<int> PendingAsync()
    {
        await using var db = await DbAsync();
        return await Get<ISyncOutbox>().CountPendingAsync(db);
    }

    public async ValueTask DisposeAsync()
    {
        await Get<ISyncLoop>().StopAsync();
        await Services.DisposeAsync();
        try { Directory.Delete(DataDirectory, recursive: true); } catch { /* best effort */ }
    }
}
//JJQR - END
