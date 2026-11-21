// JJQR - AI (Nov 21, 2026) - offline patient codes come from a per-node reserved MF- block; overflow is MF-T- temporary.
using System.Data;
using System.Globalization;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClinicNode.Data;

/// <summary>Issues <c>MF-000123</c> from <c>node_patient_code_blocks</c>. When the reserved
/// block is exhausted the node issues a temporary <c>MF-T-0001</c> to be replaced on sync.</summary>
public sealed class NodePatientCodeAllocator : IPatientCodeAllocator
{
    public const string OfficialPrefix = "MF-";
    public const string TemporaryPrefix = "MF-T-";
    public const int DefaultBlockSize = 100;

    public async Task<string> NextAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        var conn = db.Database.GetDbConnection();
        var opened = conn.State != ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();

            cmd.CommandText = """
                SELECT block_id, next_value, end_inclusive
                FROM node_patient_code_blocks
                WHERE prefix = 'MF-' AND next_value <= end_inclusive
                ORDER BY reserved_at
                LIMIT 1;
                """;
            await using (var reader = await cmd.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    var blockId = reader.GetString(0);
                    var next = reader.GetInt64(1);
                    await reader.DisposeAsync();

                    await using var upd = conn.CreateCommand();
                    upd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
                    upd.CommandText = """
                        UPDATE node_patient_code_blocks
                        SET next_value = next_value + 1
                        WHERE block_id = $id AND next_value = $expected;
                        """;
                    var pId = upd.CreateParameter(); pId.ParameterName = "$id"; pId.Value = blockId; upd.Parameters.Add(pId);
                    var pEx = upd.CreateParameter(); pEx.ParameterName = "$expected"; pEx.Value = next; upd.Parameters.Add(pEx);
                    var affected = await upd.ExecuteNonQueryAsync(ct);
                    if (affected == 1)
                        return $"{OfficialPrefix}{next.ToString("D6", CultureInfo.InvariantCulture)}";
                }
            }

            return await NextTemporaryAsync(conn, db, ct);
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }

    static async Task<string> NextTemporaryAsync(IDbConnection conn, ClinicAppDbContext db, CancellationToken ct)
    {
        await using var cmd = ((Microsoft.Data.Sqlite.SqliteConnection)conn).CreateCommand();
        cmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction() as Microsoft.Data.Sqlite.SqliteTransaction;
        cmd.CommandText = """
            SELECT COALESCE(MAX(CAST(SUBSTR(patient_code, 6) AS INTEGER)), 0) + 1
            FROM patients
            WHERE patient_code GLOB 'MF-T-[0-9]*' AND SUBSTR(patient_code, 6) NOT GLOB '*[^0-9]*';
            """;
        var result = await cmd.ExecuteScalarAsync(ct);
        var n = Convert.ToInt64(result, CultureInfo.InvariantCulture);
        return $"{TemporaryPrefix}{n.ToString("D4", CultureInfo.InvariantCulture)}";
    }

    /// <summary>If no official block is reserved yet, reserve one starting after the highest local MF- code
    /// (or 100000). This stands in for the cloud "pre-reserve a block" call until sync exists.</summary>
    public static async Task EnsureDefaultBlockAsync(ClinicAppDbContext db, int size = DefaultBlockSize, CancellationToken ct = default)
    {
        var existing = await db.Database.ExecuteSqlRawAsync(
            "SELECT 1 FROM node_patient_code_blocks WHERE prefix = 'MF-' AND next_value <= end_inclusive LIMIT 1;", ct);
        // ExecuteSqlRawAsync returns rows-affected (0 for SELECT). Use a scalar instead.
        var conn = db.Database.GetDbConnection();
        var opened = conn.State != ConnectionState.Open;
        if (opened) await conn.OpenAsync(ct);
        try
        {
            await using var check = conn.CreateCommand();
            check.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            check.CommandText = "SELECT COUNT(*) FROM node_patient_code_blocks WHERE prefix = 'MF-' AND next_value <= end_inclusive;";
            var count = Convert.ToInt64(await check.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            if (count > 0) return;

            await using var maxCmd = conn.CreateCommand();
            maxCmd.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            maxCmd.CommandText = """
                SELECT COALESCE(MAX(CAST(SUBSTR(patient_code, 4) AS INTEGER)), 0)
                FROM patients
                WHERE patient_code GLOB 'MF-[0-9]*' AND SUBSTR(patient_code, 4) NOT GLOB '*[^0-9]*';
                """;
            var max = Convert.ToInt64(await maxCmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            var start = Math.Max(100000, max + 1);
            var end = start + size - 1;
            var now = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            await using var ins = conn.CreateCommand();
            ins.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            ins.CommandText = """
                INSERT INTO node_patient_code_blocks
                    (block_id, prefix, start_inclusive, end_inclusive, next_value, reserved_at)
                VALUES ($id, 'MF-', $start, $end, $start, $at);
                """;
            void add(string n, object v) { var p = ins.CreateParameter(); p.ParameterName = n; p.Value = v; ins.Parameters.Add(p); }
            add("$id", Guid.NewGuid().ToString());
            add("$start", start);
            add("$end", end);
            add("$at", now);
            await ins.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (opened) await conn.CloseAsync();
        }
    }
}
// JJQR - END
