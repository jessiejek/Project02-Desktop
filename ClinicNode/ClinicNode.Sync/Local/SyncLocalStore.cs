//JJQR - AI (Nov 21, 2026)
// Sync-owned node tables (created here, not in ClinicNode.Data, so Data stays untouched):
//   node_sync_row_versions  - cloud row_version last seen per aggregate (base_row_version on push)
//   node_sync_conflicts     - conflict / review list (merged, conflict, rejected, flagged cloud edits,
//                             replaced MF-T- patient codes) for the UI
// Reads node_sync_outbox (owned by Data) only to ask "does this entity have unpushed local changes?".
using System.Data;
using System.Data.Common;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClinicNode.Sync.Local;

public sealed record SyncConflict
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Aggregate { get; init; } = "";
    public string EntityId { get; init; } = "";
    /// <summary>merged | conflict | rejected | cloud_edit_flagged | patient_code_replaced | illegal_transition | apply_failed | finished_immutable</summary>
    public string Kind { get; init; } = "";
    public string? ReasonCode { get; init; }
    public string? Message { get; init; }
    public Guid? OperationId { get; init; }
    public string? LocalJson { get; init; }
    public string? CloudJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; init; }
}

//JJQR - AI (Nov 21, 2026) - "reprint" signal for the UI: a queue slip (booking.queue_number changed) or a patient card (patient code replaced)
public sealed record ReprintFlag
{
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>queue_slip | patient_card</summary>
    public string Kind { get; init; } = "";
    /// <summary>booking_id for queue_slip, patient_id for patient_card.</summary>
    public string EntityId { get; init; } = "";
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Message { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
//JJQR - END

public static class SyncLocalStore
{
    public static async Task EnsureSchemaAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS node_sync_row_versions (
                aggregate TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                row_version INTEGER NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (aggregate, entity_id)
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS node_sync_conflicts (
                id TEXT NOT NULL PRIMARY KEY,
                aggregate TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                kind TEXT NOT NULL,
                reason_code TEXT NULL,
                message TEXT NULL,
                operation_id TEXT NULL,
                local_json TEXT NULL,
                cloud_json TEXT NULL,
                created_at TEXT NOT NULL,
                resolved_at TEXT NULL
            );
            """, ct);
        //JJQR - AI (Nov 21, 2026)
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS node_sync_state (key TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL);", ct);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS node_sync_reprint_flags (
                id TEXT NOT NULL PRIMARY KEY,
                kind TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                old_value TEXT NULL,
                new_value TEXT NULL,
                message TEXT NULL,
                created_at TEXT NOT NULL,
                notified INTEGER NOT NULL DEFAULT 0,
                dismissed_at TEXT NULL
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS node_sync_swept (aggregate TEXT NOT NULL, entity_id TEXT NOT NULL, PRIMARY KEY (aggregate, entity_id));", ct);
        //JJQR - END
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_node_sync_conflicts_open ON node_sync_conflicts (created_at) WHERE resolved_at IS NULL;", ct);
    }

    //JJQR - AI (Nov 21, 2026)
    public static async Task<string?> GetStateAsync(ClinicAppDbContext db, string key, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, "SELECT value FROM node_sync_state WHERE key = $k;", ct);
        Add(cmd, "$k", key);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public static async Task SetStateAsync(ClinicAppDbContext db, string key, string? value, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, value is null
            ? "DELETE FROM node_sync_state WHERE key = $k;"
            : "INSERT INTO node_sync_state (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = $v;", ct);
        Add(cmd, "$k", key);
        if (value is not null) Add(cmd, "$v", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    //JJQR - AI (21 Nov 2026) - node_sync_swept key for standalone vaccinations (pulled from the cloud, or POSTed by StandaloneVaccinationPush)
    public const string StandaloneVaccinationSweptKey = "standalone_vaccination";
    //JJQR - END

    /// <summary>Remember that an append-only row is already known to the cloud (pulled, or queued for push).</summary>
    public static async Task MarkSweptAsync(ClinicAppDbContext db, string aggregate, string entityId, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, "INSERT OR IGNORE INTO node_sync_swept (aggregate, entity_id) VALUES ($a, $e);", ct);
        Add(cmd, "$a", aggregate);
        Add(cmd, "$e", Id(entityId));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// audit_log: rows written anywhere on the node (Admin/Staff services write them without an outbox hook) that were
    /// never pushed and never pulled. Queues one append-only "create" op per row (oldest first, up to <paramref name="max"/>).
    /// </summary>
    public static async Task<int> SweepAuditLogsAsync(ClinicAppDbContext db, ClinicNode.Data.ISyncOutbox outbox, int max, CancellationToken ct = default)
    {
        var ids = await db.Database.SqlQueryRaw<string>(
            "SELECT id AS Value FROM audit_logs WHERE lower(id) NOT IN (SELECT entity_id FROM node_sync_swept WHERE aggregate = 'audit_log') LIMIT {0}", max)
            .ToListAsync(ct);
        if (ids.Count == 0) return 0;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var raw in ids)
        {
            if (!Guid.TryParse(raw, out var id)) continue;
            await outbox.EnqueueAsync(db, new ClinicNode.Data.SyncOutboxEntry
            {
                OperationId = Guid.NewGuid(),
                Aggregate = Aggregates.AuditLog,
                EntityId = id,
                OperationType = "create",
                PayloadJson = $"{{\"id\":\"{id}\"}}",
                CreatedAt = DateTimeOffset.UtcNow,
            }, ct);
            await MarkSweptAsync(db, Aggregates.AuditLog, id.ToString(), ct);
        }
        await tx.CommitAsync(ct);
        return ids.Count;
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - patient_consent is pushable (append-only): consent rows written on this computer (online or offline) that were never
    // pushed and never pulled get ONE "create" op each (the id is the consent id). The cloud answers applied / duplicate (same id again: acked, not
    // re-sent) / retry missing_dependency (patient not there yet: backoff) / rejected. Deletes are never queued (append_only). Nothing is logged.
    public static async Task<int> SweepConsentsAsync(ClinicAppDbContext db, ClinicNode.Data.ISyncOutbox outbox, int max, CancellationToken ct = default)
    {
        var et = db.Model.FindEntityType(typeof(ClinicApp.Domain.Entities.PatientConsent));
        var table = et?.GetTableName();
        var idCol = et?.FindPrimaryKey()?.Properties.Single().GetColumnName(Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table ?? "", null));
        if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(idCol)) return 0;
        var exists = await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = {0}", table).SingleAsync(ct);
        if (exists == 0) return 0; // old node file: schema repair adds it on the next start
#pragma warning disable EF1002 // table / column names come from the EF model, not from input
        var ids = await db.Database.SqlQueryRaw<string>(
            $"SELECT \"{idCol}\" AS Value FROM \"{table}\" WHERE lower(\"{idCol}\") NOT IN (SELECT entity_id FROM node_sync_swept WHERE aggregate = 'patient_consent') LIMIT {{0}}", max)
            .ToListAsync(ct);
#pragma warning restore EF1002
        if (ids.Count == 0) return 0;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var raw in ids)
        {
            if (!Guid.TryParse(raw, out var id)) continue;
            await outbox.EnqueueAsync(db, new ClinicNode.Data.SyncOutboxEntry
            {
                OperationId = Guid.NewGuid(),
                Aggregate = Aggregates.PatientConsent,
                EntityId = id,
                OperationType = "create",
                PayloadJson = $"{{\"id\":\"{id}\"}}",
                CreatedAt = DateTimeOffset.UtcNow,
            }, ct);
            await MarkSweptAsync(db, Aggregates.PatientConsent, id.ToString(), ct);
        }
        await tx.CommitAsync(ct);
        return ids.Count;
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - the cloud URL changed: cursor and cloud row versions belong to the old cloud
    public static async Task ResetForNewCloudAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM node_sync_cursor WHERE stream = 'cloud';", ct);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM node_sync_row_versions;", ct);
    }
    //JJQR - END

    public static string Id(Guid g) => g.ToString();
    public static string Id(string s) => Guid.TryParse(s, out var g) ? g.ToString() : s;

    public static async Task<long?> GetRowVersionAsync(ClinicAppDbContext db, string aggregate, string entityId, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, "SELECT row_version FROM node_sync_row_versions WHERE aggregate = $a AND entity_id = $e;", ct);
        Add(cmd, "$a", aggregate);
        Add(cmd, "$e", Id(entityId));
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is null or DBNull ? null : System.Convert.ToInt64(r);
    }

    public static async Task SetRowVersionAsync(ClinicAppDbContext db, string aggregate, string entityId, long rowVersion, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, """
            INSERT INTO node_sync_row_versions (aggregate, entity_id, row_version, updated_at)
            VALUES ($a, $e, $v, $at)
            ON CONFLICT(aggregate, entity_id) DO UPDATE SET row_version = $v, updated_at = $at;
            """, ct);
        Add(cmd, "$a", aggregate);
        Add(cmd, "$e", Id(entityId));
        Add(cmd, "$v", rowVersion);
        Add(cmd, "$at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>True when the outbox still has an unacked operation for this aggregate/entity.</summary>
    public static async Task<bool> HasPendingAsync(ClinicAppDbContext db, string aggregate, string entityId, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db,
            "SELECT 1 FROM node_sync_outbox WHERE acked_at IS NULL AND aggregate = $a AND lower(entity_id) = lower($e) LIMIT 1;", ct);
        Add(cmd, "$a", aggregate);
        Add(cmd, "$e", Id(entityId));
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    public static async Task AddConflictAsync(ClinicAppDbContext db, SyncConflict c, CancellationToken ct = default)
    {
        //JJQR - AI (Nov 21, 2026) - one open review item per (aggregate, entity, kind, reason): every op of a rejected consultation
        // used to add its own copy (real-DB run: 8 identical rows). The newest message/payloads replace the open one.
        await using (var upd = await CommandAsync(db, """
            UPDATE node_sync_conflicts SET message = $m, operation_id = $op, local_json = $l, cloud_json = $c, created_at = $at
            WHERE resolved_at IS NULL AND aggregate = $a AND lower(entity_id) = lower($e) AND kind = $k AND ifnull(reason_code, '') = ifnull($r, '');
            """, ct))
        {
            Add(upd, "$a", c.Aggregate); Add(upd, "$e", Id(c.EntityId)); Add(upd, "$k", c.Kind); Add(upd, "$r", c.ReasonCode);
            Add(upd, "$m", c.Message); Add(upd, "$op", c.OperationId?.ToString()); Add(upd, "$l", c.LocalJson); Add(upd, "$c", c.CloudJson);
            Add(upd, "$at", c.CreatedAt.ToString("O"));
            if (await upd.ExecuteNonQueryAsync(ct) > 0) return;
        }
        //JJQR - END
        await using var cmd = await CommandAsync(db, """
            INSERT INTO node_sync_conflicts (id, aggregate, entity_id, kind, reason_code, message, operation_id, local_json, cloud_json, created_at, resolved_at)
            VALUES ($id, $a, $e, $k, $r, $m, $op, $l, $c, $at, NULL);
            """, ct);
        Add(cmd, "$id", c.Id.ToString());
        Add(cmd, "$a", c.Aggregate);
        Add(cmd, "$e", Id(c.EntityId));
        Add(cmd, "$k", c.Kind);
        Add(cmd, "$r", c.ReasonCode);
        Add(cmd, "$m", c.Message);
        Add(cmd, "$op", c.OperationId?.ToString());
        Add(cmd, "$l", c.LocalJson);
        Add(cmd, "$c", c.CloudJson);
        Add(cmd, "$at", c.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<IReadOnlyList<SyncConflict>> ListOpenConflictsAsync(ClinicAppDbContext db, int take = 200, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, """
            SELECT id, aggregate, entity_id, kind, reason_code, message, operation_id, local_json, cloud_json, created_at
            FROM node_sync_conflicts WHERE resolved_at IS NULL ORDER BY created_at DESC LIMIT $t;
            """, ct);
        Add(cmd, "$t", take);
        var list = new List<SyncConflict>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new SyncConflict
            {
                Id = Guid.Parse(r.GetString(0)),
                Aggregate = r.GetString(1),
                EntityId = r.GetString(2),
                Kind = r.GetString(3),
                ReasonCode = r.IsDBNull(4) ? null : r.GetString(4),
                Message = r.IsDBNull(5) ? null : r.GetString(5),
                OperationId = r.IsDBNull(6) ? null : Guid.Parse(r.GetString(6)),
                LocalJson = r.IsDBNull(7) ? null : r.GetString(7),
                CloudJson = r.IsDBNull(8) ? null : r.GetString(8),
                CreatedAt = DateTimeOffset.Parse(r.GetString(9)),
            });
        }
        return list;
    }

    public static async Task<int> CountOpenConflictsAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, "SELECT COUNT(*) FROM node_sync_conflicts WHERE resolved_at IS NULL;", ct);
        return System.Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public static async Task ResolveConflictAsync(ClinicAppDbContext db, Guid id, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, "UPDATE node_sync_conflicts SET resolved_at = $at WHERE id = $id;", ct);
        Add(cmd, "$at", DateTimeOffset.UtcNow.ToString("O"));
        Add(cmd, "$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    //JJQR - AI (Nov 21, 2026)
    public static async Task AddReprintFlagAsync(ClinicAppDbContext db, ReprintFlag f, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, """
            INSERT INTO node_sync_reprint_flags (id, kind, entity_id, old_value, new_value, message, created_at, notified)
            VALUES ($id, $k, $e, $o, $n, $m, $at, 0);
            """, ct);
        Add(cmd, "$id", f.Id.ToString()); Add(cmd, "$k", f.Kind); Add(cmd, "$e", Id(f.EntityId));
        Add(cmd, "$o", f.OldValue); Add(cmd, "$n", f.NewValue); Add(cmd, "$m", f.Message); Add(cmd, "$at", f.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<IReadOnlyList<ReprintFlag>> ListReprintFlagsAsync(ClinicAppDbContext db, bool onlyUnnotified = false, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, "SELECT id, kind, entity_id, old_value, new_value, message, created_at FROM node_sync_reprint_flags WHERE dismissed_at IS NULL"
            + (onlyUnnotified ? " AND notified = 0" : "") + " ORDER BY created_at;", ct);
        var list = new List<ReprintFlag>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            list.Add(new ReprintFlag
            {
                Id = Guid.Parse(r.GetString(0)), Kind = r.GetString(1), EntityId = r.GetString(2),
                OldValue = r.IsDBNull(3) ? null : r.GetString(3), NewValue = r.IsDBNull(4) ? null : r.GetString(4),
                Message = r.IsDBNull(5) ? null : r.GetString(5), CreatedAt = DateTimeOffset.Parse(r.GetString(6)),
            });
        return list;
    }

    public static async Task MarkReprintNotifiedAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, "UPDATE node_sync_reprint_flags SET notified = 1 WHERE notified = 0;", ct);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<int> CountReprintFlagsAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, "SELECT COUNT(*) FROM node_sync_reprint_flags WHERE dismissed_at IS NULL;", ct);
        return System.Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public static async Task DismissReprintFlagAsync(ClinicAppDbContext db, Guid id, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db, "UPDATE node_sync_reprint_flags SET dismissed_at = $at WHERE id = $id;", ct);
        Add(cmd, "$at", DateTimeOffset.UtcNow.ToString("O")); Add(cmd, "$id", id.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Many row versions in one statement per 300 rows (first-pull catalogs are ~12k rows).</summary>
    public static async Task SetRowVersionsAsync(ClinicAppDbContext db, IReadOnlyList<(string Aggregate, string EntityId, long RowVersion)> rows, CancellationToken ct = default)
    {
        const int chunk = 300;
        var at = DateTimeOffset.UtcNow.ToString("O");
        for (var i = 0; i < rows.Count; i += chunk)
        {
            var part = rows.Skip(i).Take(chunk).ToList();
            var sql = new System.Text.StringBuilder("INSERT INTO node_sync_row_versions (aggregate, entity_id, row_version, updated_at) VALUES ");
            for (var j = 0; j < part.Count; j++) sql.Append(j == 0 ? "" : ",").Append($"($a{j}, $e{j}, $v{j}, $at)");
            sql.Append(" ON CONFLICT(aggregate, entity_id) DO UPDATE SET row_version = excluded.row_version, updated_at = excluded.updated_at;");
            await using var cmd = await CommandAsync(db, sql.ToString(), ct);
            for (var j = 0; j < part.Count; j++)
            {
                Add(cmd, $"$a{j}", part[j].Aggregate); Add(cmd, $"$e{j}", Id(part[j].EntityId)); Add(cmd, $"$v{j}", part[j].RowVersion);
            }
            Add(cmd, "$at", at);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
    //JJQR - END

    private static async Task<DbCommand> CommandAsync(ClinicAppDbContext db, string sql, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var cmd = conn.CreateCommand();
        var tx = db.Database.CurrentTransaction?.GetDbTransaction();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void Add(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
//JJQR - END
