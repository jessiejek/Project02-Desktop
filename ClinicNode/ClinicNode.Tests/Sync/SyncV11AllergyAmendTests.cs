//JJQR - AI (Nov 21, 2026)
// Contract v1.1: patient allergies (§5.1) and consultation amendments (§5.4) on the node: outbox writers, push snapshots,
// pull application and every new result code (allergy_union, stale_amendment, payment_settled, duplicate, invalid_payload).
using System.Text.Json.Nodes;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Core;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Visits;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Sync;

public class SyncV11AllergyAmendTests
{
    private static async Task<Guid> PatientAsync(SyncTestHost host)
    {
        await host.LoginAsync();
        var p = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Al", "Lergic", new DateOnly(1985, 6, 7), SexType.Female));
        return p.PatientId;
    }

    private static JsonObject LastPatientData(SyncTestHost host, Guid id) =>
        host.Cloud.PushedOperations.Last(o => o["aggregate"]!.GetValue<string>() == "patient" && o["entity_id"]!.GetValue<string>() == id.ToString())["payload"]!["data"]!.AsObject();

    private static async Task<List<PatientAllergy>> AllergiesAsync(SyncTestHost host, Guid id)
    {
        await using var db = await host.DbAsync();
        return await db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == id).OrderBy(a => a.Allergen).ToListAsync();
    }

    private static async Task<string> ReviewAsync(SyncTestHost host)
    {
        await using var db = await host.DbAsync();
        return string.Join(" | ", (await SyncLocalStore.ListOpenConflictsAsync(db)).Select(c => $"{c.Kind}/{c.ReasonCode}: {c.Message}"));
    }

    // ---- allergies: outbox writer + push ------------------------------------------------------------------------

    [Fact]
    public async Task Allergy_edit_queues_a_patient_op_and_pushes_the_full_list_with_rest_names()
    {
        await using var host = await SyncTestHost.StartAsync();
        var id = await PatientAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var pushedBefore = host.Cloud.PushedOperations.Count;

        var stored = await host.Get<IPatientService>().UpdateAllergiesAsync(id, new[]
        {
            new AllergyInput("Penicillin", "Rash", "Severe"), new AllergyInput("Sulfa", "Hives", "Mild"),
        });
        Assert.Equal(2, stored!.Count);
        await using (var db = await host.DbAsync())
        {
            var pending = await host.Get<ISyncOutbox>().ListPendingAsync(db, 50);
            var op = Assert.Single(pending, p => p.Aggregate == "patient");
            Assert.Contains("allergies", op.PayloadJson);
        }

        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var op2 = host.Cloud.PushedOperations.Skip(pushedBefore).Single(o => o["aggregate"]!.GetValue<string>() == "patient");
        Assert.Equal("allergies", Assert.Single(op2["payload"]!["changed_fields"]!.AsArray())!.GetValue<string>());
        var list = op2["payload"]!["data"]!["allergies"]!.AsArray();
        Assert.Equal(new[] { "Penicillin", "Sulfa" }, list.Select(a => a!["allergen"]!.GetValue<string>()).OrderBy(x => x).ToArray());
        Assert.Equal("Severe", list.First(a => a!["allergen"]!.GetValue<string>() == "Penicillin")!["severity"]!.GetValue<string>());
        Assert.All(list, a => Assert.False(a!["is_nkda"]!.GetValue<bool>()));
    }

    [Fact]
    public async Task Real_allergen_replaces_nkda_and_an_unchanged_list_queues_nothing()
    {
        await using var host = await SyncTestHost.StartAsync();
        var id = await PatientAsync(host);
        var svc = host.Get<IPatientService>();
        await svc.UpdateAllergiesAsync(id, new[] { new AllergyInput("", IsNkda: true) });
        Assert.True((await AllergiesAsync(host, id)).Single().IsNkda);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        await svc.UpdateAllergiesAsync(id, new[] { new AllergyInput("", IsNkda: true), new AllergyInput("Latex", "Itch", "Mild") });
        var now = await AllergiesAsync(host, id);
        Assert.Equal("Latex", now.Single().Allergen);             // NKDA never sits next to a real allergen
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.DoesNotContain(LastPatientData(host, id)["allergies"]!.AsArray(), a => a!["is_nkda"]!.GetValue<bool>());

        var pushed = host.Cloud.PushedOperations.Count;
        await svc.UpdateAllergiesAsync(id, new[] { new AllergyInput("latex", "Itch", "Mild") });   // same list (case-insensitive)
        Assert.Equal(0, await host.PendingAsync());
        await host.Get<ISyncService>().RunOnceAsync();
        Assert.Equal(pushed, host.Cloud.PushedOperations.Count);
    }

    [Fact]
    public async Task Clearing_allergies_pushes_an_empty_array()
    {
        await using var host = await SyncTestHost.StartAsync();
        var id = await PatientAsync(host);
        var svc = host.Get<IPatientService>();
        await svc.UpdateAllergiesAsync(id, new[] { new AllergyInput("Dust") });
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        await svc.UpdateAllergiesAsync(id, Array.Empty<AllergyInput>());
        Assert.Empty(await AllergiesAsync(host, id));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var allergies = LastPatientData(host, id)["allergies"];
        Assert.NotNull(allergies);
        Assert.Empty(allergies!.AsArray());
    }

    [Fact]
    public async Task Allergy_union_result_is_applied_locally_and_listed_for_review()
    {
        await using var host = await SyncTestHost.StartAsync();
        var id = await PatientAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        await host.Get<IPatientService>().UpdateAllergiesAsync(id, new[] { new AllergyInput("Penicillin", "Rash", "Severe") });

        host.Cloud.OnPush = op =>
        {
            if (op["aggregate"]!.GetValue<string>() != "patient") return host.Cloud.Applied(op);
            var current = (JsonObject)op["payload"]!["data"]!.DeepClone();
            current["allergies"] = new JsonArray
            {
                new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["allergen"] = "Penicillin", ["reaction"] = "Rash", ["severity"] = "Severe", ["is_nkda"] = false },
                new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["allergen"] = "Peanut", ["reaction"] = "Swelling", ["severity"] = "Severe", ["is_nkda"] = false },
            };
            return host.Cloud.Applied(op, "merged", "allergy_union", current);
        };
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        Assert.Equal(new[] { "Peanut", "Penicillin" }, (await AllergiesAsync(host, id)).Select(a => a.Allergen).ToArray());
        Assert.Equal(0, await host.PendingAsync());
        await using var db = await host.DbAsync();
        var review = Assert.Single(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.ReasonCode == "allergy_union");
        Assert.Equal("merged", review.Kind);
        Assert.False(string.IsNullOrWhiteSpace(review.Message));
    }

    // ---- allergies: pull -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pulled_patient_allergies_absent_keeps_empty_clears_and_a_list_replaces()
    {
        await using var host = await SyncTestHost.StartAsync();
        var id = await PatientAsync(host);
        await host.Get<IPatientService>().UpdateAllergiesAsync(id, new[] { new AllergyInput("Aspirin", "Asthma", "Moderate") });
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var doc = LastPatientData(host, id);

        // 1. key absent → keep
        var absent = (JsonObject)doc.DeepClone(); absent.Remove("allergies"); absent["first_name"] = "Renamed";
        host.Cloud.EnqueuePull("p1", false, FakeSyncServer.Change("patient", id, absent, rowVersion: 40));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.Equal("Aspirin", Assert.Single(await AllergiesAsync(host, id)).Allergen);

        // 2. a list replaces (and a stray NKDA next to a real allergen is dropped)
        var replace = (JsonObject)absent.DeepClone();
        replace["allergies"] = new JsonArray
        {
            new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["allergen"] = "Shellfish", ["reaction"] = "Hives", ["severity"] = "Mild", ["is_nkda"] = false },
            new JsonObject { ["id"] = Guid.NewGuid().ToString(), ["allergen"] = "NKDA", ["is_nkda"] = true },
        };
        host.Cloud.EnqueuePull("p2", false, FakeSyncServer.Change("patient", id, replace, rowVersion: 41));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.True((await AllergiesAsync(host, id)).Any(a => a.Allergen == "Shellfish"), await ReviewAsync(host));
        Assert.Equal("Shellfish", Assert.Single(await AllergiesAsync(host, id)).Allergen);

        // 3. [] clears
        var clear = (JsonObject)absent.DeepClone(); clear["allergies"] = new JsonArray();
        host.Cloud.EnqueuePull("p3", false, FakeSyncServer.Change("patient", id, clear, rowVersion: 42));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.Empty(await AllergiesAsync(host, id));
    }

    // ---- amendments --------------------------------------------------------------------------------------------------

    private static async Task<(Guid ConsultationId, Guid BookingId)> CompletedVisitAsync(SyncTestHost host)
    {
        var session = await host.LoginAsync();
        var patient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Am", "End", new DateOnly(1979, 2, 2), SexType.Male));
        var bookingId = await host.Get<IVisitStarter>().StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var consults = host.Get<IConsultationService>();
        var draft = (await consults.LoadByBookingAsync(bookingId)).Draft;
        draft.ChiefComplaint = "Cough";
        draft.Subjective = "Dry cough.";
        draft.NoMedsConfirmed = true;
        draft.FollowUpMode = FollowUpMode.None;
        foreach (var v in draft.Vitals)
            v.Value = v.FormKey == "blood_pressure" ? "120/80" : v.FormKey == "pulse_rate" ? "72" : "1";
        draft.PfDecision = "Charge";
        draft.PfAmount = 500m;
        var done = await consults.CompleteAsync(draft);
        Assert.True(done.Succeeded, done.Error);
        return (done.Draft!.ConsultationId!.Value, bookingId);
    }

    private static async Task<ConsultationDraft> AmendDraftAsync(SyncTestHost host, Guid bookingId, string chiefComplaint)
    {
        var consults = host.Get<IConsultationService>();
        var draft = (await consults.LoadByBookingAsync(bookingId)).Draft;
        draft.ChiefComplaint = chiefComplaint;
        draft.NoMedsConfirmed = true;
        draft.FollowUpMode = FollowUpMode.None;
        var r = await consults.AmendAsync(draft);
        Assert.True(r.Succeeded, r.Error);
        return r.Draft!;
    }

    private static JsonObject ConsultOp(SyncTestHost host, int skip) =>
        host.Cloud.PushedOperations.Skip(skip).Last(o => o["aggregate"]!.GetValue<string>() == "consultation");

    [Fact]
    public async Task Amendment_is_queued_as_an_update_op_and_pushed_as_a_full_Amended_snapshot_with_the_last_seen_row_version()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (consultId, bookingId) = await CompletedVisitAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        long? seen;
        await using (var db = await host.DbAsync())
            seen = await SyncLocalStore.GetRowVersionAsync(db, "consultation", consultId.ToString());
        Assert.NotNull(seen);
        var before = host.Cloud.PushedOperations.Count;

        await AmendDraftAsync(host, bookingId, "Cough and fever");
        await using (var db = await host.DbAsync())
            Assert.Contains(await host.Get<ISyncOutbox>().ListPendingAsync(db, 50), p => p.Aggregate == "consultation" && p.OperationType == "update");

        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var op = ConsultOp(host, before);
        Assert.Equal("update", op["operation_type"]!.GetValue<string>());   // the cloud knows no "amend" verb; status Amended in the snapshot says it
        Assert.Equal(seen, op["base_row_version"]!.GetValue<long>());
        var data = op["payload"]!["data"]!.AsObject();
        Assert.Equal("Amended", data["consultation"]!["status"]!.GetValue<string>());
        Assert.Equal("Cough and fever", data["consultation"]!["chief_complaint"]!.GetValue<string>());
        Assert.Equal(consultId.ToString(), data["consultation"]!["consultation_id"]!.GetValue<string>());
        Assert.True(data.ContainsKey("diagnoses") && data.ContainsKey("prescription") && data.ContainsKey("follow_up"));
        Assert.NotNull(data["consultation"]!["completed_at"]);                  // completion stamps are untouched
        Assert.Equal(0, await host.PendingAsync());
    }

    [Fact]
    public async Task A_second_amendment_uses_the_row_version_returned_by_the_first()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (consultId, bookingId) = await CompletedVisitAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        await AmendDraftAsync(host, bookingId, "First amendment");
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        long? afterFirst;
        await using (var db = await host.DbAsync())
            afterFirst = await SyncLocalStore.GetRowVersionAsync(db, "consultation", consultId.ToString());
        var before = host.Cloud.PushedOperations.Count;
        await AmendDraftAsync(host, bookingId, "Second amendment");
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.Equal(afterFirst, ConsultOp(host, before)["base_row_version"]!.GetValue<long>());
    }

    [Fact]
    public async Task Stale_amendment_conflict_applies_the_cloud_copy_keeps_the_text_for_review_and_acks()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (consultId, bookingId) = await CompletedVisitAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var completedDoc = (JsonObject)ConsultOp(host, 0)["payload"]!["data"]!.DeepClone();

        host.Cloud.OnPush = op =>
        {
            if (op["aggregate"]!.GetValue<string>() != "consultation") return host.Cloud.Applied(op);
            var current = (JsonObject)completedDoc.DeepClone();
            current["consultation"]!["status"] = "Amended";
            current["consultation"]!["chief_complaint"] = "Cloud-side amendment";
            return host.Cloud.Applied(op, "conflict", "stale_amendment", current);
        };
        await AmendDraftAsync(host, bookingId, "My local amendment");
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);

        Assert.Equal(0, await host.PendingAsync());                              // conflict is acked, never retried
        await using var db = await host.DbAsync();
        var local = await db.Consultations.AsNoTracking().SingleAsync(c => c.ConsultationId == consultId);
        Assert.Equal("Cloud-side amendment", local.ChiefComplaint);              // pull then redo: cloud copy applied
        var review = Assert.Single(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.ReasonCode == "stale_amendment");
        Assert.Equal("conflict", review.Kind);
        Assert.Contains("My local amendment", review.LocalJson);                 // the doctor's text is not lost
        Assert.Contains("redo", review.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Payment_settled_conflict_is_acked_and_listed_with_the_local_text()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId) = await CompletedVisitAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var completedDoc = (JsonObject)ConsultOp(host, 0)["payload"]!["data"]!.DeepClone();
        host.Cloud.OnPush = op => op["aggregate"]!.GetValue<string>() == "consultation"
            ? host.Cloud.Applied(op, "conflict", "payment_settled", completedDoc.DeepClone())
            : host.Cloud.Applied(op);
        await AmendDraftAsync(host, bookingId, "Changed my mind");
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.Equal(0, await host.PendingAsync());
        await using var db = await host.DbAsync();
        var review = Assert.Single(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.ReasonCode == "payment_settled");
        Assert.Contains("Changed my mind", review.LocalJson);
        Assert.Contains("payment", review.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("duplicate", null)]
    [InlineData("rejected", "invalid_payload")]
    public async Task Duplicate_and_invalid_payload_amendment_results_are_acked(string status, string? reason)
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId) = await CompletedVisitAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        host.Cloud.OnPush = op => op["aggregate"]!.GetValue<string>() == "consultation" ? host.Cloud.Applied(op, status, reason) : host.Cloud.Applied(op);
        await AmendDraftAsync(host, bookingId, "Amended text");
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.Equal(0, await host.PendingAsync());
        await using var db = await host.DbAsync();
        var open = await SyncLocalStore.ListOpenConflictsAsync(db);
        if (status == "duplicate") Assert.Empty(open);
        else Assert.Contains(open, c => c.Kind == "rejected" && c.ReasonCode == "invalid_payload");
    }

    [Theory]
    [InlineData("amend", "update")]
    [InlineData("complete", "complete")]
    [InlineData("confirm-waiver", "confirm-waiver")]
    [InlineData("whatever", "update")]
    public void Unknown_operation_verbs_are_sent_as_update(string local, string wire) =>
        Assert.Equal(wire, ClinicNode.Sync.Wire.HttpCloudSyncClient.WireOperationType(local));

    // ---- pull: a cloud amendment reaches a finished local copy -------------------------------------------------

    [Fact]
    public async Task Pulled_cloud_amendment_updates_a_finished_local_consultation_but_a_plain_completed_copy_does_not()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (consultId, _) = await CompletedVisitAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var doc = (JsonObject)ConsultOp(host, 0)["payload"]!["data"]!.DeepClone();

        var completedCopy = (JsonObject)doc.DeepClone();
        completedCopy["consultation"]!["chief_complaint"] = "Should be ignored";
        host.Cloud.EnqueuePull("a1", false, FakeSyncServer.Change("consultation", consultId, completedCopy, rowVersion: 60));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        await using (var db = await host.DbAsync())
            Assert.Equal("Cough", (await db.Consultations.AsNoTracking().SingleAsync(c => c.ConsultationId == consultId)).ChiefComplaint);

        var amended = (JsonObject)doc.DeepClone();
        amended["consultation"]!["status"] = "Amended";
        amended["consultation"]!["chief_complaint"] = "Amended on the web";
        host.Cloud.EnqueuePull("a2", false, FakeSyncServer.Change("consultation", consultId, amended, rowVersion: 61));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        await using var db2 = await host.DbAsync();
        var local = await db2.Consultations.AsNoTracking().SingleAsync(c => c.ConsultationId == consultId);
        Assert.Equal("Amended on the web", local.ChiefComplaint);
        Assert.Equal(ConsultationStatus.Amended, local.Status);
    }
}
//JJQR - END
