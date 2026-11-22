// JJQR - AI (Nov 21, 2026) - disposable temp SQLite host for unit tests.
using ClinicApp.Infrastructure;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Data;
using ClinicNode.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests;

public sealed class TestHost : IAsyncDisposable
{
    public string DataDirectory { get; }
    public ServiceProvider Services { get; }

    private TestHost(string dir, ServiceProvider services)
    {
        DataDirectory = dir;
        Services = services;
    }

    public static async Task<TestHost> StartAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clinic-node-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClinicNodeCore(new NodeOptions { DataDirectory = dir, Demo = true });
        services.AddClinicNodeSync();
        var sp = services.BuildServiceProvider();
        await sp.GetRequiredService<LocalDatabase>().InitializeAsync();
        //JJQR - AI (Nov 22, 2026) - since queue numbers come only from blocks reserved online, every old test that checks a patient in failed with "no reserved queue numbers left" (about 20 of the 25 failing tests share this one cause).
        // A test computer gets a block for every day this week, in both lanes, as if it had been online once.
        var factory = sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var today = ClinicApp.Domain.ClinicClock.Today;
            foreach (var day in ClinicNode.Sync.Local.QueueBlocks.Days(today))
                foreach (var lane in ClinicNode.Sync.Local.QueueBlocks.Lanes)
                    await ClinicNode.Sync.Local.QueueBlocks.SaveAsync(db, new ClinicNode.Sync.QueueBlockReservation(day, lane, 1, 200, 200, null, null));
        }
        //JJQR - END
        return new TestHost(dir, sp);
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public async Task<NodeSession> LoginDoctorAsync() => await LoginAsync("doctor@clinic.test");
    public async Task<NodeSession> LoginStaffAsync() => await LoginAsync("staff@clinic.test");
    public async Task<NodeSession> LoginAdminAsync() => await LoginAsync("admin@clinic.test");

    async Task<NodeSession> LoginAsync(string email)
    {
        var auth = Get<IAuthService>();
        var result = await auth.LoginAsync(email, ClinicApp.Infrastructure.DevDataSeeder.DevPassword, rememberEmail: false);
        Assert.True(result.Succeeded, result.Error);
        return result.Session!;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        try { Directory.Delete(DataDirectory, recursive: true); } catch { /* best effort */ }
    }
}
// JJQR - END
