//JJQR - AI (2026-11-26) - GAV-12: "Merge to existing patient", this computer's side. The server sends the merged-away record as an UPSERT carrying
// merged_into_patient_id: this computer moves its own rows of it (visits, consultations, ...) to the kept record and drops the copy, or keeps it
// (hidden, listed once) while a change here is unsent and clears it after the next good sync. An offline change pushed after the merge answers
// merged / patient_merged (the server put it on the kept record): the copy leaves the same way. Fake names only.
using System.Text.Json.Nodes;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Visits;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Sync;

public class MergeToExistingSyncTests
{
    static async Task<Guid> NewPatientAsync(SyncTestHost host, string first) =>
        (await host.Get<IPatientService>().CreateAsync(new NewPatientRequest(first, "Fakerubi", new DateOnly(1990, 1, 20), SexType.Male))).PatientId;

    static JsonObject MergedDoc(Guid gone, Guid keep) => new()
    {
        ["patient_id"] = gone.ToString(), ["merged_into_patient_id"] = keep.ToString(), ["deleted_at"] = "2026-11-26T02:00:00+00:00",
        ["first_name"] = "Jese", ["last_name"] = "Fakerubi",
    };

    [Fact]
    public async Task A_merged_upsert_moves_this_computers_visits_to_the_kept_patient_and_drops_the_copy()
    {
        await using var host = await SyncTestHost.StartAsync();
        var session = await host.LoginAsync();
        var keep = await NewPatientAsync(host, "Jessie");
        var gone = await NewPatientAsync(host, "Jese");
        await host.Get<IVisitStarter>().StartWalkInConsultAsync(gone, session.DoctorId!.Value, null); // today's visit was opened on the duplicate
        Assert.True((await host.Get<ISyncService>().RunManualAsync()).Succeeded);

        host.Cloud.EnqueuePull("7", false, FakeSyncServer.Change("patient", gone, MergedDoc(gone, keep)));
        Assert.True((await host.Get<ISyncService>().RunManualAsync()).Succeeded);

        await using var db = await host.DbAsync();
        Assert.False(await db.Patients.AnyAsync(p => p.PatientId == gone));                 // the duplicate left this computer
        Assert.True(await db.Bookings.AnyAsync(b => b.PatientId == keep));                   // its visit is under the kept patient
        Assert.False(await db.Consultations.AnyAsync(c => c.PatientId == gone));
        Assert.False(await db.Bookings.AnyAsync(b => b.PatientId == gone));
        Assert.DoesNotContain(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.Kind == AggregateDocuments.MergedKeptKind);
    }

    [Fact]
    public async Task With_an_unsent_change_the_copy_is_hidden_and_listed_then_leaves_after_the_next_good_sync()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        var keep = await NewPatientAsync(host, "Jessie");
        Assert.True((await host.Get<ISyncService>().RunManualAsync()).Succeeded);
        var gone = await NewPatientAsync(host, "Jese");                                       // not sent yet
        var retry = true;
        host.Cloud.OnPush = op => retry ? host.Cloud.Applied(op, "retry") : host.Cloud.Applied(op);
        host.Cloud.EnqueuePull("7", false, FakeSyncServer.Change("patient", gone, MergedDoc(gone, keep)));
        await host.Get<ISyncService>().RunManualAsync();

        await using (var db = await host.DbAsync())
        {
            var p = await db.Patients.AsNoTracking().SingleAsync(x => x.PatientId == gone);
            Assert.Equal(keep, p.MergedIntoPatientId);                                         // hidden from the lists
            Assert.Contains(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.Kind == AggregateDocuments.MergedKeptKind && c.EntityId == gone.ToString());
        }

        retry = false;                                                                         // the change reaches the server (retry backoff skipped here)
        await using (var db = await host.DbAsync())
        {
            var outbox = host.Get<ClinicNode.Data.ISyncOutbox>();
            foreach (var e in await outbox.ListPendingAsync(db, 500)) await outbox.MarkAckedAsync(db, e.OperationId);
        }
        Assert.True((await host.Get<ISyncService>().RunManualAsync()).Succeeded);
        await using (var db = await host.DbAsync())
        {
            Assert.False(await db.Patients.AnyAsync(x => x.PatientId == gone));
            Assert.DoesNotContain(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.Kind == AggregateDocuments.MergedKeptKind);
        }
    }

    [Fact]
    public async Task An_offline_patient_answered_patient_merged_moves_its_visit_to_the_kept_patient()
    {
        await using var host = await SyncTestHost.StartAsync();
        var session = await host.LoginAsync();
        var keep = await NewPatientAsync(host, "Jessie");
        Assert.True((await host.Get<ISyncService>().RunManualAsync()).Succeeded);
        // made offline (not sent yet) with a visit; meanwhile the server merged this id into the kept record
        var gone = await NewPatientAsync(host, "Jese");
        await host.Get<IVisitStarter>().StartWalkInConsultAsync(gone, session.DoctorId!.Value, null);
        host.Cloud.OnPush = op =>
        {
            if (op["aggregate"]?.GetValue<string>() != "patient" || op["entity_id"]?.GetValue<string>() != gone.ToString()) return host.Cloud.Applied(op);
            var r = host.Cloud.Applied(op, "merged", "patient_merged");
            r["conflict_fields"] = new JsonArray("merged_into_patient_id:" + keep);
            return r;
        };
        Assert.True((await host.Get<ISyncService>().RunManualAsync()).Succeeded);

        await using var db = await host.DbAsync();
        Assert.True(await db.Bookings.AnyAsync(b => b.PatientId == keep));
        Assert.False(await db.Bookings.AnyAsync(b => b.PatientId == gone));
        Assert.False(await db.Patients.AnyAsync(p => p.PatientId == gone));
        Assert.DoesNotContain(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.Kind == AggregateDocuments.MergedKeptKind);
    }

    [Fact]
    public void The_kept_id_is_read_from_a_push_result_or_a_review_item()
    {
        var k = Guid.NewGuid();
        Assert.Equal(k, AggregateDocuments.KeptIdFrom(["first_name", "merged_into_patient_id:" + k]));
        Assert.Equal(k, AggregateDocuments.KeptIdFrom(null, "{\"merged_into_patient_id\":\"" + k + "\"}"));
        Assert.Null(AggregateDocuments.KeptIdFrom(["first_name"], "not json"));
    }
}
//JJQR - END
