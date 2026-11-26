//JJQR - AI (2026-11-26) - GAV-12: "Merge to existing patient" on the desktop: online only (Offline mode = "needs an internet connection", no request),
// the preview read (both records, visits open today), the POST body the server expects (the picked existing record is KEPT), its 409 refusals,
// and "sync first" (unsent work for the current patient stops the merge). Fake names only.
using System.Net;
using System.Text.Json;
using ClinicApp.Infrastructure;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.Core.Admin;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.Api.Tests;

public class PatientMergeOnlineFirstTests
{
    static Task<ApiRig> Rig() => ApiRig.StartAsync(s =>
    {
        s.TryAddSingleton<OfflinePatientMergeService>();
        s.AddOnlineFirst<IPatientMergeService, ApiPatientMergeService, OfflinePatientMergeService>();
    });

    static readonly Guid Kept = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011");
    static readonly Guid Dupe = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000012");

    static Dictionary<string, object?> Card(Guid id, string code, string first, string? phone, string created, int consults) => new()
    {
        ["patient_id"] = id, ["patient_code"] = code, ["first_name"] = first, ["middle_name"] = null, ["last_name"] = "Fakerubi", ["date_of_birth"] = "1990-01-20",
        ["sex"] = "Male", ["contact_number"] = phone, ["email"] = null, ["address"] = null, ["city"] = "Fake City", ["civil_status"] = null, ["blood_type"] = consults > 0 ? "O+" : null,
        ["philhealth_number"] = null, ["hmo_provider"] = null, ["has_account"] = false, ["created_at"] = created,
        ["counts"] = new Dictionary<string, int> { ["bookings"] = consults, ["consultations"] = consults },
    };

