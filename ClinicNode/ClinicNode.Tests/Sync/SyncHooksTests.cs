//JJQR - AI (Nov 21, 2026)
// Core hooks: ISyncBridge (immediate sync, revoked lock), vital_reading / booking / payment ops at completion,
// patient update ops with changed_fields, audit_log sweep.
using System.Text.Json.Nodes;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Sync;
using ClinicNode.Core.Visits;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.Sync;

public class SyncHooksTests
{
    private static async Task<(Guid BookingId, Guid ConsultationId)> CompletedVisitAsync(SyncTestHost host)
    {
        var session = await host.LoginAsync();
        var patient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Vi", "Tal", new DateOnly(1980, 5, 5), SexType.Female));
        var bookingId = await host.Get<IVisitStarter>().StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var consults = host.Get<IConsultationService>();
        var draft = (await consults.LoadByBookingAsync(bookingId)).Draft;
        draft.ChiefComplaint = "Cough";
        draft.Subjective = "Dry cough.";
        draft.NoMedsConfirmed = true;
        draft.FollowUpMode = FollowUpMode.None;
        foreach (var v in draft.Vitals)
            v.Value = v.FormKey == "blood_pressure" ? "120/80" : v.FormKey == "pulse_rate" ? "72" : "1";
        draft.PfDecision = "Waive";
        draft.PfWaiveReason = "Indigent";
        var done = await consults.CompleteAsync(draft);
        Assert.True(done.Succeeded, done.Error);
        return (bookingId, done.Draft!.ConsultationId!.Value);
    }

    [Fact]
    public async Task SyncHooks_completion_queues_vital_booking_and_payment_ops_and_pushes_final_snapshots()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (bookingId, _) = await CompletedVisitAsync(host);
        await using (var db = await host.DbAsync())
        {
            var pending = await host.Get<ISyncOutbox>().ListPendingAsync(db, 500);
            var vitals = pending.Where(p => p.Aggregate == "vital_reading").ToList();
            Assert.NotEmpty(vitals);
            Assert.Equal(vitals.Count, await db.PatientVitalReadings.CountAsync(v => v.BookingId == bookingId));
            Assert.Contains(pending, p => p.Aggregate == "booking" && p.OperationType == "status");
            Assert.Contains(pending, p => p.Aggregate == "payment");
        }

        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        var ops = host.Cloud.PushedOperations.ToList();
        var vop = ops.First(o => o["aggregate"]!.GetValue<string>() == "vital_reading");
        var data = vop["payload"]!["data"]!.AsObject();
        Assert.Equal(vop["entity_id"]!.GetValue<string>(), data["id"]!.GetValue<string>());
        Assert.Equal(bookingId.ToString(), data["booking_id"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(data["value"]!.GetValue<string>()));
        var pay = ops.First(o => o["aggregate"]!.GetValue<string>() == "payment")["payload"]!["data"]!.AsObject();
        Assert.Equal("Waived", pay["status"]!.GetValue<string>());
        // vitals are queued before the cloud sees the consultation, in created_at order
        Assert.True(ops.FindIndex(o => o["aggregate"]!.GetValue<string>() == "consultation") < ops.FindIndex(o => o["aggregate"]!.GetValue<string>() == "vital_reading"));
    }

    [Fact]
    public async Task SyncHooks_draft_save_does_not_queue_vitals_because_the_cloud_treats_them_as_append_only()
    {
        await using var host = await SyncTestHost.StartAsync();
        var session = await host.LoginAsync();
        var patient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Dr", "Aft", new DateOnly(1980, 5, 5), SexType.Male));
        var bookingId = await host.Get<IVisitStarter>().StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var draft = (await host.Get<IConsultationService>().LoadByBookingAsync(bookingId)).Draft;
        draft.Vitals[0].Value = "99";
        Assert.True((await host.Get<IConsultationService>().SaveDraftAsync(draft)).Succeeded);
        await using var db = await host.DbAsync();
        Assert.DoesNotContain(await host.Get<ISyncOutbox>().ListPendingAsync(db, 500), p => p.Aggregate == "vital_reading");
    }

    [Fact]
    public async Task SyncHooks_patient_update_queues_an_update_op_with_changed_fields()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        var patients = host.Get<IPatientService>();
        var p = await patients.CreateAsync(new NewPatientRequest("Up", "Date", new DateOnly(1970, 1, 1), SexType.Male, ContactNumber: "0917"));
        var updated = await patients.UpdateAsync(p.PatientId, new PatientUpdate(LastName: "Dated", ContactNumber: "0917", Address: "Manila"));
        Assert.Equal("Dated", updated!.LastName);

