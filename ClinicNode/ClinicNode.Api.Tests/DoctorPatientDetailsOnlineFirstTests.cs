//JJQR - AI (2026-11-26) - GAV-10: the doctor's "Edit details" online (PUT /api/patients/{id}, fetch-merge-put) and in Offline mode (this computer + sync).
using System.Net;
using System.Text.Json;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.Core.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.Api.Tests;

public class DoctorPatientDetailsOnlineFirstTests
{
    static Task<ApiRig> Rig() => ApiRig.StartAsync(s =>
    {
        s.AddClinicNodeAdmin();
        s.TryAddSingleton<AdminPatientService>();
        s.AddOnlineFirst<IAdminPatientService, ApiAdminPatientService, OfflineGuardedAdminPatientService>();
    });

    [Fact]
    public async Task Online_save_reads_the_full_row_changes_only_dialog_fields_and_puts_it_back()
    {
        await using var rig = await Rig();
        var id = Guid.NewGuid();
        var cloud = new Patient
        {
            PatientId = id, PatientCode = "MF-000777", FirstName = "Ana", LastName = "Cruz", DateOfBirth = new DateOnly(1990, 5, 10), Sex = SexType.Female,
            Email = "ana@example.test", SeniorIdNumber = "S-1", PwdIdNumber = "P-1", GuardianName = "G", ContactNumber = "09170000000",
        };
        string? putBody = null;
        var healthy = rig.Http.Route;
        rig.Http.Route = r =>
        {
            if (r.RequestUri!.AbsolutePath == $"/api/patients/{id:D}" && r.Method == HttpMethod.Get) return FakeHandler.Json(cloud);
            if (r.RequestUri!.AbsolutePath == $"/api/patients/{id:D}" && r.Method == HttpMethod.Put)
            {
                putBody = r.Content!.ReadAsStringAsync().Result;
                return FakeHandler.Text(putBody, HttpStatusCode.OK);
            }
            return healthy(r);
        };
        var svc = rig.Get<IAdminPatientService>();
        var got = await svc.GetDetailsAsync(id);
        Assert.True(got.Succeeded, got.Error);
        Assert.Equal("MF-000777", got.Value!.PatientCode);

        var saved = await svc.UpdateDetailsAsync(id, got.Value with { FirstName = "anabel", DateOfBirth = new DateOnly(1992, 12, 30), City = "Cebu", CivilStatus = "Married", EmergencyContactName = "Fake Contact" });
        Assert.True(saved.Succeeded, saved.Error);
        Assert.Equal("Anabel", saved.Value!.FirstName);
        Assert.NotNull(putBody);
        using var json = JsonDocument.Parse(putBody!);
        var b = json.RootElement;
        Assert.Equal("Anabel", b.GetProperty("first_name").GetString());
        Assert.Equal("1992-12-30", b.GetProperty("date_of_birth").GetString());
        Assert.Equal("Cebu", b.GetProperty("city").GetString());
        Assert.Equal("Married", b.GetProperty("civil_status").GetString());
        Assert.Equal("Fake Contact", b.GetProperty("emergency_contact_name").GetString());
        // fields the dialog does not show go back unchanged
        Assert.Equal("ana@example.test", b.GetProperty("email").GetString());
        Assert.Equal("S-1", b.GetProperty("senior_id_number").GetString());
        Assert.Equal("09170000000", b.GetProperty("contact_number").GetString());

        // a refusal is answered before any request
        var before = rig.Http.Requests.Count;
        var bad = await svc.UpdateDetailsAsync(id, got.Value with { LastName = " " });
        Assert.False(bad.Succeeded);
        Assert.Equal(before, rig.Http.Requests.Count);
    }

    [Fact]
    public async Task Offline_mode_saves_on_this_computer_and_queues_one_patient_update()
    {
        await using var rig = await Rig();
        await rig.GoOfflineAsync();
        Guid id;
        await using (var db = await rig.Get<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>().CreateDbContextAsync())
            id = (await db.Patients.AsNoTracking().OrderBy(p => p.PatientCode).FirstAsync()).PatientId;
        var svc = rig.Get<IAdminPatientService>();
        var got = await svc.GetDetailsAsync(id);
        Assert.True(got.Succeeded, got.Error);
        var saved = await svc.UpdateDetailsAsync(id, got.Value! with { City = "Offline City", PhilhealthNumber = "12-000000000-1" });
        Assert.True(saved.Succeeded, saved.Error);
        Assert.Equal("Offline City", saved.Value!.City);
        Assert.DoesNotContain(rig.Http.Requests, r => r.PathAndQuery.StartsWith("/api/patients", StringComparison.Ordinal));
        await using var db2 = await rig.Get<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>().CreateDbContextAsync();
        var pending = await rig.Get<ClinicNode.Data.ISyncOutbox>().ListPendingAsync(db2, 500);
        var op = Assert.Single(pending, e => e.EntityId == id && e.OperationType == "update" && e.Aggregate == "patient");
        Assert.Contains("\"city\"", op.PayloadJson);
        Assert.Contains("\"philhealth_number\"", op.PayloadJson);
    }
}
//JJQR - END
