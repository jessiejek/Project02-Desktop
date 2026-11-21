// JJQR - AI (Nov 21, 2026) - two demo nodes must not share well-known visit IDs (sync clash).
using ClinicApp.Infrastructure;
using ClinicNode.Core;
using ClinicNode.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests;

public class DemoSeedIdsTests
{
    static readonly Guid FixedBookingA = Guid.Parse("f2111111-1111-4111-8111-111111111201");
    static readonly Guid FixedBookingB = Guid.Parse("f2111111-1111-4111-8111-111111111202");
    static readonly Guid FixedBookingC = Guid.Parse("f2111111-1111-4111-8111-111111111203");

    [Fact]
    public async Task Two_demo_nodes_mint_distinct_booking_ids_and_do_not_plant_the_shared_dev_guids()
    {
        var idsA = await SeedBookingIdsAsync();
        var idsB = await SeedBookingIdsAsync();
        Assert.NotEmpty(idsA);
        Assert.NotEmpty(idsB);
        Assert.Empty(idsA.Intersect(idsB));
        var forbidden = new[] { FixedBookingA, FixedBookingB, FixedBookingC };
        Assert.Empty(idsA.Intersect(forbidden));
        Assert.Empty(idsB.Intersect(forbidden));
    }

    [Fact]
    public async Task Non_demo_init_does_not_insert_the_shared_dev_booking_ids()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clinic-node-tests", "nodemo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var sc = new ServiceCollection();
            sc.AddLogging();
            sc.AddClinicNodeCore(new NodeOptions { DataDirectory = dir, Demo = false });
            await using var sp = sc.BuildServiceProvider();
            await sp.GetRequiredService<LocalDatabase>().InitializeAsync();
            await using var db = await sp.GetRequiredService<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
            var planted = await db.Bookings.CountAsync(b =>
                b.BookingId == FixedBookingA || b.BookingId == FixedBookingB || b.BookingId == FixedBookingC);
            Assert.Equal(0, planted);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    static async Task<HashSet<Guid>> SeedBookingIdsAsync()
    {
        var dir = Path.Combine(Path.GetTempPath(), "clinic-node-tests", "demo-ids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddClinicNodeCore(new NodeOptions { DataDirectory = dir, Demo = true });
        await using var sp = sc.BuildServiceProvider();
        await sp.GetRequiredService<LocalDatabase>().InitializeAsync();
        await using var db = await sp.GetRequiredService<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        var ids = (await db.Bookings.Select(b => b.BookingId).ToListAsync()).ToHashSet();
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        return ids;
    }
}
// JJQR - END
