//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.1 (desktop side): refused and stuck changes go up to the server's review list once, without patient data
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;

namespace ClinicNode.Tests.Sync;

public class ReviewUploadTests
{
    private static async Task AddChangesAsync(SyncTestHost host, string last)
    {
        await host.LoginAsync();
        for (var i = 0; i < 3; i++)
            await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Up" + i, last, new DateOnly(1990, 1, 1 + i), SexType.Female));
    }

    [Fact]
    public async Task A_refused_change_is_uploaded_once_with_a_short_description_and_no_patient_data()
    {
        await using var host = await SyncTestHost.StartAsync();
        await AddChangesAsync(host, "Refused");
        host.Cloud.OnPush = op => host.Cloud.Applied(op, "rejected", "invalid_payload");
        var svc = host.Get<ISyncService>();

        var r = await svc.RunManualAsync();
        Assert.True(r.Succeeded, r.Error);
        Assert.True(r.Rejected >= 3);
        Assert.Equal(r.Rejected, host.Cloud.ReviewUploads.Count);
        var first = host.Cloud.ReviewUploads[0];
        Assert.Equal("change_rejected", first["kind"]!.GetValue<string>());
        Assert.Equal("patient", first["aggregate"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(first["title"]!.GetValue<string>()));
        Assert.Equal("invalid_payload", first["details"]!["reason_code"]!.GetValue<string>());
        Assert.DoesNotContain("Refused", first.ToJsonString());               // the patient's name never goes up
        Assert.DoesNotContain("local_json", first.ToJsonString());

        var before = host.Cloud.ReviewUploads.Count;
        await svc.RunManualAsync();                                           // nothing new, nothing sent again
        Assert.Equal(before, host.Cloud.ReviewUploads.Count);
    }

    [Fact]
    public async Task A_change_stuck_after_ten_tries_is_uploaded_as_stuck()
    {
        await using var host = await SyncTestHost.StartAsync();
        await AddChangesAsync(host, "Stuck");
        var outbox = host.Get<ISyncOutbox>();
        Guid stuckId;
        await using (var db = await host.DbAsync())
        {
            var one = (await outbox.ListPendingAsync(db, 10)).First();
            stuckId = one.OperationId;
            for (var i = 0; i < SyncOutbox.StuckAttempts; i++) await outbox.MarkAttemptAsync(db, one.OperationId, "boom");
        }
        await host.Get<ISyncService>().RunManualAsync();
        var item = Assert.Single(host.Cloud.ReviewUploads, u => u["kind"]!.GetValue<string>() == "change_stuck");
        Assert.Equal(stuckId.ToString(), item["id"]!.GetValue<string>(), ignoreCase: true);
        Assert.Contains("failed 10 times", item["title"]!.GetValue<string>());
        Assert.Equal("boom", item["details"]!["last_error"]!.GetValue<string>());
    }

    [Fact]
    public async Task If_the_upload_does_not_work_the_items_wait_and_go_next_time()
    {
        await using var host = await SyncTestHost.StartAsync();
        await AddChangesAsync(host, "Later");
        host.Cloud.OnPush = op => host.Cloud.Applied(op, "rejected", "invalid_payload");
        host.Cloud.ReviewUploadFails = true;                                  // an older server without the route
        var svc = host.Get<ISyncService>();
        Assert.True((await svc.RunManualAsync()).Succeeded);                  // the sync itself is unaffected
        Assert.Empty(host.Cloud.ReviewUploads);

        host.Cloud.ReviewUploadFails = false;
        await svc.RunManualAsync();
        Assert.True(host.Cloud.ReviewUploads.Count >= 3);
    }

    [Fact]
    public async Task Notes_the_server_makes_itself_are_not_uploaded()
    {
        await using var host = await SyncTestHost.StartAsync();
        await AddChangesAsync(host, "Merged");
        host.Cloud.OnPush = op => host.Cloud.Applied(op, "merged", "queue_number_reassigned");   // "the server kept some of its own values": for the desktop's own list only
        await host.Get<ISyncService>().RunManualAsync();
        Assert.Empty(host.Cloud.ReviewUploads);
    }
}
//JJQR - END
