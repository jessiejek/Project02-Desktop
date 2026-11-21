//JJQR - AI (Nov 21, 2026)
// Round trip against the REAL backend sync API (Project02-be on SQLite). Skipped unless
//   CLINIC_SYNC_IT_URL            e.g. http://localhost:5077   (never :5055 / :3000)
// and either
//   CLINIC_SYNC_IT_ADMIN_TOKEN    cloud Admin JWT (the test registers a fresh node, §2.1)
// or
//   CLINIC_SYNC_IT_NODE_ID + CLINIC_SYNC_IT_NODE_SECRET   an already registered node.
// Run:  ASPNETCORE_URLS=http://localhost:5077 Database__Provider=Sqlite dotnet run --project ../Project02-be/src/ClinicApp.Api
//       CLINIC_SYNC_IT_URL=http://localhost:5077 CLINIC_SYNC_IT_ADMIN_TOKEN=... dotnet test ClinicNode.Tests --filter SyncApiIntegration
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Auth;
using ClinicNode.Sync.Wire;

namespace ClinicNode.Tests.Sync;

public sealed class SyncApiFactAttribute : FactAttribute
{
    public SyncApiFactAttribute()
    {
        var url = Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_URL");
        var hasCreds = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_ADMIN_TOKEN"))
            || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_NODE_SECRET"));
        if (string.IsNullOrEmpty(url) || !hasCreds)
            Skip = "Set CLINIC_SYNC_IT_URL and CLINIC_SYNC_IT_ADMIN_TOKEN (or NODE_ID/NODE_SECRET) to run against the real sync API.";
        else if (url.Contains(":5055") || url.Contains(":3000"))
            Skip = "Refusing to run against :5055/:3000; start the API on another port (e.g. 5077).";
    }
}

public class SyncApiIntegrationTests
{
    [SyncApiFact]
    public async Task SyncApiIntegration_push_and_pull_round_trip_against_the_real_api()
    {
        var url = Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_URL")!;
        await using var host = await SyncTestHost.StartAsync(registered: false, baseUrl: url, configure: o => o.HttpHandlerFactory = null);
        var creds = host.Get<INodeCredentialStore>();
        var nodeId = Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_NODE_ID");
        var secret = Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_NODE_SECRET");
        if (!string.IsNullOrEmpty(secret) && Guid.TryParse(nodeId, out var nid))
        {
            await creds.SaveAsync(new NodeCredential(nid, secret));
        }
        else
        {
            var reg = await host.Get<ISyncNodeRegistration>().RegisterAsync(
                Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_ADMIN_TOKEN")!, "integration-test " + DateTime.Now.ToString("HHmmss"), "test", "0.0.0");
            Assert.True(reg.Succeeded, reg.Error);
        }

        var hs = await host.Get<ISyncNodeRegistration>().HandshakeAsync();
        Assert.NotNull(hs);
        Assert.Equal(1, hs!.ProtocolVersion);

        await host.LoginAsync();
        var patient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Integ", "Ration", new DateOnly(1988, 8, 8), SexType.Female));

        var result = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(result.Succeeded, $"{result.Failure}: {result.Error}");
        Assert.Equal(0, await host.PendingAsync());
        await using var db = await host.DbAsync();
        Assert.False(string.IsNullOrEmpty(await SyncOutbox.GetCursorAsync(db, SyncService.CloudStream)));
        Assert.Empty((await host.Get<ISyncStatus>().GetOpenConflictsAsync()).Where(c => c.EntityId == patient.PatientId.ToString() && c.Kind == "rejected"));
    }
}
//JJQR - END
