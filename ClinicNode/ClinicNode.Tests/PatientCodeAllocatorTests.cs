// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Patients;
using ClinicNode.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests;

public class PatientCodeAllocatorTests
{
    [Fact]
    public async Task New_patients_receive_codes_from_the_reserved_MF_block()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var patients = host.Get<IPatientService>();
        var a = await patients.CreateAsync(New("Ana", "Reyes", new DateOnly(1990, 1, 1)));
        var b = await patients.CreateAsync(New("Ben", "Santos", new DateOnly(1991, 2, 2)));
        Assert.StartsWith("MF-", a.PatientCode);
        Assert.DoesNotContain("MF-T-", a.PatientCode);
        Assert.StartsWith("MF-", b.PatientCode);
        Assert.NotEqual(a.PatientCode, b.PatientCode);
        var hits = await patients.SearchByNameAsync("Reyes");
        Assert.Contains(hits, p => p.PatientId == a.PatientId);
    }

    [Fact]
    public async Task Exhausted_block_issues_a_temporary_MF_T_code()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var factory = host.Get<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE node_patient_code_blocks SET next_value = end_inclusive + 1 WHERE prefix = 'MF-';";
            await cmd.ExecuteNonQueryAsync();
        }

        var patients = host.Get<IPatientService>();
        var temp = await patients.CreateAsync(New("Tia", "Morales", new DateOnly(1988, 5, 5)));
        Assert.StartsWith("MF-T-", temp.PatientCode);
        var temp2 = await patients.CreateAsync(New("Tom", "Cruz", new DateOnly(1987, 6, 6)));
        Assert.StartsWith("MF-T-", temp2.PatientCode);
        Assert.NotEqual(temp.PatientCode, temp2.PatientCode);
    }

    //JJQR - AI (Nov 23, 2026) - owner (offline test): the block was reserved at 100000-100099 on an empty database, then the server's patients MF-100000..MF-100096 were pulled in. Registering offline hit the unique index every time and the counter never moved.
    [Fact]
    public async Task A_block_that_lies_under_codes_already_on_file_skips_them_instead_of_failing()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var factory = host.Get<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            for (var n = 100000; n <= 100096; n++)
                db.Patients.Add(new ClinicApp.Domain.Entities.Patient
                {
                    PatientId = Guid.NewGuid(), PatientCode = $"MF-{n}", FirstName = "Synced", LastName = "P" + n, DateOfBirth = new DateOnly(1980, 1, 1),
                    Sex = SexType.Male, Email = "", IsGuest = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
                });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE node_patient_code_blocks SET start_inclusive = 100000, end_inclusive = 100099, next_value = 100000 WHERE prefix = 'MF-';");
        }

        var patients = host.Get<IPatientService>();
        var a = await patients.CreateAsync(New("Glorife", "Nosido", new DateOnly(1996, 11, 13)));
        var b = await patients.CreateAsync(New("Next", "Person", new DateOnly(1997, 1, 1)));
        Assert.Equal("MF-100097", a.PatientCode);
        Assert.Equal("MF-100098", b.PatientCode);
    }

    [Fact]
    public async Task When_server_codes_pass_the_end_of_the_block_the_block_moves_forward_and_codes_stay_official()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var factory = host.Get<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            db.Patients.Add(new ClinicApp.Domain.Entities.Patient
            {
                PatientId = Guid.NewGuid(), PatientCode = "MF-100250", FirstName = "Far", LastName = "Ahead", DateOfBirth = new DateOnly(1980, 1, 1),
                Sex = SexType.Male, Email = "", IsGuest = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE node_patient_code_blocks SET start_inclusive = 100000, end_inclusive = 100099, next_value = 100000 WHERE prefix = 'MF-';");
        }
        var a = await host.Get<IPatientService>().CreateAsync(New("Late", "Comer", new DateOnly(1990, 3, 3)));
        Assert.Equal("MF-100251", a.PatientCode);
        var b = await host.Get<IPatientService>().CreateAsync(New("Late", "Comer2", new DateOnly(1990, 3, 4)));
        Assert.Equal("MF-100252", b.PatientCode);
    }
    //JJQR - END

    static NewPatientRequest New(string first, string last, DateOnly dob) =>
        new(first, last, dob, SexType.Female);
}
// JJQR - END
