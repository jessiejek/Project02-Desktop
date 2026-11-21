// JJQR - AI (Nov 21, 2026) - in-place schema repair for existing node DBs whose files predate new EF
// tables and columns (notably ISyncTracked: row_version, deleted_at, origin_node_id, and sync_changes).
// EnsureCreated is a no-op when the file already exists.
using System.Data;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging;

namespace ClinicNode.Data;

public static class SchemaRepair
{
    public static async Task EnsureModelColumnsAsync(ClinicAppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var missingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedCols = 0;

        foreach (var entity in db.Model.GetEntityTypes())
        {
            if (entity.IsOwned()) continue;
            var table = entity.GetTableName();
            if (string.IsNullOrWhiteSpace(table)) continue;
            var existing = await ColumnNamesAsync(conn, table, ct);
            if (existing.Count == 0)
            {
                missingTables.Add(table);
                continue;
            }

            addedCols += await AddMissingColumnsAsync(db, entity, table, existing, logger, ct);
        }

        if (missingTables.Count > 0)
        {
            await CreateMissingTablesAsync(db, missingTables, logger, ct);
            // Columns on brand-new tables come from the create script; still repair any that arrived incomplete.
            foreach (var entity in db.Model.GetEntityTypes())
            {
                if (entity.IsOwned()) continue;
                var table = entity.GetTableName();
                if (string.IsNullOrWhiteSpace(table) || !missingTables.Contains(table)) continue;
                var existing = await ColumnNamesAsync(conn, table, ct);
                if (existing.Count == 0) continue;
                addedCols += await AddMissingColumnsAsync(db, entity, table, existing, logger, ct);
            }
        }

        if (addedCols > 0)
            logger.LogWarning("Schema repair added {Count} missing column(s) to the local database.", addedCols);

        // JJQR - AI (Nov 21, 2026) - one official receipt number per payment on this node
        await EnsureOrNumberIndexAsync(db, logger, ct);
        // JJQR - END
    }

    // JJQR - AI (Nov 21, 2026) - unique OR index (node SQLite only)
    public const string OrIndexName = "ix_payments_or_number";

    /// <summary>Only numbers in the node sequence format (OR-XXXX-yyyymmdd-nnnn) are unique. Legacy OR-xxxxxxxx values
    /// (booking-id prefix, written by the web/cloud and the old fallback) collide by design and are left untouched,
    /// so the index can never fail on, or reject a pulled cloud row carrying, a legacy duplicate.</summary>
    public const string OrIndexPredicate = "or_number IS NOT NULL AND or_number GLOB 'OR-????-????????-*'";

    public enum OrIndexResult { NoPaymentsTable, Created, AlreadyPresent, SkippedDuplicates }

    /// <summary>CREATE UNIQUE INDEX IF NOT EXISTS ix_payments_or_number ON payments(or_number) WHERE &lt;sequence-format only&gt;.
    /// Idempotent. If rows in the sequence format already collide (should not happen), nothing is modified: the index is skipped and logged.</summary>
    public static async Task<OrIndexResult> EnsureOrNumberIndexAsync(ClinicAppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var cols = await ColumnNamesAsync(conn, "payments", ct);
        if (!cols.Contains("or_number")) return OrIndexResult.NoPaymentsTable;

        await using (var exists = conn.CreateCommand())
        {
            exists.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{OrIndexName}'";
            if (Convert.ToInt64(await exists.ExecuteScalarAsync(ct)) > 0) return OrIndexResult.AlreadyPresent;
        }

        var dups = new List<string>();
        await using (var q = conn.CreateCommand())
        {
            q.CommandText = $"SELECT or_number, COUNT(*) FROM payments WHERE {OrIndexPredicate} GROUP BY or_number HAVING COUNT(*) > 1";
            await using var r = await q.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct)) dups.Add($"{r.GetString(0)} x{r.GetInt64(1)}");
        }
        if (dups.Count > 0)
        {
            logger.LogError("Skipped unique index {Index}: duplicate sequence-format OR numbers exist ({Dups}). Resolve them manually.",
                OrIndexName, string.Join(", ", dups.Take(10)));
            return OrIndexResult.SkippedDuplicates;
        }

        await db.Database.ExecuteSqlRawAsync(
            $"CREATE UNIQUE INDEX IF NOT EXISTS {OrIndexName} ON payments(or_number) WHERE {OrIndexPredicate}", ct);
        logger.LogInformation("Created unique index {Index} on payments(or_number).", OrIndexName);
        return OrIndexResult.Created;
    }
    // JJQR - END

    static async Task CreateMissingTablesAsync(ClinicAppDbContext db, HashSet<string> missing, ILogger logger, CancellationToken ct)
    {
        var differ = db.GetService<IMigrationsModelDiffer>();
        var operations = differ.GetDifferences(null, db.Model.GetRelationalModel());
        var wanted = operations.Where(op => ForMissingTable(op, missing)).ToList();
        if (wanted.Count == 0)
        {
            logger.LogWarning("Schema repair found missing tables ({Tables}) but no create operations.", string.Join(", ", missing));
            return;
        }

        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(wanted, db.Model);
        foreach (var cmd in commands)
            await db.Database.ExecuteSqlRawAsync(cmd.CommandText, ct);

        logger.LogWarning("Schema repair created missing table(s): {Tables}.", string.Join(", ", missing.OrderBy(t => t)));
    }

    static bool ForMissingTable(MigrationOperation op, HashSet<string> missing) => op switch
    {
        CreateTableOperation t => missing.Contains(t.Name),
        CreateIndexOperation i => missing.Contains(i.Table),
        AddForeignKeyOperation f => missing.Contains(f.Table),
        AddPrimaryKeyOperation p => missing.Contains(p.Table),
        AddUniqueConstraintOperation u => missing.Contains(u.Table),
        AddCheckConstraintOperation c => missing.Contains(c.Table),
        _ => false,
    };

    static async Task<int> AddMissingColumnsAsync(ClinicAppDbContext db, IEntityType entity, string table, HashSet<string> existing, ILogger logger, CancellationToken ct)
    {
        var added = 0;
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        foreach (var prop in entity.GetProperties())
        {
            var col = prop.GetColumnName(store);
            if (string.IsNullOrWhiteSpace(col) || existing.Contains(col)) continue;

            var type = prop.GetColumnType() ?? "TEXT";
            var suffix = "";
            if (!prop.IsNullable)
            {
                var t = type.ToLowerInvariant();
                suffix = t.Contains("int") || t is "integer" or "bigint" or "smallint"
                         || t.Contains("decimal") || t.Contains("numeric") || t.Contains("real")
                         || t.Contains("float") || t.Contains("double")
                    ? " NOT NULL DEFAULT 0"
                    : " NOT NULL DEFAULT ''";
            }

            var sql = $"ALTER TABLE \"{Quote(table)}\" ADD COLUMN \"{Quote(col)}\" {type}{suffix}";
            await db.Database.ExecuteSqlRawAsync(sql, ct);
            existing.Add(col);
            added++;
            logger.LogWarning("Added missing column {Table}.{Column} ({Type}).", table, col, type);
        }
        return added;
    }

    static async Task<HashSet<string>> ColumnNamesAsync(System.Data.Common.DbConnection conn, string table, CancellationToken ct)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{Quote(table)}\")";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(1)) names.Add(reader.GetString(1));
        }
        return names;
    }

    static string Quote(string ident) => ident.Replace("\"", "\"\"", StringComparison.Ordinal);
}
// JJQR - END
