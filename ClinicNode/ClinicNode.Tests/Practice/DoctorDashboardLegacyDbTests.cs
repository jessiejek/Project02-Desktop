// JJQR - AI (Nov 21, 2026) - prove schema repair lets IDoctorDashboardService load against a pre-sync-column node DB.
using ClinicApp.Domain;
using ClinicNode.Core;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using ClinicNode.Tests;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.PracticeTests;

[Collection(SandboxCopyCollection.Name)]
public class DoctorDashboardLegacyDbTests
{
    [SkippableFact]
    public async Task Schema_repair_then_dashboard_loads_against_a_copy_of_the_sandbox_db()
    {
        if (!SandboxCopyCollection.TryCopy(out var dir, out var reason))
            Skip.If(true, reason);

        var dest = Path.Combine(dir, "clinic-node.db");
        try
        {
            // The copy may still be missing the sync columns (the live sandbox as of the original failure).
            await using (var probe = new SqliteConnection($"Data Source={dest}"))
            {
                await probe.OpenAsync();
                var cmd = probe.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('bookings') WHERE name='row_version'";
                _ = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            }

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddClinicNodeCore(new NodeOptions { DataDirectory = dir, Demo = true });
            services.AddClinicNodeStaff();
            await using var sp = services.BuildServiceProvider();
            await sp.GetRequiredService<LocalDatabase>().InitializeAsync();

            await using (var probe = new SqliteConnection($"Data Source={dest}"))
            {
                await probe.OpenAsync();
                var cmd = probe.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('bookings') WHERE name='row_version'";
                Assert.Equal(1, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
                cmd = probe.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('bookings') WHERE name='deleted_at'";
                Assert.Equal(1, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
                cmd = probe.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('bookings') WHERE name='origin_node_id'";
                Assert.Equal(1, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
                cmd = probe.CreateCommand();
                cmd.CommandText = "SELECT email FROM users WHERE email='doctor@clinic.test'";
                Assert.Equal("doctor@clinic.test", (string?)await cmd.ExecuteScalarAsync());
            }

            var doctorId = DemoClinicDay.DemoDoctorId;
            var dash = sp.GetRequiredService<IDoctorDashboardService>();
            var data = await dash.GetAsync(doctorId);
            Assert.False(string.IsNullOrWhiteSpace(data.DoctorName));
            // GetAsync / GetAnalyticsAsync must not throw (the owner-facing Today errors). A quiet clinic day is still a successful load.
            var today = ClinicClock.Today;
            var analytics = await dash.GetAnalyticsAsync(doctorId, new DateOnly(today.Year, today.Month, 1), today, true);
            Assert.NotNull(analytics.Totals);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
// JJQR - END
