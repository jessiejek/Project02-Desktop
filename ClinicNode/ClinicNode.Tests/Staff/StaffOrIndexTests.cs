//JJQR - AI (Nov 21, 2026) - unique OR index: created by SchemaRepair, idempotent, legacy-safe, skip on bad data.
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicNode.Tests.Staff;

public class StaffOrIndexTests
{
    static async Task<long> ScalarAsync(ClinicApp.Infrastructure.ClinicAppDbContext db, string sql)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var c = conn.CreateCommand();
        c.CommandText = sql;
        return Convert.ToInt64(await c.ExecuteScalarAsync());
    }

    static Task<long> IndexCountAsync(ClinicApp.Infrastructure.ClinicAppDbContext db) =>
        ScalarAsync(db, $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{SchemaRepair.OrIndexName}'");

    [Fact]
    public async Task Startup_creates_the_index_and_repair_is_idempotent()
    {
        await using var host = await TestHost.StartAsync();
        await using var db = await host.DbAsync();
        Assert.Equal(1, await IndexCountAsync(db));
        Assert.Equal(SchemaRepair.OrIndexResult.AlreadyPresent, await SchemaRepair.EnsureOrNumberIndexAsync(db, NullLogger.Instance));
        await SchemaRepair.EnsureModelColumnsAsync(db, NullLogger.Instance);
        await SchemaRepair.EnsureModelColumnsAsync(db, NullLogger.Instance);
        Assert.Equal(1, await IndexCountAsync(db));
    }

    [Fact]
    public async Task Sequence_numbers_are_unique_but_legacy_duplicates_are_allowed()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var t1 = await host.CompletedVisitAsync("IdxA");
        var t2 = await host.CompletedVisitAsync("IdxB");
        await using var db = await host.DbAsync();
        var p1 = await db.Payments.SingleAsync(p => p.BookingId == t1.BookingId);
        var p2 = await db.Payments.SingleAsync(p => p.BookingId == t2.BookingId);

        // legacy format collides by design (booking-id prefix): both rows may carry it
        p1.OrNumber = "OR-F2111111"; p2.OrNumber = "OR-F2111111";
        await db.SaveChangesAsync();

        // the node sequence format may not repeat
        p1.OrNumber = "OR-AB12-20261121-0001"; p2.OrNumber = "OR-AB12-20261121-0001";
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("UNIQUE", ex.InnerException?.Message ?? ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Existing_db_with_legacy_duplicates_gets_the_index_and_rows_are_untouched()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var t1 = await host.CompletedVisitAsync("LegA");
        var t2 = await host.CompletedVisitAsync("LegB");
        await using var db = await host.DbAsync();
        await db.Database.ExecuteSqlRawAsync($"DROP INDEX IF EXISTS {SchemaRepair.OrIndexName}");
        // force at least two legacy rows regardless of key casing
        await db.Database.ExecuteSqlRawAsync("UPDATE payments SET or_number='OR-F2111111'");
        var legacy = await ScalarAsync(db, "SELECT COUNT(*) FROM payments WHERE or_number='OR-F2111111'");
        Assert.True(legacy >= 2);

        Assert.Equal(SchemaRepair.OrIndexResult.Created, await SchemaRepair.EnsureOrNumberIndexAsync(db, NullLogger.Instance));
        Assert.Equal(1, await IndexCountAsync(db));
        Assert.Equal(legacy, await ScalarAsync(db, "SELECT COUNT(*) FROM payments WHERE or_number='OR-F2111111'")); // nothing nulled or rewritten
        Assert.Equal(SchemaRepair.OrIndexResult.AlreadyPresent, await SchemaRepair.EnsureOrNumberIndexAsync(db, NullLogger.Instance));
    }

    [Fact]
    public async Task Duplicate_sequence_numbers_skip_index_creation_without_modifying_rows()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        await host.CompletedVisitAsync("DupA");
        await host.CompletedVisitAsync("DupB");
        await using var db = await host.DbAsync();
        await db.Database.ExecuteSqlRawAsync($"DROP INDEX IF EXISTS {SchemaRepair.OrIndexName}");
        await db.Database.ExecuteSqlRawAsync("UPDATE payments SET or_number='OR-AB12-20261121-0001'");
        var n = await ScalarAsync(db, "SELECT COUNT(*) FROM payments WHERE or_number='OR-AB12-20261121-0001'");
        Assert.True(n >= 2);

        Assert.Equal(SchemaRepair.OrIndexResult.SkippedDuplicates, await SchemaRepair.EnsureOrNumberIndexAsync(db, NullLogger.Instance));
        Assert.Equal(0, await IndexCountAsync(db));
        Assert.Equal(n, await ScalarAsync(db, "SELECT COUNT(*) FROM payments WHERE or_number='OR-AB12-20261121-0001'"));
    }
}
//JJQR - END
