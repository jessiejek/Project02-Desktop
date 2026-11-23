// JJQR - AI (Nov 21, 2026) - TestHost plus AddClinicNodeAdmin (cannot edit the shared TestHost).
using ClinicApp.Infrastructure;
using ClinicNode.Core;
using ClinicNode.Core.Admin;
using ClinicNode.Core.Auth;
using ClinicNode.Data;
using ClinicNode.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.Admin;

public sealed class AdminTestHost : IAsyncDisposable
{
    public string DataDirectory { get; }
    public ServiceProvider Services { get; }

    private AdminTestHost(string dir, ServiceProvider services)
    {
        DataDirectory = dir;
        Services = services;
    }

    public static async Task<AdminTestHost> StartAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clinic-node-tests-admin", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClinicNodeCore(new NodeOptions { DataDirectory = dir, Demo = true });
        services.AddClinicNodeAdmin();
        services.AddClinicNodeSync();
        var sp = services.BuildServiceProvider();
        await sp.GetRequiredService<LocalDatabase>().InitializeAsync();
        //JJQR - AI (Nov 23, 2026) - offline audit: queue numbers come only from blocks reserved online (see TestHost). Without them every admin booking test failed with "no reserved queue numbers left", which hid whether the admin logic itself works.
        var factory = sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var today = ClinicApp.Domain.ClinicClock.Today;
            // a booking made for next week (the tests book 7-8 days ahead) also needs its day's numbers
            foreach (var day in Enumerable.Range(0, 21).Select(n => today.AddDays(n)))
                foreach (var lane in ClinicNode.Sync.Local.QueueBlocks.Lanes)
                    await ClinicNode.Sync.Local.QueueBlocks.SaveAsync(db, new ClinicNode.Sync.QueueBlockReservation(day, lane, 1, 200, 200, null, null));
        }
        //JJQR - END
        return new AdminTestHost(dir, sp);
    }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public async Task<NodeSession> LoginAdminAsync() => await LoginAsync("admin@clinic.test");
    public async Task<NodeSession> LoginDoctorAsync() => await LoginAsync("doctor@clinic.test");
    public async Task<NodeSession> LoginStaffAsync() => await LoginAsync("staff@clinic.test");

    async Task<NodeSession> LoginAsync(string email)
    {
        var auth = Get<IAuthService>();
        var result = await auth.LoginAsync(email, DevDataSeeder.DevPassword, rememberEmail: false);
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
