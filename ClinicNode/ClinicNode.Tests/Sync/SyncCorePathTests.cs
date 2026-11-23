//JJQR - AI (Nov 21, 2026)
// Core push/pull path against the fake cloud: patients, queue (bookings), consultations, payments.
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

public class SyncCorePathTests
{
    private static async Task<(Guid PatientId, Guid BookingId, Guid ConsultationId)> MakeVisitAsync(SyncTestHost host)
    {
        var session = await host.LoginAsync();
        var patient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Ana", "Cruz", new DateOnly(1985, 3, 1), SexType.Female));
        var bookingId = await host.Get<IVisitStarter>().StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var consults = host.Get<IConsultationService>();
        var ws = await consults.LoadByBookingAsync(bookingId);
        ws.Draft.ChiefComplaint = "Cough for 3 days";
        var saved = await consults.SaveDraftAsync(ws.Draft);
        Assert.True(saved.Succeeded, saved.Error);
        return (patient.PatientId, bookingId, saved.Draft!.ConsultationId!.Value);
    }

    [Fact]
    public async Task Sync_push_sends_full_snapshots_with_protocol_header_and_acks_every_op()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (patientId, bookingId, consultId) = await MakeVisitAsync(host);
        var pendingBefore = await host.PendingAsync();
        Assert.True(pendingBefore >= 3, $"expected patient+booking+consultation ops, got {pendingBefore}");

        var result = await host.Get<ISyncService>().RunOnceAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(0, await host.PendingAsync());
        var ops = host.Cloud.PushedOperations.ToList();
        Assert.Equal(pendingBefore, ops.Count);
        Assert.All(ops, o => Assert.NotNull(o["operation_id"]));

        var patientOp = ops.First(o => o["aggregate"]!.GetValue<string>() == "patient");
        var pdata = patientOp["payload"]!["data"]!.AsObject();
        Assert.Equal(patientId.ToString(), pdata["patient_id"]!.GetValue<string>());
        Assert.Equal("Ana", pdata["first_name"]!.GetValue<string>());
        Assert.Equal("Female", pdata["sex"]!.GetValue<string>());
        Assert.Null(patientOp["base_row_version"]?.GetValue<long?>());

        var bookingOp = ops.First(o => o["aggregate"]!.GetValue<string>() == "booking");
        Assert.Equal(bookingId.ToString(), bookingOp["payload"]!["data"]!["booking"]!["booking_id"]!.GetValue<string>());
        Assert.NotNull(bookingOp["payload"]!["data"]!["booking_services"] as JsonArray);

        var consultOp = ops.Last(o => o["aggregate"]!.GetValue<string>() == "consultation");
        var cdoc = consultOp["payload"]!["data"]!.AsObject();
        Assert.Equal(consultId.ToString(), cdoc["consultation"]!["consultation_id"]!.GetValue<string>());
        Assert.Equal("Cough for 3 days", cdoc["consultation"]!["chief_complaint"]!.GetValue<string>());
        Assert.True(cdoc.ContainsKey("diagnoses") && cdoc.ContainsKey("prescription") && cdoc.ContainsKey("follow_up"));

        lock (host.Cloud.Requests)
        {
            var push = host.Cloud.Requests.First(r => r.RequestUri!.AbsolutePath == "/api/sync/push");
            Assert.Equal("1", push.Headers.GetValues("X-Sync-Protocol").Single());
            Assert.Equal("Bearer", push.Headers.Authorization!.Scheme);
        }

