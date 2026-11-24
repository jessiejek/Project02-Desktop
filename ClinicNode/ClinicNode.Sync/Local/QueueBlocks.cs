//JJQR - AI (21 Nov 2026) - queue-number BLOCKS (owner decision, 21 Nov 2026). Replaces per-computer numbering:
//   - while online, each sync cycle keeps a reserved block for TODAY through TODAY+6 (7 days; was today+tomorrow) in BOTH lanes (R, S), 20 numbers each, from
//     POST /api/sync/queue-blocks (the server raises the SAME per-day counter, so online check-ins never get a number inside a block);
//   - when about 5 numbers are left in a lane/day, the next online cycle reserves another block;
//   - offline (and any local booking) numbers come ONLY from a reserved block, lowest first. No block left = no number (never invented).
//   - a number that was handed out is never given again (the slip is printed with it; nothing here renumbers).
// Table node_sync_queue_blocks (sync-owned, like node_sync_state). Nothing is logged about patients.
using System.Data;
using System.Data.Common;
using ClinicApp.Domain;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ClinicNode.Sync.Local;

public sealed record QueueBlockRemaining(DateOnly Date, string Lane, int Remaining);

public static class QueueBlocks
{
    public const int BlockSize = 20;
    public const int RefillWhenRemainingAtMost = 5;
    //JJQR - AI (21 Nov 2026) - Jessie: reserve for today through today+6 (7 days inclusive), both lanes
    public const int DaysAhead = 7;
    public static IEnumerable<DateOnly> Days(DateOnly today) => Enumerable.Range(0, DaysAhead).Select(today.AddDays);
    //JJQR - END
    public static readonly string[] Lanes = [QueueLane.Regular, QueueLane.Priority];

