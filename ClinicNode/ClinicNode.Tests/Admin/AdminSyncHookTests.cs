// JJQR - AI (Nov 21, 2026) - ISyncBridge.RequestSync after successful admin saves; patient update changed_fields.
using System.Text.Json;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Admin;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Events;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Admin;

public class AdminSyncHookTests
{
    sealed class RecordingSyncBridge : ISyncBridge
    {
        public int Requests { get; private set; }
        public void RequestSync() => Requests++;
        public Task<bool> IsRevokedAsync(CancellationToken ct = default) => Task.FromResult(false);
    }

    [Fact]
    public async Task RequestSync_runs_on_successful_save_but_not_on_failure()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var sync = new RecordingSyncBridge();
        var announcements = new AdminAnnouncementService(
            host.Get<IDbContextFactory<ClinicAppDbContext>>(),
            host.Get<ISyncOutbox>(),
            host.Get<IAuthService>(),
            sync);

        var failed = await announcements.CreateAsync(new AnnouncementEdit("", "", true));
        Assert.False(failed.Succeeded);
        Assert.Equal(0, sync.Requests);

        var created = await announcements.CreateAsync(new AnnouncementEdit("Holiday hours", "The clinic closes at noon.", true));
        Assert.True(created.Succeeded, created.Error);
        Assert.Equal(1, sync.Requests);

        var updated = await announcements.UpdateAsync(created.Value!.Id, new AnnouncementEdit("", "Missing title", true));
        Assert.False(updated.Succeeded);
        Assert.Equal(1, sync.Requests);
    }

    [Fact]
    public async Task Patient_update_queues_changed_fields_and_skips_when_unchanged()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var sync = new RecordingSyncBridge();
        var patients = new AdminPatientService(
            host.Get<IDbContextFactory<ClinicAppDbContext>>(),
            host.Get<ISyncOutbox>(),
            host.Get<IAuthService>(),
            host.Get<IPatientCodeAllocator>(),
            host.Get<IClinicEventBus>(),
            sync);
        var outbox = host.Get<ISyncOutbox>();
        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();

        var future = await patients.CreateAsync(new PatientEdit("Future", "Kid", ClinicApp.Domain.ClinicClock.Today.AddDays(1), SexType.Female));
        Assert.False(future.Succeeded);
        Assert.Equal(0, sync.Requests);

        var edit = new PatientEdit("Tesa", "Marquez", new DateOnly(1991, 7, 7), SexType.Female, ContactNumber: "09171234567");
        var created = await patients.CreateAsync(edit);
        Assert.True(created.Succeeded, created.Error);
        Assert.Equal(1, sync.Requests);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var pendingAfterCreate = await outbox.ListPendingAsync(db, 200);
            Assert.Contains(pendingAfterCreate, e => e.EntityId == created.Value!.PatientId && e.OperationType == "create");
        }

        var unchanged = await patients.UpdateAsync(created.Value!.PatientId, edit);
        Assert.True(unchanged.Succeeded, unchanged.Error);
        Assert.Equal(1, sync.Requests);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var pending = await outbox.ListPendingAsync(db, 200);
            Assert.DoesNotContain(pending, e => e.EntityId == created.Value.PatientId && e.OperationType == "update");
        }

        var changed = await patients.UpdateAsync(
            created.Value.PatientId,
            new PatientEdit("Tesa", "Marquez-Reyes", new DateOnly(1991, 7, 7), SexType.Female, ContactNumber: "09170000000"));
        Assert.True(changed.Succeeded, changed.Error);
        Assert.Equal(2, sync.Requests);
        Assert.Equal("Marquez-Reyes", changed.Value!.LastName);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var pending = await outbox.ListPendingAsync(db, 200);
            var update = Assert.Single(pending, e => e.EntityId == created.Value.PatientId && e.OperationType == "update");
            using var json = JsonDocument.Parse(update.PayloadJson);
            Assert.Equal(created.Value.PatientId, json.RootElement.GetProperty("patientId").GetGuid());
            var fields = json.RootElement.GetProperty("changed_fields").EnumerateArray().Select(x => x.GetString()).ToList();
            Assert.Equal(["last_name", "contact_number"], fields);
        }
    }
}
// JJQR - END
