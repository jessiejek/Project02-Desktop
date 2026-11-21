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

    static NewPatientRequest New(string first, string last, DateOnly dob) =>
        new(first, last, dob, SexType.Female);
}
// JJQR - END
