//JJQR - AI (Nov 22, 2026) - a computer whose local database predates a table the backend model added (sync_review_items) must repair itself on start, not fail to sign in
using ClinicApp.Infrastructure;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests;

public class SchemaRepairNewTableTests
{
    [Fact]
    public async Task A_missing_table_with_a_filtered_unique_index_is_created_again_on_start()
    {
        await using var host = await TestHost.StartAsync();
        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS sync_review_items");
            await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS ix_patients_merged_into_patient_id");
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE patients DROP COLUMN merged_into_patient_id"); // an older database also lacks the new column
        }

        await using (var repair = await factory.CreateDbContextAsync())
            await SchemaRepair.EnsureModelColumnsAsync(repair, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);   // what the app does at start

        await using var after = await factory.CreateDbContextAsync();
        Assert.Equal(0, await after.SyncReviewItems.CountAsync());   // the table is back and readable
        Assert.Equal(0, await after.Patients.CountAsync(p => p.MergedIntoPatientId != null));   // and so is the column
    }
}
//JJQR - END
