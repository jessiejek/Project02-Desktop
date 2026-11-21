//JJQR - AI (Nov 21, 2026)
// Contract §8/§11 alignment (reason codes, poison batch, child-set semantics, composite ids) and the runtime-configurable cloud URL.
using System.Net;
using System.Text.Json.Nodes;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Visits;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Sync;

public class SyncAlignmentAndEndpointTests
{
    private static async Task<Guid> NewPatientAsync(SyncTestHost host, string first = "Al")
    {
        await host.LoginAsync();
        var p = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest(first, "Ign", new DateOnly(1990, 1, 1), SexType.Male));
        return p.PatientId;
    }

    // ---- runtime URL ----------------------------------------------------------------------------------------------

    [Fact]
    public void Default_cloud_url_is_the_local_backend()
    {
        Assert.Equal("http://localhost:5055", SyncOptions.DefaultLocalBackendUrl);
        Assert.Equal(SyncOptions.DefaultLocalBackendUrl, new SyncOptions().BaseUrl);
    }

    [Fact]
    public async Task Endpoint_defaults_persists_validates_and_resets()
    {
        await using var host = await SyncTestHost.StartAsync(baseUrl: SyncOptions.DefaultLocalBackendUrl);
        var ep = host.Get<ISyncEndpoint>();
        var changes = new List<string>();
        ep.Changed += changes.Add;

        Assert.Equal("http://localhost:5055", ep.BaseUrl);
        Assert.False(ep.IsCustom);

        var bad = await ep.SetBaseUrlAsync("not a url");
        Assert.False(bad.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(bad.Error));
        Assert.Equal("http://localhost:5055", ep.BaseUrl);
        Assert.Empty(changes);

        var ok = await ep.SetBaseUrlAsync("http://localhost:5077/");
        Assert.True(ok.Succeeded, ok.Error);
        Assert.Equal("http://localhost:5077", ep.BaseUrl);
        Assert.True(ep.IsCustom);
        Assert.Equal(new[] { "http://localhost:5077" }, changes);
        Assert.True(File.Exists(Path.Combine(host.DataDirectory, "cloud-endpoint.json")));

        // same value again does not re-fire
        await ep.SetBaseUrlAsync("http://localhost:5077");
        Assert.Single(changes);

        // a fresh endpoint over the same data directory (an app restart) picks the saved value up
        var again = new FileSyncEndpoint(new SyncOptions(), new NodeOptions { DataDirectory = host.DataDirectory });
        Assert.Equal("http://localhost:5077", again.BaseUrl);

        var local = await ep.SetBaseUrlAsync("");
        Assert.True(local.Succeeded);
        Assert.Equal("", ep.BaseUrl);
        Assert.False(host.Get<ICloudSyncClient>().IsConfigured);

        var reset = await ep.ResetAsync();
        Assert.True(reset.Succeeded);
        Assert.Equal("http://localhost:5055", ep.BaseUrl);
        Assert.False(ep.IsCustom);
        Assert.False(File.Exists(Path.Combine(host.DataDirectory, "cloud-endpoint.json")));
    }

    [Fact]
    public async Task Changing_the_url_at_runtime_applies_without_a_restart_and_restarts_the_pull()
    {
        await using var host = await SyncTestHost.StartAsync(baseUrl: "http://one.cloud");
        await NewPatientAsync(host);
        host.Cloud.EnqueuePull("cur-1", false);
        var first = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(first.Succeeded, first.Error);
        Assert.Equal(1, host.Cloud.TokensIssued);

        await host.Get<ISyncEndpoint>().SetBaseUrlAsync("http://two.cloud");
        var second = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(second.Succeeded, second.Error);

        lock (host.Cloud.Requests)
        {
            Assert.Equal("one.cloud", host.Cloud.Requests.First().RequestUri!.Host);
            Assert.Equal("two.cloud", host.Cloud.Requests.Last().RequestUri!.Host);
        }
        Assert.Equal(2, host.Cloud.TokensIssued);           // token was dropped with the old URL
        Assert.True(string.IsNullOrEmpty(host.Cloud.PullCursors.Last()) || host.Cloud.PullCursors.Last() == "0",
            $"pull should start afresh against the new cloud, got '{host.Cloud.PullCursors.Last()}'");
    }

    [Fact]
    public async Task Blank_url_means_local_only_and_a_later_url_resumes_sync()
    {
        await using var host = await SyncTestHost.StartAsync(baseUrl: "http://one.cloud");
        await NewPatientAsync(host);
        var ep = host.Get<ISyncEndpoint>();
        await ep.SetBaseUrlAsync("");
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.False(r.Succeeded);
        Assert.True(await host.PendingAsync() > 0);
        Assert.Empty(host.Cloud.PushedOperations);

        await ep.SetBaseUrlAsync("http://three.cloud");
        var r2 = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r2.Succeeded, r2.Error);
        Assert.Equal(0, await host.PendingAsync());
    }

    [Fact]
    public async Task Test_connection_reports_reachability()
    {
        await using var host = await SyncTestHost.StartAsync(baseUrl: "http://one.cloud");
        var ep = host.Get<ISyncEndpoint>();
        Assert.True(await ep.TestConnectionAsync("http://probe.cloud"));
        host.Cloud.Offline = true;
        Assert.False(await ep.TestConnectionAsync());
    }

    // ---- reason codes / poison batch ------------------------------------------------------------------------------

    [Fact]
    public async Task Illegal_transition_rejection_is_acked_and_listed_for_review()
    {
        await using var host = await SyncTestHost.StartAsync();
        await NewPatientAsync(host);
        host.Cloud.OnPush = op => host.Cloud.Applied(op, "rejected", "illegal_transition");
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        Assert.Equal(0, await host.PendingAsync());
        await using var db = await host.DbAsync();
        Assert.Contains(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.Kind == "rejected");
    }

    [Fact]
    public async Task Cloud_edit_overwritten_on_an_applied_op_gets_a_review_entry()
    {
        await using var host = await SyncTestHost.StartAsync();
        await NewPatientAsync(host);
        host.Cloud.OnPush = op => host.Cloud.Applied(op, "applied", "cloud_edit_overwritten");
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        Assert.Equal(0, await host.PendingAsync());
        await using var db = await host.DbAsync();
        Assert.Contains(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.Kind == "cloud_edit_overwritten");
    }

    [Fact]
    public async Task Batch_level_invalid_operation_is_isolated_to_the_culprit_op()
    {
        await using var host = await SyncTestHost.StartAsync();
        var session = await host.LoginAsync();
        var patients = host.Get<IPatientService>();
        await patients.CreateAsync(new NewPatientRequest("Good", "One", new DateOnly(1990, 1, 1), SexType.Male));
        var bad = await patients.CreateAsync(new NewPatientRequest("Bad", "Two", new DateOnly(1990, 1, 1), SexType.Male));
        await patients.CreateAsync(new NewPatientRequest("Good", "Three", new DateOnly(1990, 1, 1), SexType.Male));
        var badId = bad.PatientId.ToString();

        host.Cloud.BatchError = ops => ops.OfType<JsonObject>().Any(o => o["entity_id"]!.GetValue<string>() == badId)
            ? (HttpStatusCode.BadRequest, new JsonObject { ["error"] = "invalid_operation" })
            : null;

        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        Assert.Equal(0, await host.PendingAsync());          // good ops landed, culprit rejected and acked
        var pushedIds = host.Cloud.PushedOperations.Select(o => o["entity_id"]!.GetValue<string>()).ToList();
        Assert.Equal(2, pushedIds.Count);
        Assert.DoesNotContain(badId, pushedIds);
        await using var db = await host.DbAsync();
        Assert.Contains(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.EntityId == badId);
        _ = session;
    }

    [Fact]
    public async Task Id_conflict_409_is_not_retried_forever()
    {
        await using var host = await SyncTestHost.StartAsync();
        await NewPatientAsync(host);
        host.Cloud.BatchError = _ => (HttpStatusCode.Conflict, new JsonObject { ["error"] = "id_conflict" });
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        Assert.Equal(0, await host.PendingAsync());
        await using var db = await host.DbAsync();
        Assert.NotEmpty(await SyncLocalStore.ListOpenConflictsAsync(db));
    }

    // ---- consultation child sets / composite ids -------------------------------------------------------------------

    private static async Task<Guid> ConsultationWithDiagnosisAsync(SyncTestHost host)
    {
        var session = await host.LoginAsync();
        var patient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Di", "Ag", new DateOnly(1980, 1, 1), SexType.Female));
        var bookingId = await host.Get<IVisitStarter>().StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var consults = host.Get<IConsultationService>();
        var ws = await consults.LoadByBookingAsync(bookingId);
        ws.Draft.ChiefComplaint = "Fever";
        ws.Draft.Diagnoses.Add(new DiagnosisLine { Icd10Code = "J06.9", Type = DiagnosisType.Primary });
        var saved = await consults.SaveDraftAsync(ws.Draft);
        Assert.True(saved.Succeeded, saved.Error);
        return saved.Draft!.ConsultationId!.Value;
    }

    [Fact]
    public async Task Pushed_consultation_diagnoses_use_rest_json_names()
    {
        await using var host = await SyncTestHost.StartAsync();
        await ConsultationWithDiagnosisAsync(host);
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        var op = host.Cloud.PushedOperations.Last(o => o["aggregate"]!.GetValue<string>() == "consultation");
        var dx = op["payload"]!["data"]!["diagnoses"]!.AsArray();
        Assert.Single(dx);
        Assert.Equal("J06.9", dx[0]!["icd10_code"]!.GetValue<string>());
    }

    [Fact]
    public async Task Pulled_consultation_without_a_child_key_keeps_that_set_and_empty_array_removes_it()
    {
        await using var host = await SyncTestHost.StartAsync();
        var consultId = await ConsultationWithDiagnosisAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);   // drain the outbox so the cloud copy may apply

        async Task<int> DxCountAsync()
        {
            await using var db = await host.DbAsync();
            return await db.ConsultationDiagnoses.CountAsync(d => d.ConsultationId == consultId);
        }
        var before = await DxCountAsync();
        Assert.Equal(1, before);

        // build the cloud copy from what we pushed, minus children
        var pushed = host.Cloud.PushedOperations.Last(o => o["aggregate"]!.GetValue<string>() == "consultation")["payload"]!["data"]!.AsObject();
        var keep = (JsonObject)pushed.DeepClone();
        keep.Remove("diagnoses");
        keep["consultation"]!["chief_complaint"] = "Fever (cloud)";
        host.Cloud.EnqueuePull("c2", false, FakeSyncServer.Change("consultation", consultId, keep, rowVersion: 50));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.Equal(1, await DxCountAsync());                      // key absent → set kept

        var remove = (JsonObject)keep.DeepClone();
        remove["diagnoses"] = new JsonArray();
        host.Cloud.EnqueuePull("c3", false, FakeSyncServer.Change("consultation", consultId, remove, rowVersion: 51));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.Equal(0, await DxCountAsync());                      // [] → removed
    }

    [Fact]
    public async Task Composite_string_entity_ids_use_colon_and_apply_on_pull()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        Guid doctorId;
        var serviceId = Guid.NewGuid();
        await using (var db = await host.DbAsync())
            doctorId = await db.Doctors.Select(d => d.DoctorId).FirstAsync();
        var svc = FakeSyncServer.Change("service", serviceId, new JsonObject
        {
            ["service_id"] = serviceId.ToString(), ["name"] = "Sync Svc", ["category"] = "Consultation", ["price"] = 500,
            ["is_active"] = true, ["created_at"] = "2026-11-21T06:00:00+00:00", ["updated_at"] = "2026-11-21T06:00:00+00:00",
        }, rowVersion: 6);
        var composite = $"{doctorId}:{serviceId}";
        var change = new JsonObject
        {
            ["aggregate"] = "doctor_service", ["entity_id"] = composite, ["operation"] = "upsert", ["row_version"] = 7,
            ["origin_node_id"] = null, ["changed_at"] = "2026-11-21T06:00:00+00:00",
            ["data"] = new JsonObject { ["doctor_id"] = doctorId.ToString(), ["service_id"] = serviceId.ToString(), ["duration_minutes"] = 20, ["created_at"] = "2026-11-21T06:00:00+00:00" },
        };
        host.Cloud.EnqueuePull("c1", false, svc, change);
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        await using var db2 = await host.DbAsync();
        Assert.Empty(await SyncLocalStore.ListOpenConflictsAsync(db2));
        Assert.NotNull(await SyncLocalStore.GetRowVersionAsync(db2, "doctor_service", composite));
    }
}
//JJQR - END