    [Fact]
    public async Task Online_preview_is_read_and_the_merge_keeps_the_picked_existing_record()
    {
        await using var rig = await Rig();
        string? posted = null;
        var healthy = rig.Http.Route;
        rig.Http.Route = r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == "/api/patients/merge-preview") return FakeHandler.Json(new Dictionary<string, object?>
            {
                ["keep"] = Card(Kept, "MF-000011", "Jessie", null, "2026-11-23T01:00:00Z", 1),
                ["merge"] = Card(Dupe, "MF-000012", "Jese", "0918 555 0102", "2026-11-25T01:00:00Z", 1),
                ["both_have_accounts"] = false,
                ["keep_active_today"] = Array.Empty<object>(),
                ["merge_active_today"] = new[] { new Dictionary<string, object?> { ["booking_id"] = Guid.NewGuid(), ["queue_number"] = "R-007", ["status"] = "InProgress" } },
            });
            if (path == "/api/patients/merge" && r.Method == HttpMethod.Post)
            {
                posted = r.Content!.ReadAsStringAsync().Result;
                return FakeHandler.Json(new Dictionary<string, object?>
                {
                    ["kept_patient_id"] = Kept, ["kept_patient_code"] = "MF-000011", ["merged_patient_id"] = Dupe, ["merged_patient_code"] = "MF-000012",
                    ["moved"] = new Dictionary<string, int> { ["bookings"] = 1, ["consultations"] = 1 }, ["filled_fields"] = new[] { "ContactNumber" },
                });
            }
            return healthy(r);
        };
        var svc = rig.Get<IPatientMergeService>();
        var p = await svc.PreviewAsync(Kept, Dupe);
        Assert.True(p.Succeeded, p.Error);
        Assert.Contains("merge-preview?keep=" + Kept.ToString("D"), Assert.Single(rig.Http.Requests, x => x.PathAndQuery.Contains("merge-preview")).PathAndQuery);
        Assert.Equal("Jessie Fakerubi", p.Value!.Keep.Name);
        Assert.Equal(new DateOnly(1990, 1, 20), p.Value.Merge.DateOfBirth);
        Assert.Contains("R-007", PatientMergeRules.ActiveVisitWarning(p.Value));                  // warning, not a stop
        Assert.Contains("will be merged into Jessie Fakerubi (MF-000011)", PatientMergeRules.Summary(p.Value));
        var rows = PatientMergeRules.CompareRows(p.Value.Keep, p.Value.Merge);
        Assert.True(rows.Single(r => r.Label == "First name").Differs);
        Assert.False(rows.Single(r => r.Label == "Last name").Differs);
        Assert.Equal("—", rows.Single(r => r.Label == "Contact number").Keep);                 // the blank the merge fills

        var m = await svc.MergeAsync(Kept, Dupe, "  Same person, registered again on Nov 25  ");
        Assert.True(m.Succeeded, m.Error);
        Assert.Equal("MF-000011", m.Merged!.KeptPatientCode);
        Assert.Equal(1, m.Merged.Moved["consultations"]);
        Assert.Equal("ContactNumber", Assert.Single(m.Merged.FilledFields));
        using var json = JsonDocument.Parse(posted!);
        Assert.Equal(Kept, json.RootElement.GetProperty("keep_patient_id").GetGuid());
        Assert.Equal(Dupe, json.RootElement.GetProperty("merge_patient_id").GetGuid());
        Assert.Equal("Same person, registered again on Nov 25", json.RootElement.GetProperty("note").GetString());
    }

    [Fact]
    public async Task Server_refusals_come_back_as_messages()
    {
        await using var rig = await Rig();
        var healthy = rig.Http.Route;
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/api/patients/merge"
            ? FakeHandler.Json(new { error = "both_have_accounts", message = "Both patients have a patient-portal account, so they cannot be merged here." }, HttpStatusCode.Conflict)
            : healthy(r);
        var m = await rig.Get<IPatientMergeService>().MergeAsync(Kept, Dupe, "Same person twice");
        Assert.False(m.Succeeded);
        Assert.Contains("portal account", m.Error);
        Assert.False(m.NeedsInternet);
    }

    [Fact]
    public async Task Offline_mode_refuses_with_needs_internet_and_sends_nothing()
    {
        await using var rig = await Rig();
        await rig.GoOfflineAsync();
        var svc = rig.Get<IPatientMergeService>();
        var p = await svc.PreviewAsync(Kept, Dupe);
        Assert.True(AdminOnlineOnly.IsNeedsInternet(p.Error));
        var m = await svc.MergeAsync(Kept, Dupe, "Same person twice");
        Assert.True(m.NeedsInternet);
        Assert.DoesNotContain(rig.Http.Requests, r => r.PathAndQuery.Contains("merge", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sync_first_unsent_work_for_the_current_patient_stops_the_merge()
    {
        await using var rig = await Rig();
        var factory = rig.Get<IDbContextFactory<ClinicAppDbContext>>();
        Guid patientId, bookingId, other;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var b = await db.Bookings.AsNoTracking().OrderBy(x => x.CreatedAt).FirstAsync();
            bookingId = b.BookingId; patientId = b.PatientId;
            other = await db.Patients.AsNoTracking().Where(x => x.PatientId != patientId).Select(x => x.PatientId).FirstAsync();
        }
        var gate = new PatientSyncGate(factory);
        Assert.Null(await gate.PendingProblemAsync(patientId));
        await using (var db = await factory.CreateDbContextAsync())
            await rig.Get<ISyncOutbox>().EnqueueAsync(db, new SyncOutboxEntry { OperationId = Guid.NewGuid(), Aggregate = "booking", EntityId = bookingId, OperationType = "update", PayloadJson = "{}", CreatedAt = DateTimeOffset.UtcNow });
        Assert.Equal(PatientMergeRules.PendingMessage, await gate.PendingProblemAsync(patientId)); // this patient's visit is unsent → refused
        Assert.Null(await gate.PendingProblemAsync(other));                                         // someone else's work does not block
    }

    [Fact]
    public void Dialog_rules()
    {
        Assert.Contains("choose the existing", PatientMergeRules.Problem(Dupe, null, "Same person"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same record", PatientMergeRules.Problem(Dupe, Dupe, "Same person"));
        Assert.Contains("at least 5", PatientMergeRules.Problem(Dupe, Kept, "no"));
        Assert.Contains("portal", PatientMergeRules.Problem(Dupe, Kept, "Same person", bothHaveAccounts: true));
        Assert.Null(PatientMergeRules.Problem(Dupe, Kept, "Same person"));
    }
}
//JJQR - END
