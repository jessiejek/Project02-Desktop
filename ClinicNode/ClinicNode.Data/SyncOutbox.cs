// JJQR - AI (Nov 21, 2026) - ADO.NET outbox on the ClinicAppDbContext connection so enqueue
// can share the EF SaveChanges transaction.
using System.Data;
using System.Data.Common;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClinicNode.Data;

public sealed class SyncOutbox : ISyncOutbox
{
    public Task EnsureSchemaAsync(ClinicAppDbContext db, CancellationToken ct = default) =>
        SyncSchema.EnsureCreatedAsync(db, ct);

    public async Task EnqueueAsync(ClinicAppDbContext db, SyncOutboxEntry entry, CancellationToken ct = default)
    {
        var (cmd, mustDispose) = CreateCommand(db);
        try
        {
            cmd.CommandText = """
                INSERT INTO node_sync_outbox
                    (operation_id, aggregate, entity_id, operation_type, payload_json, created_at, attempts, last_error, last_attempt_at, acked_at)
                VALUES
                    ($operation_id, $aggregate, $entity_id, $operation_type, $payload_json, $created_at, 0, NULL, NULL, NULL)
                ON CONFLICT(operation_id) DO NOTHING;
                """;
            Add(cmd, "$operation_id", entry.OperationId.ToString());
            Add(cmd, "$aggregate", entry.Aggregate);
            Add(cmd, "$entity_id", entry.EntityId.ToString());
            Add(cmd, "$operation_type", entry.OperationType);
            Add(cmd, "$payload_json", entry.PayloadJson);
            Add(cmd, "$created_at", entry.CreatedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (mustDispose) await cmd.DisposeAsync();
        }
    }

    public async Task<IReadOnlyList<SyncOutboxEntry>> ListPendingAsync(ClinicAppDbContext db, int take = 50, CancellationToken ct = default)
    {
        var (cmd, mustDispose) = CreateCommand(db);
        try
        {
            cmd.CommandText = """
                SELECT operation_id, aggregate, entity_id, operation_type, payload_json, created_at,
                       attempts, last_error, last_attempt_at, acked_at
                FROM node_sync_outbox
                WHERE acked_at IS NULL
                ORDER BY created_at
                LIMIT $take;
                """;
            Add(cmd, "$take", take);
            var list = new List<SyncOutboxEntry>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                list.Add(Read(reader));
            return list;
        }
        finally
        {
            if (mustDispose) await cmd.DisposeAsync();
        }
    }

    public async Task MarkAckedAsync(ClinicAppDbContext db, Guid operationId, CancellationToken ct = default)
    {
        var (cmd, mustDispose) = CreateCommand(db);
        try
        {
            cmd.CommandText = "UPDATE node_sync_outbox SET acked_at = $at WHERE operation_id = $id;";
            Add(cmd, "$at", DateTimeOffset.UtcNow.ToString("O"));
            Add(cmd, "$id", operationId.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (mustDispose) await cmd.DisposeAsync();
        }
    }

    public async Task MarkAttemptAsync(ClinicAppDbContext db, Guid operationId, string? error, CancellationToken ct = default)
    {
        var (cmd, mustDispose) = CreateCommand(db);
        try
        {
            cmd.CommandText = """
                UPDATE node_sync_outbox
                SET attempts = attempts + 1, last_error = $err, last_attempt_at = $at
                WHERE operation_id = $id;
                """;
            Add(cmd, "$err", error);
            Add(cmd, "$at", DateTimeOffset.UtcNow.ToString("O"));
            Add(cmd, "$id", operationId.ToString());
            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            if (mustDispose) await cmd.DisposeAsync();
        }
    }

    public async Task<int> CountPendingAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        var (cmd, mustDispose) = CreateCommand(db);
        try
        {
            cmd.CommandText = "SELECT COUNT(*) FROM node_sync_outbox WHERE acked_at IS NULL;";
            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt32(result);
        }
        finally
        {
            if (mustDispose) await cmd.DisposeAsync();
        }
    }

    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.5: "Sync problem" = a pending change retried this many times
    public const int StuckAttempts = 10;

    public async Task<int> CountStuckAsync(ClinicAppDbContext db, int minAttempts, CancellationToken ct = default)
    {
        var (cmd, mustDispose) = CreateCommand(db);
        try
        {
            cmd.CommandText = "SELECT COUNT(*) FROM node_sync_outbox WHERE acked_at IS NULL AND attempts >= $n;";
            Add(cmd, "$n", minAttempts);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }
        finally
        {
            if (mustDispose) await cmd.DisposeAsync();
        }
    }
    //JJQR - END

    public static async Task<NodeIdentity> GetOrCreateIdentityAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        var conn = await OpenAsync(db, ct);
        await using var cmd = conn.CreateCommand();
        BindTransaction(db, cmd);
        cmd.CommandText = "SELECT node_id, created_at, last_successful_sync_at, cloud_base_url FROM node_identity LIMIT 1;";
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                return new NodeIdentity
                {
                    NodeId = Guid.Parse(reader.GetString(0)),
                    CreatedAt = DateTimeOffset.Parse(reader.GetString(1)),
                    LastSuccessfulSyncAt = reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                    CloudBaseUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
                };
            }
        }

        var identity = new NodeIdentity { NodeId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
        cmd.Parameters.Clear();
        cmd.CommandText = "INSERT INTO node_identity (node_id, created_at, last_successful_sync_at, cloud_base_url) VALUES ($id, $at, NULL, NULL);";
        Add(cmd, "$id", identity.NodeId.ToString());
        Add(cmd, "$at", identity.CreatedAt.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
        return identity;
    }

    public static async Task SetLastSuccessfulSyncAsync(ClinicAppDbContext db, DateTimeOffset at, CancellationToken ct = default)
    {
        var conn = await OpenAsync(db, ct);
        await using var cmd = conn.CreateCommand();
        BindTransaction(db, cmd);
        cmd.CommandText = "UPDATE node_identity SET last_successful_sync_at = $at;";
        Add(cmd, "$at", at.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task SetCursorAsync(ClinicAppDbContext db, string stream, string cursor, CancellationToken ct = default)
    {
        var conn = await OpenAsync(db, ct);
        await using var cmd = conn.CreateCommand();
        BindTransaction(db, cmd);
        cmd.CommandText = """
            INSERT INTO node_sync_cursor (stream, cursor, updated_at)
            VALUES ($stream, $cursor, $at)
            ON CONFLICT(stream) DO UPDATE SET cursor = $cursor, updated_at = $at;
            """;
        Add(cmd, "$stream", stream);
        Add(cmd, "$cursor", cursor);
        Add(cmd, "$at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public static async Task<string?> GetCursorAsync(ClinicAppDbContext db, string stream, CancellationToken ct = default)
    {
        var conn = await OpenAsync(db, ct);
        await using var cmd = conn.CreateCommand();
        BindTransaction(db, cmd);
        cmd.CommandText = "SELECT cursor FROM node_sync_cursor WHERE stream = $stream;";
        Add(cmd, "$stream", stream);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    private static (DbCommand cmd, bool mustDispose) CreateCommand(ClinicAppDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            conn.Open();
        var cmd = conn.CreateCommand();
        BindTransaction(db, cmd);
        return (cmd, true);
    }

    private static async Task<DbConnection> OpenAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await conn.OpenAsync(ct);
        return conn;
    }

    private static void BindTransaction(ClinicAppDbContext db, DbCommand cmd)
    {
        var tx = db.Database.CurrentTransaction?.GetDbTransaction();
        if (tx is not null) cmd.Transaction = tx;
    }

    private static void Add(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static SyncOutboxEntry Read(DbDataReader reader) => new()
    {
        OperationId = Guid.Parse(reader.GetString(0)),
        Aggregate = reader.GetString(1),
        EntityId = Guid.Parse(reader.GetString(2)),
        OperationType = reader.GetString(3),
        PayloadJson = reader.GetString(4),
        CreatedAt = DateTimeOffset.Parse(reader.GetString(5)),
        Attempts = reader.GetInt32(6),
        LastError = reader.IsDBNull(7) ? null : reader.GetString(7),
        LastAttemptAt = reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
        AckedAt = reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
    };
}
// JJQR - END