    public static async Task EnsureSchemaAsync(ClinicAppDbContext db, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS node_sync_queue_blocks (
                id TEXT NOT NULL PRIMARY KEY,
                queue_date TEXT NOT NULL,
                lane TEXT NOT NULL,
                from_seq INTEGER NOT NULL,
                to_seq INTEGER NOT NULL,
                next_seq INTEGER NOT NULL,
                reserved_at TEXT NOT NULL
            );
            """, ct);
        await db.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS ix_node_sync_queue_blocks_day ON node_sync_queue_blocks (queue_date, lane, from_seq);", ct);
    }

    private static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Numbers still unused for <paramref name="date"/> / <paramref name="lane"/> on this computer.</summary>
    public static async Task<int> RemainingAsync(ClinicAppDbContext db, DateOnly date, string lane, CancellationToken ct = default)
    {
        await using var cmd = await CommandAsync(db,
            "SELECT COALESCE(SUM(to_seq - next_seq + 1), 0) FROM node_sync_queue_blocks WHERE queue_date = $d AND lane = $l AND next_seq <= to_seq;", ct);
        Add(cmd, "$d", Day(date));
        Add(cmd, "$l", lane);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    /// <summary>For the UI / status: what is left per day (today..today+6) and lane.</summary>
    public static async Task<IReadOnlyList<QueueBlockRemaining>> SummaryAsync(ClinicAppDbContext db, DateOnly today, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(db, ct);
        var list = new List<QueueBlockRemaining>();
        foreach (var d in Days(today)) //JJQR - AI (21 Nov 2026) - today..today+6 //JJQR - END
            foreach (var lane in Lanes)
                list.Add(new QueueBlockRemaining(d, lane, await RemainingAsync(db, d, lane, ct)));
        return list;
    }

    /// <summary>
    /// Online top-up, called by the sync cycle after a successful push/pull: today..today+6, lanes R and S. A lane/day with
    /// &lt;= <see cref="RefillWhenRemainingAtMost"/> numbers left gets one more block. Returns how many blocks were reserved.
    /// A refused / failed reservation is skipped (tried again next cycle); it never blocks the sync.
    /// </summary>
    public static async Task<int> TopUpAsync(ClinicAppDbContext db, ICloudSyncClient cloud, DateOnly today, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(db, ct);
        var reserved = 0;
        foreach (var d in Days(today)) //JJQR - AI (21 Nov 2026) - today..today+6 //JJQR - END
            foreach (var lane in Lanes)
            {
                if (await RemainingAsync(db, d, lane, ct) > RefillWhenRemainingAtMost) continue;
                var block = await cloud.ReserveQueueBlockAsync(d, lane, BlockSize, ct);
                if (block is null) continue;
                await SaveAsync(db, block, ct);
                reserved++;
            }
        return reserved;
    }

    public static async Task SaveAsync(ClinicAppDbContext db, QueueBlockReservation block, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(db, ct);
        await using var cmd = await CommandAsync(db, """
            INSERT INTO node_sync_queue_blocks (id, queue_date, lane, from_seq, to_seq, next_seq, reserved_at)
            VALUES ($id, $d, $l, $f, $t, $f, $at);
            """, ct);
        Add(cmd, "$id", Guid.NewGuid().ToString());
        Add(cmd, "$d", Day(block.Date));
        Add(cmd, "$l", block.Lane);
        Add(cmd, "$f", block.From);
        Add(cmd, "$t", block.To);
        Add(cmd, "$at", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    //JJQR - AI (Nov 24, 2026) - owner: the day starts at 1 (R-001) and an offline computer simply carries on from the last number it knows of (35 -> 36).
    // No more reserved blocks: the next number is one more than the highest number already on this computer for that day and lane (cancelled ones count, their slip was printed).
    // The server raises its own counter to the number we push, so online numbers never repeat it.
    /// <summary>
    /// Next number for the day/lane on this computer: highest known + 1 ("R-036"). Runs on the caller's connection and transaction.
    /// Never null (kept nullable for the callers that still show <see cref="NoNumbersMessage"/>).
    /// </summary>
    public static async Task<string?> TryTakeAsync(ClinicAppDbContext db, DateOnly date, string lane, CancellationToken ct = default)
    {
        var highest = 0;
        await using (var sel = await CommandAsync(db,
            "SELECT queue_number FROM bookings WHERE appointment_date = $d AND queue_number LIKE $p;", ct))
        {
            Add(sel, "$d", Day(date));
            Add(sel, "$p", lane + "-%");
            await using var r = await sel.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                if (r.IsDBNull(0)) continue;
                var text = r.GetString(0);
                var dash = text.IndexOf('-');
                if (dash > 0 && int.TryParse(text.AsSpan(dash + 1), out var n) && n > highest) highest = n;
            }
        }
        return QueueLane.Format(lane, highest + 1);
    }
    //JJQR - END

    /// <summary>Lane by the same rule as the server (<see cref="QueueLane.For"/>), then <see cref="TryTakeAsync"/>.</summary>
    public static async Task<string?> TryTakeForPatientAsync(ClinicAppDbContext db, DateOnly date, Guid patientId, string? discountCategory, CancellationToken ct = default)
    {
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == patientId, ct);
        return await TryTakeAsync(db, date, QueueLane.For(patient, discountCategory, date), ct);
    }

    /// <summary>Blocks for days that are over are dropped (unused numbers of a past day are worthless).</summary>
    public static async Task PruneAsync(ClinicAppDbContext db, DateOnly today, CancellationToken ct = default)
    {
        await EnsureSchemaAsync(db, ct);
        await using var cmd = await CommandAsync(db, "DELETE FROM node_sync_queue_blocks WHERE queue_date < $d;", ct);
        Add(cmd, "$d", Day(today));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public const string NoNumbersMessage =
        "This computer has no reserved queue numbers left for today. Connect to the internet once so it can reserve more, or check the patient in on a computer that is online.";

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

/// <summary>
/// DI-friendly wrapper for Core (registered by AddClinicNodeSync). Core's booking paths call
/// <see cref="NextForPatientAsync"/> instead of QueueSequencer.NextForPatientAsync; null => show <see cref="QueueBlocks.NoNumbersMessage"/>.
/// </summary>
public sealed class QueueBlockNumberSource : ClinicNode.Core.Queue.IQueueNumberSource //JJQR - AI (21 Nov 2026) - B //JJQR - END
{
    public Task<string?> NextForPatientAsync(ClinicAppDbContext db, DateOnly day, Guid patientId, string? discountCategory, CancellationToken ct = default) =>
        QueueBlocks.TryTakeForPatientAsync(db, day, patientId, discountCategory, ct);
}
//JJQR - END
