// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Admin;

namespace ClinicNode.Tests.Admin;

public class AdminDoctorPatientTests
{
    [Fact]
    public async Task Doctor_update_rejects_a_negative_fee_and_saves_a_valid_edit()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var doctors = host.Get<IAdminDoctorService>();
        // Seed may include more than the demo doctor (e.g. "Dr. Phase5 Doctor").
        var doc = (await doctors.ListAllAsync())
            .Single(d => d.Email.Equals("doctor@clinic.test", StringComparison.OrdinalIgnoreCase));
        var bad = await doctors.UpdateAsync(doc.DoctorId, new DoctorEdit(
            doc.Specialization, -5, doc.SlotDurationMinutes, Math.Max(1, doc.SlotCapacity), doc.DailyPatientLimit,
            doc.Bio, doc.LicenseNumber, doc.PtrNumber, doc.S2Number));
        Assert.False(bad.Succeeded);

        var ok = await doctors.UpdateAsync(doc.DoctorId, new DoctorEdit(
            "Internal Medicine", 600, 20, Math.Max(1, doc.SlotCapacity), doc.DailyPatientLimit,
            "Updated bio", doc.LicenseNumber, doc.PtrNumber, doc.S2Number, FullName: doc.Name));
        Assert.True(ok.Succeeded, ok.Error);
        Assert.Equal("Internal Medicine", ok.Value!.Specialization);
        Assert.Equal(600, ok.Value.ConsultationFee);
        Assert.Equal(20, ok.Value.SlotDurationMinutes);
    }

    [Fact]
    public async Task Patient_create_rejects_a_future_birth_date_and_lists_on_search()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var patients = host.Get<IAdminPatientService>();
        var future = await patients.CreateAsync(new PatientEdit("Future", "Kid", ClinicClock.Today.AddDays(1), SexType.Female));
        Assert.False(future.Succeeded);
        Assert.Contains("future", future.Error, StringComparison.OrdinalIgnoreCase);

        var created = await patients.CreateAsync(new PatientEdit("Tesa", "Marquez", new DateOnly(1991, 7, 7), SexType.Female, ContactNumber: "09171234567"));
        Assert.True(created.Succeeded, created.Error);
        Assert.StartsWith("MF-", created.Value!.PatientCode);

        var page = await patients.SearchAsync("Marquez", sort: "created");
        Assert.Contains(page.Items, p => p.PatientId == created.Value.PatientId);

        var updated = await patients.UpdateAsync(created.Value.PatientId, new PatientEdit("Tesa", "Marquez-Reyes", new DateOnly(1991, 7, 7), SexType.Female, ContactNumber: "09171234567"));
        Assert.True(updated.Succeeded, updated.Error);
        Assert.Equal("Marquez-Reyes", updated.Value!.LastName);
    }

    [Fact]
    public async Task Data_request_cannot_complete_deletion_before_retention()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var patients = host.Get<IAdminPatientService>();
        var created = await patients.CreateAsync(new PatientEdit("Uma", "Tan", new DateOnly(1960, 1, 1), SexType.Female));
        Assert.True(created.Succeeded, created.Error);

        // Seed a deletion request that is still inside the 7-year window.
        var factory = host.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>();
        Guid requestId;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var req = new ClinicApp.Domain.Entities.DataRequest
            {
                Id = Guid.NewGuid(),
                PatientId = created.Value!.PatientId,
                Kind = ClinicApp.Domain.Entities.DataRequestKind.Deletion,
                Details = "Please delete my record.",
                Status = ClinicApp.Domain.Entities.DataRequestStatus.Scheduled,
                RequestedAt = DateTimeOffset.UtcNow,
                RetainUntil = ClinicClock.Today.AddYears(1),
            };
            db.DataRequests.Add(req);
            await db.SaveChangesAsync();
            requestId = req.Id;
        }

        var blocked = await patients.ResolveDataRequestAsync(requestId, "Completed", "too soon");
        Assert.False(blocked.Succeeded);
        Assert.Contains("must be kept until", blocked.Error, StringComparison.OrdinalIgnoreCase);

        var rejected = await patients.ResolveDataRequestAsync(requestId, "Rejected", "Still within retention.");
        Assert.True(rejected.Succeeded, rejected.Error);
        Assert.Equal("Rejected", rejected.Value!.Status);
    }
}
// JJQR - END
