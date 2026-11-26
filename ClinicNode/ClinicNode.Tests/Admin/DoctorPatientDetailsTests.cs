//JJQR - AI (2026-11-26) - GAV-10: the doctor's "Edit details" (all patient fields, online + offline). Local path = what an offline desktop saves and syncs.
using System.Text.Json;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Admin;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Admin;

public class DoctorPatientDetailsTests
{
    static PatientDetails Sample() => new("Ana", null, "Cruz", new DateOnly(1990, 5, 10), SexType.Female, ContactNumber: "09170000000");

    [Fact]
    public void Rules_title_case_names_blank_to_null_and_age_from_birthday()
    {
        var n = PatientDetailsRules.Normalize(Sample() with { FirstName = "  maria clara ", MiddleName = " ", City = "  ", LastName = "o'NEIL-smith" });
        Assert.Equal("Maria Clara", n.FirstName);
        Assert.Null(n.MiddleName);
        Assert.Null(n.City);
        Assert.Equal("O'Neil-Smith", n.LastName);
        Assert.Equal(36, PatientDetailsRules.AgeOn(new DateOnly(1990, 5, 10), new DateOnly(2026, 11, 26)));
        Assert.Equal(35, PatientDetailsRules.AgeOn(new DateOnly(1990, 11, 27), new DateOnly(2026, 11, 26)));
        Assert.Equal("Maria Clara O'Neil-Smith", PatientDetailsRules.DisplayName(n));
    }

    [Fact]
    public void Rules_refuse_what_the_server_refuses()
    {
        var today = new DateOnly(2026, 11, 26);
        Assert.Null(PatientDetailsRules.Problem(Sample(), today));
        Assert.Contains("First name", PatientDetailsRules.Problem(Sample() with { FirstName = " " }, today));
        Assert.Contains("Last name", PatientDetailsRules.Problem(Sample() with { LastName = "" }, today));
        Assert.Contains("future", PatientDetailsRules.Problem(Sample() with { DateOfBirth = today.AddDays(1) }, today));
        Assert.NotNull(PatientDetailsRules.Problem(Sample() with { DateOfBirth = new DateOnly(1899, 12, 31) }, today));
        Assert.Contains("Contact number", PatientDetailsRules.Problem(Sample() with { ContactNumber = "abc1234567" }, today));
        Assert.Contains("Emergency contact number", PatientDetailsRules.Problem(Sample() with { EmergencyContactNumber = "12345" }, today));
        Assert.Contains("Address", PatientDetailsRules.Problem(Sample() with { Address = new string('a', 301) }, today));
        Assert.Null(PatientDetailsRules.Problem(Sample() with { ContactNumber = "+63 917 000 0000" }, today));
    }

    [Fact]
    public void ApplyTo_reports_only_changed_fields_and_never_touches_fields_the_put_ignores()
    {
        var p = new Patient { FirstName = "Ana", LastName = "Cruz", DateOfBirth = new DateOnly(1990, 5, 10), Sex = SexType.Female, ContactNumber = "09170000000",
            Email = "ana@example.test", SeniorIdNumber = "S-1", PwdIdNumber = "P-1", GuardianName = "G" };
        var changed = PatientDetailsRules.ApplyTo(p, Sample() with { City = "Cebu", CivilStatus = "Married" });
        Assert.Equal(["civil_status", "city"], changed.Select(c => c.Field));
        Assert.Equal(("ana@example.test", "S-1", "P-1", "G"), (p.Email, p.SeniorIdNumber, p.PwdIdNumber, p.GuardianName));
        Assert.Empty(PatientDetailsRules.ApplyTo(p, PatientDetailsRules.From(p)));
    }

    [Fact]
    public async Task Doctor_saves_all_fields_offline_audited_and_queued_for_sync()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var patients = host.Get<IAdminPatientService>();
        var created = await patients.CreateAsync(new PatientEdit("Tesa", "Marquez", new DateOnly(1991, 7, 7), SexType.Female, ContactNumber: "09171234567", Email: "tesa@example.test"));
        Assert.True(created.Succeeded, created.Error);
        var id = created.Value!.PatientId;

        await host.LoginDoctorAsync();
        var before = await patients.GetDetailsAsync(id);
        Assert.True(before.Succeeded, before.Error);
        var edit = before.Value! with
        {
            FirstName = "teresa", MiddleName = "luna", LastName = "marquez-reyes", DateOfBirth = new DateOnly(1992, 12, 30), ContactNumber = "0918 111 2222",
            CivilStatus = "Married", Address = "1 Fake St", City = "Lapu-Lapu City", ZipCode = "6015", BloodType = "O+", PhilhealthNumber = "12-345678901-2",
            HmoProvider = "Fake HMO", HmoCardNumber = "H-1", EmergencyContactName = "Fake Contact", EmergencyContactNumber = "0917 222 3333", EmergencyContactRelationship = "Sister",
        };
        var bad = await patients.UpdateDetailsAsync(id, edit with { ContactNumber = "abc" });
        Assert.False(bad.Succeeded);

        var saved = await patients.UpdateDetailsAsync(id, edit);
        Assert.True(saved.Succeeded, saved.Error);
        Assert.Equal("Teresa", saved.Value!.FirstName);
        Assert.Equal("Luna", saved.Value.MiddleName);
        Assert.Equal("Marquez-Reyes", saved.Value.LastName);
        Assert.Equal(new DateOnly(1992, 12, 30), saved.Value.DateOfBirth);

        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var row = await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == id);
        Assert.Equal(("Lapu-Lapu City", "6015", "O+", "Sister"), (row.City, row.ZipCode, row.BloodType, row.EmergencyContactRelationship));
        Assert.Equal("tesa@example.test", row.Email); // not part of the dialog: kept

        var pending = await host.Get<ISyncOutbox>().ListPendingAsync(db, 200);
        var update = Assert.Single(pending, e => e.EntityId == id && e.OperationType == "update");
        using var json = JsonDocument.Parse(update.PayloadJson);
        var fields = json.RootElement.GetProperty("changed_fields").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(["first_name", "middle_name", "last_name", "date_of_birth", "civil_status", "contact_number", "address", "city", "zip_code",
            "blood_type", "philhealth_number", "hmo_provider", "hmo_card_number", "emergency_contact_name", "emergency_contact_number", "emergency_contact_relationship"], fields);

        var audit = await db.AuditLogs.AsNoTracking().Where(a => a.EntityId == id && a.Action == "Updated").ToListAsync();
        var entry = Assert.Single(audit);
        Assert.Contains("First name", entry.Details);
        Assert.DoesNotContain("Teresa", entry.Details); // field names only (privacy rule PV-9)

        // saving the same values again changes nothing and queues nothing more
        var again = await patients.UpdateDetailsAsync(id, saved.Value);
        Assert.True(again.Succeeded, again.Error);
        await using var db2 = await factory.CreateDbContextAsync();
        Assert.Single(await host.Get<ISyncOutbox>().ListPendingAsync(db2, 200), e => e.EntityId == id && e.OperationType == "update");
    }
}
//JJQR - END