        await using (var db = await host.DbAsync())
        {
            var op = (await host.Get<ISyncOutbox>().ListPendingAsync(db, 50)).Single(x => x.OperationType == "update");
            Assert.Equal("patient", op.Aggregate);
            Assert.Equal(p.PatientId, op.EntityId);
        }
        // unchanged update queues nothing
        await patients.UpdateAsync(p.PatientId, new PatientUpdate(LastName: "Dated"));
        await using (var db = await host.DbAsync())
            Assert.Single(await host.Get<ISyncOutbox>().ListPendingAsync(db, 50), x => x.OperationType == "update");

        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var push = host.Cloud.PushedOperations.Single(o => o["operation_type"]!.GetValue<string>() == "update");
        var changed = push["payload"]!["changed_fields"]!.AsArray().Select(n => n!.GetValue<string>()).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { "address", "last_name" }, changed);
        Assert.Equal("Dated", push["payload"]!["data"]!["last_name"]!.GetValue<string>());
    }

    [Fact]
    public async Task SyncHooks_audit_rows_are_swept_into_the_outbox_once_and_pulled_rows_are_never_pushed_back()
    {
        await using var host = await SyncTestHost.StartAsync();
        var local = Guid.NewGuid();
        await using (var db = await host.DbAsync())
        {
            db.AuditLogs.Add(new AuditLog { Id = local, EntityType = AuditEntityType.Patient, EntityId = Guid.NewGuid(), Action = "Updated", PerformedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var fromCloud = Guid.NewGuid();
        host.Cloud.EnqueuePull("1", false, FakeSyncServer.Change("audit_log", fromCloud, new JsonObject
        {
            ["id"] = fromCloud.ToString(), ["entity_type"] = "Patient", ["entity_id"] = Guid.NewGuid().ToString(), ["action"] = "Cloud", ["performed_at"] = "2026-11-21T00:00:00+00:00",
        }));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        var audits = host.Cloud.PushedOperations.Where(o => o["aggregate"]!.GetValue<string>() == "audit_log").ToList();
        Assert.Single(audits, o => o["entity_id"]!.GetValue<string>() == local.ToString());
        Assert.DoesNotContain(audits, o => o["entity_id"]!.GetValue<string>() == fromCloud.ToString());
        Assert.Equal("Updated", audits.First(o => o["entity_id"]!.GetValue<string>() == local.ToString())["payload"]!["data"]!["action"]!.GetValue<string>());
        Assert.Equal(audits.Select(a => a["entity_id"]!.GetValue<string>()).Distinct().Count(), audits.Count);
    }

    [Fact]
    public async Task SyncHooks_revoked_node_locks_login_immediately_and_stays_locked_across_a_restart_of_the_sync_service()
    {
        await using var host = await SyncTestHost.StartAsync();
        Assert.True((await host.Get<IAuthService>().LoginAsync("doctor@clinic.test", ClinicApp.Infrastructure.DevDataSeeder.DevPassword, false)).Succeeded);
        await host.Get<IAuthService>().LogoutAsync();

        host.Cloud.Revoked = true;
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.Equal(SyncFailureKind.Revoked, r.Failure);

        var auth = host.Get<IAuthService>();
        Assert.True(await auth.IsLockedForOfflinePeriodAsync());
        var login = await auth.LoginAsync("doctor@clinic.test", ClinicApp.Infrastructure.DevDataSeeder.DevPassword, false);
        Assert.False(login.Succeeded);
        Assert.True(login.Locked);
        Assert.Contains("disconnected", login.Error);

        // a fresh SyncService (app restart) still knows: the flag is persisted in the node database
        var fresh = new SyncService(host.Get<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>(), host.Get<ISyncOutbox>(),
            host.Get<ICloudSyncClient>(), host.Get<SyncOptions>(), Microsoft.Extensions.Logging.Abstractions.NullLogger<SyncService>.Instance);
        Assert.True(await ((ISyncBridge)fresh).IsRevokedAsync());
    }

    [Fact]
    public async Task SyncHooks_without_sync_registered_core_uses_the_noop_bridge()
    {
        var sp = new ServiceCollection();
        sp.AddLogging();
        var dir = Path.Combine(Path.GetTempPath(), "clinic-node-sync-tests", Guid.NewGuid().ToString("N"));
        sp.AddClinicNodeCore(new NodeOptions { DataDirectory = dir, Demo = true });
        await using var provider = sp.BuildServiceProvider();
        var bridge = provider.GetRequiredService<ISyncBridge>();
        Assert.IsType<NoOpSyncBridge>(bridge);
        bridge.RequestSync();
        Assert.False(await bridge.IsRevokedAsync());
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public async Task SyncHooks_saves_trigger_an_immediate_sync_without_waiting_for_the_poll()
    {
        await using var host = await SyncTestHost.StartAsync(configure: o => o.PollInterval = TimeSpan.FromHours(1));
        await host.LoginAsync();
        var loop = host.Get<ISyncLoop>();
        loop.Start();
        await WaitUntil(() => host.Get<ISyncStatus>().LastSyncedAt is not null); // first cycle done, loop now sleeping 1h
        var before = host.Cloud.PushedOperations.Count;

        await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Fa", "St", new DateOnly(1999, 9, 9), SexType.Female));
        await WaitUntil(() => host.Cloud.PushedOperations.Count > before);
        await WaitUntil(() => host.Get<ISyncStatus>().PendingCount == 0);
        Assert.Contains(host.Cloud.PushedOperations, o => o["aggregate"]!.GetValue<string>() == "patient");
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 10000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("condition not met");
            await Task.Delay(50);
        }
    }
}
//JJQR - END