        // row_version from the push result becomes the next base_row_version
        await using var db = await host.DbAsync();
        Assert.NotNull(await SyncLocalStore.GetRowVersionAsync(db, "patient", patientId.ToString()));
        Assert.NotNull((await SyncOutbox.GetOrCreateIdentityAsync(db)).LastSuccessfulSyncAt);
    }

    [Fact]
    public async Task Sync_retry_ops_stay_pending_and_everything_else_is_acked()
    {
        await using var host = await SyncTestHost.StartAsync();
        await MakeVisitAsync(host);
        host.Cloud.OnPush = op => op["aggregate"]!.GetValue<string>() == "booking"
            ? host.Cloud.Applied(op, "retry", "missing_dependency")
            : op["aggregate"]!.GetValue<string>() == "consultation"
                ? host.Cloud.Applied(op, "rejected", "invalid_payload")
                : host.Cloud.Applied(op);

        var result = await host.Get<ISyncService>().RunOnceAsync();

        Assert.True(result.Succeeded, result.Error);
        await using var db = await host.DbAsync();
        var pending = await host.Get<ISyncOutbox>().ListPendingAsync(db);
        Assert.NotEmpty(pending);
        Assert.All(pending, p => Assert.Equal("booking", p.Aggregate));
        Assert.All(pending, p => Assert.Equal(1, p.Attempts));
        Assert.Contains(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.Kind == "rejected" && c.Aggregate == "consultation");
    }

    [Fact]
    public async Task Sync_retried_op_is_resent_with_the_same_operation_id()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Re", "Try", new DateOnly(1990, 1, 1), SexType.Male));
        var first = true;
        host.Cloud.OnPush = op => { var r = first ? host.Cloud.Applied(op, "retry") : host.Cloud.Applied(op); first = false; return r; };
        await host.Get<ISyncService>().RunOnceAsync();
        Assert.Equal(1, await host.PendingAsync());

        // backoff for attempt 1 is ~1s
        await Task.Delay(1300);
        await host.Get<ISyncService>().RunOnceAsync();
        Assert.Equal(0, await host.PendingAsync());
        var ids = host.Cloud.PushedOperations.Select(o => o["operation_id"]!.GetValue<string>()).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(ids[0], ids[1]);
    }

    [Fact]
    public async Task Sync_pull_applies_changes_and_persists_the_cursor()
    {
        await using var host = await SyncTestHost.StartAsync();
        Guid patientId;
        await using (var db = await host.DbAsync())
            patientId = (await db.Patients.FirstAsync()).PatientId;

        var medicineId = Guid.NewGuid();
        host.Cloud.EnqueuePull("10", true,
            FakeSyncServer.Change("patient", patientId, new JsonObject
            {
                ["patient_id"] = patientId.ToString(), ["first_name"] = "Cloudy", ["updated_at"] = "2026-11-21T05:00:00+00:00",
            }, rowVersion: 4));
        host.Cloud.EnqueuePull("11", false,
            FakeSyncServer.Change("medicine", medicineId, new JsonObject
            {
                ["medicine_id"] = medicineId.ToString(), ["generic_name"] = "Zzz-cloud-only-medicine",
            }));

        var result = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(2, result.Pulled);

        await using (var db = await host.DbAsync())
        {
            var p = await db.Patients.SingleAsync(x => x.PatientId == patientId);
            Assert.Equal("Cloudy", p.FirstName);
            Assert.Equal(DateTimeOffset.Parse("2026-11-21T05:00:00+00:00"), p.UpdatedAt); // cloud updated_at kept
            Assert.True(await db.Medicines.AnyAsync(m => m.MedicineId == medicineId));
            Assert.Equal("11", await SyncOutbox.GetCursorAsync(db, SyncService.CloudStream));
            Assert.Equal(4, await SyncLocalStore.GetRowVersionAsync(db, "patient", patientId.ToString()));
            Assert.Equal(DateTimeOffset.Parse("2026-11-21T06:15:00+00:00"), (await SyncOutbox.GetOrCreateIdentityAsync(db)).LastSuccessfulSyncAt);
        }

        await host.Get<ISyncService>().RunOnceAsync();
        Assert.Equal(new[] { "", "10", "11" }, host.Cloud.PullCursors.ToArray());
    }

    [Fact]
    public async Task Sync_merged_push_replaces_a_temporary_patient_code_and_lists_it_for_review()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        var patient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Tem", "Porary", new DateOnly(1980, 2, 2), SexType.Male));
        await using (var db = await host.DbAsync())
        {
            var p = await db.Patients.SingleAsync(x => x.PatientId == patient.PatientId);
            p.PatientCode = "MF-T-0001";
            await db.SaveChangesAsync();
        }
        host.Cloud.OnPush = op =>
        {
            var current = op["payload"]!["data"]!.DeepClone().AsObject();
            current["patient_code"] = "MF-009999";
            var r = host.Cloud.Applied(op, "merged", null, current);
            r["conflict_fields"] = new JsonArray("patient_code");
            return r;
        };

        var result = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(result.Succeeded, result.Error);

        await using var db2 = await host.DbAsync();
        Assert.Equal("MF-009999", (await db2.Patients.SingleAsync(x => x.PatientId == patient.PatientId)).PatientCode);
        var conflicts = await SyncLocalStore.ListOpenConflictsAsync(db2);
        Assert.Contains(conflicts, c => c.Kind == "patient_code_replaced" && c.ReasonCode == "temporary_code_replaced");
        Assert.Contains(conflicts, c => c.Kind == "merged");
        Assert.Equal(0, await host.PendingAsync());
    }

    [Fact]
    public async Task Sync_pull_patient_code_replacement_is_applied_and_flagged()
    {
        await using var host = await SyncTestHost.StartAsync();
        Guid id;
        await using (var db = await host.DbAsync())
        {
            var p = await db.Patients.FirstAsync();
            p.PatientCode = "MF-T-0042";
            await db.SaveChangesAsync();
            id = p.PatientId;
        }
        host.Cloud.EnqueuePull("5", false, FakeSyncServer.Change("patient", id, new JsonObject
        {
            ["patient_id"] = id.ToString(), ["patient_code"] = "MF-000777",
        }));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        await using var db2 = await host.DbAsync();
        Assert.Equal("MF-000777", (await db2.Patients.SingleAsync(x => x.PatientId == id)).PatientCode);
        Assert.Contains(await SyncLocalStore.ListOpenConflictsAsync(db2), c => c.Kind == "patient_code_replaced");
    }

    [Fact]
    public async Task Sync_pull_never_changes_a_finished_consultation()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId, consultId) = await MakeVisitAsync(host);
        await host.Get<ISyncService>().RunOnceAsync(); // push, so nothing is pending
        await using (var db = await host.DbAsync())
        {
            var c = await db.Consultations.SingleAsync(x => x.ConsultationId == consultId);
            c.Status = ConsultationStatus.Completed;
            await db.SaveChangesAsync();
        }
        host.Cloud.EnqueuePull("7", false, FakeSyncServer.Change("consultation", consultId, new JsonObject
        {
            ["consultation"] = new JsonObject { ["consultation_id"] = consultId.ToString(), ["chief_complaint"] = "EDITED IN CLOUD", ["status"] = "Draft" },
            ["diagnoses"] = new JsonArray(), ["lab_orders"] = new JsonArray(), ["vaccinations"] = new JsonArray(),
        }));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);
        await using var db2 = await host.DbAsync();
        var after = await db2.Consultations.SingleAsync(x => x.ConsultationId == consultId);
        Assert.Equal("Cough for 3 days", after.ChiefComplaint);
        Assert.Equal(ConsultationStatus.Completed, after.Status);
    }

    [Fact]
    public async Task Sync_pull_does_not_overwrite_a_draft_with_unpushed_local_changes_but_updates_a_synced_draft()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, _, consultId) = await MakeVisitAsync(host);
        host.Cloud.Offline = true;
        await host.Get<ISyncService>().RunOnceAsync(); // stays pending
        host.Cloud.Offline = false;
        host.Cloud.OnPush = op => host.Cloud.Applied(op, "retry"); // keep it pending through this cycle
        host.Cloud.EnqueuePull("8", false, FakeSyncServer.Change("consultation", consultId, new JsonObject
        {
            ["consultation"] = new JsonObject { ["consultation_id"] = consultId.ToString(), ["chief_complaint"] = "cloud" },
        }));
        await host.Get<ISyncService>().RunOnceAsync();
        await using (var db = await host.DbAsync())
            Assert.Equal("Cough for 3 days", (await db.Consultations.SingleAsync(x => x.ConsultationId == consultId)).ChiefComplaint);

        // once pushed (acked), a cloud edit to the still-draft consultation is applied
        host.Cloud.OnPush = null;
        await Task.Delay(1300);
        host.Cloud.EnqueuePull("9", false, FakeSyncServer.Change("consultation", consultId, new JsonObject
        {
            ["consultation"] = new JsonObject { ["consultation_id"] = consultId.ToString(), ["chief_complaint"] = "cloud" },
            ["diagnoses"] = new JsonArray(), ["lab_orders"] = new JsonArray(), ["vaccinations"] = new JsonArray(),
            ["prescription"] = null, ["follow_up"] = null, ["medical_certificate"] = null,
        }));
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        Assert.Equal(0, await host.PendingAsync());
        await using (var db = await host.DbAsync())
            Assert.Equal("cloud", (await db.Consultations.SingleAsync(x => x.ConsultationId == consultId)).ChiefComplaint);
    }

    [Fact]
    public async Task Sync_payment_is_single_writer_and_cloud_edits_are_flagged_not_merged()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId, _) = await MakeVisitAsync(host);
        await host.Get<ISyncService>().RunOnceAsync();
        ClinicApp.Domain.Entities.Payment local;
        await using (var db = await host.DbAsync())
            local = await db.Payments.SingleAsync(p => p.BookingId == bookingId);

        host.Cloud.EnqueuePull("3", false, FakeSyncServer.Change("payment", local.PaymentId, new JsonObject
        {
            ["payment_id"] = local.PaymentId.ToString(), ["booking_id"] = bookingId.ToString(),
            ["amount"] = local.Amount + 100m, ["status"] = "Paid",
        }));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        await using var db2 = await host.DbAsync();
        var after = await db2.Payments.SingleAsync(p => p.PaymentId == local.PaymentId);
        Assert.Equal(local.Amount, after.Amount);
        Assert.Equal(local.Status, after.Status);
        Assert.Contains(await SyncLocalStore.ListOpenConflictsAsync(db2), c => c.Kind == "cloud_edit_flagged" && c.Aggregate == "payment");
    }

    //JJQR - AI (Nov 23, 2026) - offline audit (the owner's real database): an identical payment coming back from the server was flagged "cloud copy differs" because of timestamp precision (4 vs 7 decimals) and 500 vs 500.0
    [Fact]
    public async Task An_identical_cloud_payment_with_only_formatting_differences_is_not_flagged()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId, _) = await MakeVisitAsync(host);
        await host.Get<ISyncService>().RunOnceAsync();
        var confirmed = new DateTimeOffset(2026, 11, 23, 5, 9, 10, TimeSpan.Zero).AddTicks(5_329_904);
        ClinicApp.Domain.Entities.Payment local;
        await using (var db = await host.DbAsync())
        {
            local = await db.Payments.SingleAsync(p => p.BookingId == bookingId);
            local.Status = ClinicApp.Domain.Enums.PaymentStatus.Paid;
            local.AmountReceived = local.Amount;
            local.ConfirmedAt = confirmed;
            await db.SaveChangesAsync();
        }

        host.Cloud.EnqueuePull("tz1", false, FakeSyncServer.Change("payment", local.PaymentId, new JsonObject
        {
            ["payment_id"] = local.PaymentId.ToString(), ["booking_id"] = bookingId.ToString(),
            ["amount"] = (double)local.Amount, ["status"] = "Paid", ["confirmed_at"] = "2026-11-23T05:09:10.5329904+00:00",
        }));
        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        await using var db2 = await host.DbAsync();
        Assert.DoesNotContain(await SyncLocalStore.ListOpenConflictsAsync(db2), c => c.Kind == "cloud_edit_flagged" && c.Aggregate == "payment");
    }
    //JJQR - END

    //JJQR - AI (Nov 23, 2026) - the owner's Sync review showed 4 items whose "this computer" and "clinic server" columns were the same values; pressing Sync did not clear them
    [Fact]
    public async Task Identical_review_items_clear_themselves_at_the_next_sync_and_real_differences_stay()
    {
        await using var host = await SyncTestHost.StartAsync();
        await using (var db = await host.DbAsync())
        {
            await SyncLocalStore.EnsureSchemaAsync(db);
            await SyncLocalStore.AddConflictAsync(db, new ClinicNode.Sync.Local.SyncConflict
            {
                Aggregate = "payment", EntityId = Guid.NewGuid().ToString(), Kind = "cloud_edit_flagged", ReasonCode = "not_owner", Message = "same",
                LocalJson = """{"payment_id":"5472f697-3589-4438-bcef-936da1f5645f","amount":500,"amount_received":500,"status":"Paid","confirmed_at":"2026-11-23T05:09:10.5329+00:00","origin_node_id":null,"row_version":0}""",
                CloudJson = """{"payment_id":"5472f697-3589-4438-bcef-936da1f5645f","amount":500.00,"amount_received":500.00,"status":"Paid","confirmed_at":"2026-11-23T05:09:10.5329904+00:00","origin_node_id":"64496148-6ba6-4eab-86c9-adc8e336351f","row_version":3}""",
            });
            await SyncLocalStore.AddConflictAsync(db, new ClinicNode.Sync.Local.SyncConflict
            {
                Aggregate = "consultation", EntityId = Guid.NewGuid().ToString(), Kind = "conflict", ReasonCode = "finished_immutable", Message = "same doc",
                LocalJson = """{"consultation":{"status":"Completed","plan":"rest","pf_amount":500,"completed_at":"2026-11-23T05:03:31.2301+00:00","row_version":0},"diagnoses":[]}""",
                CloudJson = """{"consultation":{"status":"Completed","plan":"rest","pf_amount":500.00,"completed_at":"2026-11-23T05:03:31.2301000+00:00","row_version":2},"diagnoses":[]}""",
            });
            await SyncLocalStore.AddConflictAsync(db, new ClinicNode.Sync.Local.SyncConflict
            {
                Aggregate = "payment", EntityId = Guid.NewGuid().ToString(), Kind = "cloud_edit_flagged", ReasonCode = "not_owner", Message = "really different",
                LocalJson = """{"payment_id":"11111111-1111-1111-1111-111111111111","amount":500,"status":"Paid"}""",
                CloudJson = """{"payment_id":"11111111-1111-1111-1111-111111111111","amount":450,"status":"Paid"}""",
            });
        }

        Assert.True((await host.Get<ISyncService>().RunOnceAsync()).Succeeded);

        await using var db2 = await host.DbAsync();
        var open = await SyncLocalStore.ListOpenConflictsAsync(db2);
        var left = Assert.Single(open);
        Assert.Equal("really different", left.Message);
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - real-DB finding: demo bookings share ids with the cloud but payments do not (payments.booking_id is UNIQUE)
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cloud_payment_with_another_id_for_the_same_booking_replaces_only_an_untouched_local_one(bool localPaid)
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId, _) = await MakeVisitAsync(host);
        await host.Get<ISyncService>().RunOnceAsync();
        Guid localId;
        await using (var db = await host.DbAsync())
        {
            var p = await db.Payments.SingleAsync(x => x.BookingId == bookingId);
            localId = p.PaymentId;
            p.Status = localPaid ? ClinicApp.Domain.Enums.PaymentStatus.Paid : ClinicApp.Domain.Enums.PaymentStatus.Unpaid;
            await db.SaveChangesAsync();
        }
        var cloudId = Guid.NewGuid();
        host.Cloud.EnqueuePull("3", false, FakeSyncServer.Change("payment", cloudId, new JsonObject
        {
            ["payment_id"] = cloudId.ToString(), ["booking_id"] = bookingId.ToString(), ["amount"] = 500m, ["status"] = "Unpaid",
        }));
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);

        await using var db2 = await host.DbAsync();
        var kept = await db2.Payments.SingleAsync(x => x.BookingId == bookingId);
        var review = await SyncLocalStore.ListOpenConflictsAsync(db2);
        Assert.DoesNotContain(review, c => c.Kind == "apply_failed");
        if (localPaid)
        {
            Assert.Equal(localId, kept.PaymentId);
            Assert.Contains(review, c => c.ReasonCode == "payment_booking_clash");
        }
        else Assert.Equal(cloudId, kept.PaymentId);
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - real-DB finding: a cloud consultation with another id for the same booking is flagged, not an apply_failed
    [Fact]
    public async Task A_cloud_consultation_with_another_id_for_the_same_booking_is_flagged_and_local_kept()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId, consultId) = await MakeVisitAsync(host);
        await host.Get<ISyncService>().RunOnceAsync();
        var cloudId = Guid.NewGuid();
        host.Cloud.EnqueuePull("4", false, FakeSyncServer.Change("consultation", cloudId, new JsonObject
        {
            ["consultation"] = new JsonObject { ["consultation_id"] = cloudId.ToString(), ["booking_id"] = bookingId.ToString(), ["chief_complaint"] = "cloud", ["status"] = "Completed" },
            ["diagnoses"] = new JsonArray(),
        }));
        var r = await host.Get<ISyncService>().RunOnceAsync();
        Assert.True(r.Succeeded, r.Error);
        await using var db = await host.DbAsync();
        Assert.Equal(consultId, (await db.Consultations.SingleAsync(x => x.BookingId == bookingId)).ConsultationId);
        var review = await SyncLocalStore.ListOpenConflictsAsync(db);
        Assert.DoesNotContain(review, c => c.Kind == "apply_failed");
        Assert.Contains(review, c => c.ReasonCode == "consultation_booking_clash");
    }
    //JJQR - END

    [Fact]
    public async Task Sync_booking_status_keeps_the_furthest_legal_state()
    {
        await using var host = await SyncTestHost.StartAsync();
        var (_, bookingId, _) = await MakeVisitAsync(host);
        await host.Get<ISyncService>().RunOnceAsync();
        BookingStatus localStatus;
        await using (var db = await host.DbAsync())
            localStatus = (await db.Bookings.SingleAsync(b => b.BookingId == bookingId)).Status;

        // cloud behind (Confirmed) -> keep local; cloud ahead (Completed) -> take cloud
        host.Cloud.EnqueuePull("1", false, FakeSyncServer.Change("booking", bookingId, new JsonObject
        {
            ["booking"] = new JsonObject { ["booking_id"] = bookingId.ToString(), ["status"] = "Pending" },
        }));
        await host.Get<ISyncService>().RunOnceAsync();
        await using (var db = await host.DbAsync())
        {
            var s = (await db.Bookings.SingleAsync(b => b.BookingId == bookingId)).Status;
            Assert.Equal(localStatus, s);
        }

        host.Cloud.EnqueuePull("2", false, FakeSyncServer.Change("booking", bookingId, new JsonObject
        {
            ["booking"] = new JsonObject { ["booking_id"] = bookingId.ToString(), ["status"] = "Completed" },
        }));
        await host.Get<ISyncService>().RunOnceAsync();
        await using (var db = await host.DbAsync())
            Assert.Equal(BookingStatus.Completed, (await db.Bookings.SingleAsync(b => b.BookingId == bookingId)).Status);
    }

    [Fact]
    public async Task Sync_offline_keeps_the_outbox_and_reports_offline()
    {
        await using var host = await SyncTestHost.StartAsync();
        await MakeVisitAsync(host);
        host.Cloud.Offline = true;
        var result = await host.Get<ISyncService>().RunOnceAsync();
        Assert.False(result.Succeeded);
        Assert.Equal(SyncFailureKind.Offline, result.Failure);
        var status = host.Get<ISyncStatus>();
        Assert.Equal(SyncPhase.Offline, status.Phase);
        Assert.False(status.IsOnline);
        Assert.True(status.PendingCount >= 3);
        Assert.Contains("waiting to sync", status.Banner);
        await using var db = await host.DbAsync();
        Assert.All(await host.Get<ISyncOutbox>().ListPendingAsync(db), p => Assert.Equal(0, p.Attempts)); // transport failures do not burn attempts
    }
}
//JJQR - END
