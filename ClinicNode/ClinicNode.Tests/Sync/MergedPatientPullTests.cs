//JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.4 (desktop side): a patient the server merged away leaves this computer's lists, unless something here still points at it
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Visits;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Sync;

public class MergedPatientPullTests
{
    private static async Task<Guid> NewPatientAsync(SyncTestHost host, string first) =>
        (await host.Get<IPatientService>().CreateAsync(new NewPatientRequest(first, "Merged", new DateOnly(1990, 1, 1), SexType.Female))).PatientId;

    private static async Task PullDeleteAsync(SyncTestHost host, Guid patientId)
    {
        host.Cloud.EnqueuePull("5", false, FakeSyncServer.Change("patient", patientId, null, delete: true));
        var r = await host.Get<ISyncService>().RunManualAsync();
        Assert.True(r.Succeeded, r.Error);
    }

    [Fact]
    public async Task A_merged_away_patient_with_nothing_pointing_at_it_is_removed()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        var keep = await NewPatientAsync(host, "Keep"); var gone = await NewPatientAsync(host, "Gone");
        await host.Get<ISyncService>().RunManualAsync();                      // both are known to the server: nothing waiting

        await PullDeleteAsync(host, gone);

        await using var db = await host.DbAsync();
        Assert.False(await db.Patients.AnyAsync(p => p.PatientId == gone));
        Assert.True(await db.Patients.AnyAsync(p => p.PatientId == keep));
    }

    [Fact]
    public async Task A_patient_with_a_visit_still_recorded_under_it_is_kept_and_listed()
    {
        await using var host = await SyncTestHost.StartAsync();
        var session = await host.LoginAsync();
        var gone = await NewPatientAsync(host, "Visited");
        await host.Get<IVisitStarter>().StartWalkInConsultAsync(gone, session.DoctorId!.Value, null);
        await host.Get<ISyncService>().RunManualAsync();

        await PullDeleteAsync(host, gone);

        await using var db = await host.DbAsync();
        Assert.True(await db.Patients.AnyAsync(p => p.PatientId == gone));    // kept: its visit still points to it
        var open = await SyncLocalStore.ListOpenConflictsAsync(db);
        Assert.Contains(open, c => c.Kind == "patient_merged_kept" && c.EntityId == gone.ToString());
    }

    [Fact]
    public async Task A_patient_with_an_unsent_change_is_kept_and_listed()
    {
        await using var host = await SyncTestHost.StartAsync();
        await host.LoginAsync();
        var gone = await NewPatientAsync(host, "Unsent");                      // not synced yet
        host.Cloud.Offline = false;
        await using (var db0 = await host.DbAsync()) Assert.True(await host.Get<ClinicNode.Data.ISyncOutbox>().CountPendingAsync(db0) > 0);
        host.Cloud.OnPush = op => host.Cloud.Applied(op, "retry");             // the server keeps asking to retry: the change stays waiting
        host.Cloud.EnqueuePull("5", false, FakeSyncServer.Change("patient", gone, null, delete: true));
        await host.Get<ISyncService>().RunManualAsync();

        await using var db = await host.DbAsync();
        Assert.True(await db.Patients.AnyAsync(p => p.PatientId == gone));
        Assert.Contains(await SyncLocalStore.ListOpenConflictsAsync(db), c => c.Kind == "patient_merged_kept");
    }
}
//JJQR - END
