//JJQR - AI (Nov 21, 2026)
// Part B: queue-number reprint signal (§5.2), bulk catalog pull (icd10 ~12k rows), automatic single stale_amendment retry.
using System.Diagnostics;
using System.Text.Json.Nodes;
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

public class SyncQueueCatalogRetryTests
{
    private static JsonObject Change(string aggregate, string entityId, JsonNode data, long rv) => new()
    {
        ["aggregate"] = aggregate, ["entity_id"] = entityId, ["operation"] = "upsert", ["row_version"] = rv,
        ["origin_node_id"] = null, ["changed_at"] = "2026-11-21T06:00:00+00:00", ["data"] = data,
    };

    private static async Task<(Guid PatientId, Guid BookingId)> WalkInAsync(SyncTestHost host)
    {
        var session = await host.LoginAsync();
        var p = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Que", "Ue", new DateOnly(1990, 3, 3), SexType.Female));
        var booking = await host.Get<IVisitStarter>().StartWalkInConsultAsync(p.PatientId, session.DoctorId!.Value, null);
        return (p.PatientId, booking);
    }

    // ---- queue numbers ---------------------------------------------------------------------------------------------

    //JJQR - AI (Nov 23, 2026) - offline audit: these two tests still expected the OLD rule ("a renumbered queue slip raises a reprint flag"). The product rule changed on 21 Nov (see AggregateDocuments, REPRINT COPY): the number already printed stays valid,
    // the server keeps it and only audits the clash, so there is NO reprint flag; the clash shows as a review item. They now check the current rule.
    [Fact]
    public async Task A_queue_number_clash_on_push_updates_the_booking_but_demands_no_slip_reprint()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId) = await WalkInAsync(host);

        host.Cloud.OnPush = op =>
        {
            if (op["aggregate"]!.GetValue<string>() != "booking") return host.Cloud.Applied(op);
            var current = (JsonObject)op["payload"]!["data"]!.DeepClone();
            current["booking"]!["queue_number"] = "Q-777";
            var r = host.Cloud.Applied(op, "merged", "queue_number_reassigned", current);
            r["conflict_fields"] = new JsonArray("queue_number");
            return r;
        };
        var status = host.Get<ISyncStatus>();
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        await using (var db = await host.DbAsync())
            Assert.Equal("Q-777", (await db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == bookingId)).QueueNumber);
        Assert.Empty(await status.GetReprintFlagsAsync());
        Assert.Equal(0, status.ReprintCount);
        // the clash is still explained to the admin as a review item
        await using (var db = await host.DbAsync())
            Assert.Contains(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.ReasonCode == "queue_number_reassigned");
    }

    [Fact]
    public async Task A_cloud_queue_number_arriving_on_pull_is_applied_without_a_reprint_demand()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId) = await WalkInAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);       // nothing pending now
        var doc = (JsonObject)host.Cloud.PushedOperations.Last(o => o["aggregate"]!.GetValue<string>() == "booking")["payload"]!["data"]!.DeepClone();
        doc["booking"]!["queue_number"] = "Q-555";
        host.Cloud.EnqueuePull("q1", false, Change("booking", bookingId.ToString(), doc, 30));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        Assert.Empty(await host.Get<ISyncStatus>().GetReprintFlagsAsync());
        await using var db = await host.DbAsync();
        Assert.Equal("Q-555", (await db.Bookings.AsNoTracking().SingleAsync(b => b.BookingId == bookingId)).QueueNumber);
    }
    //JJQR - END

    [Fact]
    public async Task A_replaced_patient_code_signals_a_patient_card_reprint()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (patientId, _) = await WalkInAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var doc = (JsonObject)host.Cloud.PushedOperations.First(o => o["aggregate"]!.GetValue<string>() == "patient")["payload"]!["data"]!.DeepClone();
        doc["patient_code"] = "MF-900001";
        host.Cloud.EnqueuePull("p1", false, Change("patient", patientId.ToString(), doc, 31));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var flag = Assert.Single(await host.Get<ISyncStatus>().GetReprintFlagsAsync());
        Assert.Equal("patient_card", flag.Kind);
        Assert.Equal("MF-900001", flag.NewValue);
    }

    // ---- catalogs --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Five_catalog_aggregates_are_applied_not_reported_as_unknown()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        Guid doctorId; Guid medicineId;
        await using (var db = await host.DbAsync())
        {
            doctorId = await db.Doctors.Select(d => d.DoctorId).FirstAsync();
            medicineId = Guid.NewGuid();
        }
        var annId = Guid.NewGuid(); var dayId = Guid.NewGuid(); var copyId = Guid.NewGuid();
        host.Cloud.EnqueuePull("c1", false,
            Change("icd10_code", "J45.9", new JsonObject { ["code"] = "J45.9", ["description"] = "Asthma, unspecified", ["level"] = 3, ["is_leaf"] = true }, 5),
            Change("announcement", annId.ToString(), new JsonObject { ["id"] = annId.ToString(), ["title"] = "Closed Monday", ["body"] = "Holiday", ["is_active"] = true,
                ["created_at"] = "2026-11-21T00:00:00+00:00", ["updated_at"] = "2026-11-21T00:00:00+00:00" }, 6),
            Change("doctor_day_status", dayId.ToString(), new JsonObject { ["id"] = dayId.ToString(), ["doctor_id"] = doctorId.ToString(), ["status_date"] = "2026-11-24",
                ["status"] = "RunningLate", ["running_late_minutes"] = 15, ["created_at"] = "2026-11-21T00:00:00+00:00", ["updated_at"] = "2026-11-21T00:00:00+00:00" }, 7),
            Change("doctor_builtin_template_copy", copyId.ToString(), new JsonObject { ["id"] = copyId.ToString(), ["doctor_id"] = doctorId.ToString(), ["kind"] = "soap", ["builtin_key"] = "cough", ["copied_at"] = "2026-11-21T00:00:00+00:00" }, 8));
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        await using var db2 = await host.DbAsync();
        var review = (await SyncLocalStore.ListOpenConflictsAsync(db2)).Select(c => $"{c.Kind}/{c.ReasonCode}/{c.Aggregate}: {c.Message}").ToList();
        Assert.True(review.Count == 0, string.Join(" | ", review));
        Assert.Equal("Asthma, unspecified", (await db2.Icd10Codes.AsNoTracking().SingleAsync(c => c.Code == "J45.9")).Description);
        Assert.Equal("Closed Monday", (await db2.Announcements.AsNoTracking().SingleAsync(a => a.Id == annId)).Title);
        Assert.Equal(15, (await db2.DoctorDayStatuses.AsNoTracking().SingleAsync(d => d.Id == dayId)).RunningLateMinutes);
        Assert.Equal("cough", (await db2.DoctorBuiltinTemplateCopies.AsNoTracking().SingleAsync(c => c.Id == copyId)).BuiltinKey);
    }

    [Fact]
    public async Task A_cloud_row_replaces_a_local_row_with_the_same_natural_key_but_another_id()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        Guid doctorId; var localId = Guid.NewGuid(); var cloudId = Guid.NewGuid();
        await using (var db = await host.DbAsync())
        {
            doctorId = await db.Doctors.Select(d => d.DoctorId).FirstAsync();
            db.DoctorBuiltinTemplateCopies.RemoveRange(db.DoctorBuiltinTemplateCopies.Where(c => c.DoctorId == doctorId && c.Kind == "soap" && c.BuiltinKey == "clash"));
            db.DoctorBuiltinTemplateCopies.Add(new ClinicApp.Domain.Entities.DoctorBuiltinTemplateCopy { Id = localId, DoctorId = doctorId, Kind = "soap", BuiltinKey = "clash", CopiedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        host.Cloud.EnqueuePull("u1", false, Change("doctor_builtin_template_copy", cloudId.ToString(), new JsonObject
            { ["id"] = cloudId.ToString(), ["doctor_id"] = doctorId.ToString(), ["kind"] = "soap", ["builtin_key"] = "clash", ["copied_at"] = "2026-11-21T00:00:00+00:00" }, 9));
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        await using var db2 = await host.DbAsync();
        Assert.Empty(await SyncLocalStore.ListOpenConflictsAsync(db2));
        var rows = await db2.DoctorBuiltinTemplateCopies.AsNoTracking().Where(c => c.DoctorId == doctorId && c.Kind == "soap" && c.BuiltinKey == "clash").ToListAsync();
        Assert.Equal(cloudId, Assert.Single(rows).Id);
    }

    [Fact]
    public async Task A_big_icd10_pull_is_applied_in_bulk_and_fast_and_updates_existing_rows()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        JsonObject Row(int i, string suffix = "") => Change("icd10_code", $"ZZT{i:00000}", new JsonObject
            { ["code"] = $"ZZT{i:00000}", ["description"] = $"Test code {i}{suffix}", ["level"] = 4, ["is_leaf"] = true }, 1000 + i);
        const int n = 2400;
        host.Cloud.EnqueuePull("b1", true, Enumerable.Range(0, 1000).Select(i => Row(i)).ToArray());
        host.Cloud.EnqueuePull("b2", true, Enumerable.Range(1000, 1000).Select(i => Row(i)).ToArray());
        host.Cloud.EnqueuePull("b3", false, Enumerable.Range(2000, n - 2000).Select(i => Row(i)).Concat(new[] { Row(5, " (edited)") }).ToArray());

        var sw = Stopwatch.StartNew();
        var r = await host.Get<ISyncService>().RunOnceAsync();
        sw.Stop();
        Assert.True(r.Succeeded, r.Error);
        Assert.Equal(n + 1, r.Pulled);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"bulk pull took {sw.Elapsed}");

        await using var db = await host.DbAsync();
        Assert.Equal(n, await db.Icd10Codes.CountAsync(c => c.Code.StartsWith("ZZT")));
        Assert.Equal("Test code 5 (edited)", (await db.Icd10Codes.AsNoTracking().SingleAsync(c => c.Code == "ZZT00005")).Description);   // later change in the page wins
        Assert.Equal(1005, await SyncLocalStore.GetRowVersionAsync(db, "icd10_code", "ZZT00005"));
        Assert.Equal(1000 + n - 1, await SyncLocalStore.GetRowVersionAsync(db, "icd10_code", $"ZZT{n - 1:00000}"));
        Assert.Empty(await SyncLocalStore.ListOpenConflictsAsync(db));
        Assert.Equal(new[] { "b1", "b2", "b3" }.Length, host.Cloud.PullCursors.Count(c => c is not null) );

        // a second pull of changed rows updates them in place
        host.Cloud.EnqueuePull("b4", false, Enumerable.Range(0, 60).Select(i => Row(i, " v2")).ToArray());
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        await using var db2 = await host.DbAsync();
        Assert.Equal(n, await db2.Icd10Codes.CountAsync(c => c.Code.StartsWith("ZZT")));
        Assert.Equal("Test code 7 v2", (await db2.Icd10Codes.AsNoTracking().SingleAsync(c => c.Code == "ZZT00007")).Description);
    }

    [Fact]
    public async Task Pull_page_size_is_1000()
    {
        await using var host = await SyncTestHost.StartAsync();
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        lock (host.Cloud.Requests)
            Assert.Contains(host.Cloud.Requests, r => r.RequestUri!.AbsolutePath == "/api/sync/pull" && r.RequestUri.Query.Contains("limit=1000"));
    }

    // ---- stale amendment: one automatic retry -----------------------------------------------------------------------

    private static async Task<(Guid ConsultationId, Guid BookingId)> CompletedVisitAsync(SyncTestHost host)
    {
        var session = await host.LoginAsync();
        var patient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Re", "Try", new DateOnly(1981, 1, 1), SexType.Male));
        var bookingId = await host.Get<IVisitStarter>().StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var consults = host.Get<IConsultationService>();
        var draft = (await consults.LoadByBookingAsync(bookingId)).Draft;
        draft.ChiefComplaint = "Cough"; draft.Subjective = "Dry."; draft.NoMedsConfirmed = true; draft.FollowUpMode = FollowUpMode.None;
        foreach (var v in draft.Vitals) v.Value = v.FormKey == "blood_pressure" ? "120/80" : v.FormKey == "pulse_rate" ? "72" : "1";
        draft.PfDecision = "Charge"; draft.PfAmount = 500m;
        var done = await consults.CompleteAsync(draft);
        Assert.True(done.Succeeded, done.Error);
        return (done.Draft!.ConsultationId!.Value, bookingId);
    }

    private static async Task AmendAsync(SyncTestHost host, Guid bookingId, string text)
    {
        var consults = host.Get<IConsultationService>();
        var draft = (await consults.LoadByBookingAsync(bookingId)).Draft;
        draft.ChiefComplaint = text; draft.NoMedsConfirmed = true; draft.FollowUpMode = FollowUpMode.None;
        var r = await consults.AmendAsync(draft);
        Assert.True(r.Succeeded, r.Error);
    }

    [Fact]
    public async Task Stale_amendment_is_pulled_reapplied_and_pushed_again_once_in_the_same_cycle()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (consultId, bookingId) = await CompletedVisitAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var completedDoc = (JsonObject)host.Cloud.PushedOperations.Last(o => o["aggregate"]!.GetValue<string>() == "consultation")["payload"]!["data"]!.DeepClone();
        var before = host.Cloud.PushedOperations.Count;

        var staleRowVersion = 0L;
        var attempts = 0;
        host.Cloud.OnPush = op =>
        {
            if (op["aggregate"]!.GetValue<string>() != "consultation") return host.Cloud.Applied(op);
            attempts++;
            if (attempts == 1)
            {
                var current = (JsonObject)completedDoc.DeepClone();
                current["consultation"]!["chief_complaint"] = "Cloud audit-only edit";
                var stale = host.Cloud.Applied(op, "conflict", "stale_amendment", current);
                stale["row_version"] = staleRowVersion = 500;
                return stale;
            }
            return host.Cloud.Applied(op);
        };
        await AmendAsync(host, bookingId, "Doctor amendment");
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);

        var consultOps = host.Cloud.PushedOperations.Skip(before).Where(o => o["aggregate"]!.GetValue<string>() == "consultation").ToList();
        Assert.Equal(2, consultOps.Count);
        Assert.Equal(500, consultOps[1]["base_row_version"]!.GetValue<long>());            // the new base_row_version
        Assert.Equal("Doctor amendment", consultOps[1]["payload"]!["data"]!["consultation"]!["chief_complaint"]!.GetValue<string>());
        Assert.Equal("Amended", consultOps[1]["payload"]!["data"]!["consultation"]!["status"]!.GetValue<string>());
        Assert.Equal(0, await host.PendingAsync());
        await using var db = await host.DbAsync();
        Assert.Empty(await SyncLocalStore.ListOpenConflictsAsync(db));                       // retry worked: nothing to review
        var local = await db.Consultations.AsNoTracking().SingleAsync(c => c.ConsultationId == consultId);
        Assert.Equal("Doctor amendment", local.ChiefComplaint);
        Assert.Equal(ConsultationStatus.Amended, local.Status);
        _ = staleRowVersion;
    }

    [Fact]
    public async Task A_second_stale_amendment_is_not_retried_again_and_goes_on_the_review_list()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (consultId, bookingId) = await CompletedVisitAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var completedDoc = (JsonObject)host.Cloud.PushedOperations.Last(o => o["aggregate"]!.GetValue<string>() == "consultation")["payload"]!["data"]!.DeepClone();
        var before = host.Cloud.PushedOperations.Count;
        host.Cloud.OnPush = op => op["aggregate"]!.GetValue<string>() == "consultation"
            ? host.Cloud.Applied(op, "conflict", "stale_amendment", completedDoc.DeepClone())
            : host.Cloud.Applied(op);
        await AmendAsync(host, bookingId, "Doctor amendment");
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        Assert.Equal(2, host.Cloud.PushedOperations.Skip(before).Count(o => o["aggregate"]!.GetValue<string>() == "consultation"));   // original + ONE retry
        Assert.Equal(0, await host.PendingAsync());
        await using var db = await host.DbAsync();
        var review = Assert.Single(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.ReasonCode == "stale_amendment");
        Assert.Contains("automatic retry", review.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Doctor amendment", review.LocalJson);
    }

    [Fact]
    public async Task Payment_settled_is_not_retried()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId) = await CompletedVisitAsync(host);
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        var completedDoc = (JsonObject)host.Cloud.PushedOperations.Last(o => o["aggregate"]!.GetValue<string>() == "consultation")["payload"]!["data"]!.DeepClone();
        var before = host.Cloud.PushedOperations.Count;
        host.Cloud.OnPush = op => op["aggregate"]!.GetValue<string>() == "consultation"
            ? host.Cloud.Applied(op, "conflict", "payment_settled", completedDoc.DeepClone())
            : host.Cloud.Applied(op);
        await AmendAsync(host, bookingId, "Changed fee decision");
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        Assert.Equal(1, host.Cloud.PushedOperations.Skip(before).Count(o => o["aggregate"]!.GetValue<string>() == "consultation"));
        await using var db = await host.DbAsync();
        Assert.Single(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.ReasonCode == "payment_settled");
    }
}
//JJQR - END
