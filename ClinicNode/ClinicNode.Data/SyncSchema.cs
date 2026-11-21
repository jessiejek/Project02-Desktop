// JJQR - AI (Nov 21, 2026) - node-only tables in the same SQLite file, created with raw SQL
// so we do not edit ClinicAppDbContext (a production model).
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Data;

public static class SyncSchema
{
    public const string Outbox = "node_sync_outbox";
    public const string Cursor = "node_sync_cursor";
    public const string Identity = "node_identity";
    public const string PatientCodeBlock = "node_patient_code_blocks";

    public static async Task EnsureCreatedAsync(ClinicApp.Infrastructure.ClinicAppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS node_identity (
                node_id TEXT NOT NULL PRIMARY KEY,
                created_at TEXT NOT NULL,
                last_successful_sync_at TEXT NULL,
                cloud_base_url TEXT NULL
            );
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS node_sync_outbox (
                operation_id TEXT NOT NULL PRIMARY KEY,
                aggregate TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                operation_type TEXT NOT NULL,
                payload_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL,
                last_attempt_at TEXT NULL,
                acked_at TEXT NULL
            );
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE INDEX IF NOT EXISTS ix_node_sync_outbox_pending
            ON node_sync_outbox (created_at) WHERE acked_at IS NULL;
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS node_sync_cursor (
                stream TEXT NOT NULL PRIMARY KEY,
                cursor TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """, ct);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS node_patient_code_blocks (
                block_id TEXT NOT NULL PRIMARY KEY,
                prefix TEXT NOT NULL,
                start_inclusive INTEGER NOT NULL,
                end_inclusive INTEGER NOT NULL,
                next_value INTEGER NOT NULL,
                reserved_at TEXT NOT NULL
            );
            """, ct);
    }
}
// JJQR - END
