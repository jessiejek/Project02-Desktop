//JJQR - AI (Nov 21, 2026) - local backup / restore of the node DB. Platform-neutral: the app supplies the file (picker) and the restart.
// Backup: consistent snapshot (sqlcipher_export or VACUUM INTO) → .cnbackup (BackupFormat, password). Restore: decrypt to a temp file,
// verify (GCM tags, size, integrity_check, tables), keep the current DB as .pre-restore-*, then swap in (re-encrypted with this node's key).
using Microsoft.Data.Sqlite;

namespace ClinicNode.Core.NodeSettings;

public sealed record BackupVerification(BackupHeader Header, string PlainSnapshotPath, IReadOnlyDictionary<string, long> TableCounts);

public sealed record RestoreResult(string DatabasePath, string? PreviousDatabasePath, int Tables);

public sealed class BackupService
{
    readonly string _dbPath;
    readonly DatabaseEncryptionResult _encryption;

    /// <summary>Tables a ClinicNode DB must have to be accepted on restore.</summary>
    public static readonly string[] RequiredTables = ["node_sync_outbox"];

    public BackupService(string dbPath, DatabaseEncryptionResult encryption)
    {
        _dbPath = dbPath;
        _encryption = encryption;
    }

    string WorkDir => Path.GetDirectoryName(Path.GetFullPath(_dbPath))!;
    string TempPath(string tag) => Path.Combine(WorkDir, $".{tag}-{Guid.NewGuid():N}.tmp");

    public static string SuggestedFileName(DateTimeOffset now) => $"ClinicNode-backup-{now:yyyyMMdd-HHmm}{BackupFormat.FileExtension}";

    /// <summary>Writes an encrypted backup of the live DB to <paramref name="output"/>.</summary>
    public async Task<BackupHeader> CreateAsync(Stream output, string password, string? appVersion = null, string? machine = null,
        int iterations = BackupFormat.DefaultIterations, CancellationToken ct = default)
    {
        BackupFormat.ValidatePassword(password);
        var snap = TempPath("backup");
        try
        {
            if (_encryption.Key is { } key) SqlCipher.Export(_dbPath, key, snap, null);
            else SqlCipher.VacuumInto(_dbPath, snap);
            SqliteConnection.ClearPool(new SqliteConnection(SqlCipher.ConnectionString(snap, null)));
            await using var input = new FileStream(snap, FileMode.Open, FileAccess.Read, FileShare.Read);
            return await BackupFormat.WriteAsync(input, output, password, new BackupHeader
            {
                CreatedAt = DateTimeOffset.Now, AppVersion = appVersion, Machine = machine,
                DatabaseBytes = input.Length, SourceEncryptedAtRest = _encryption.Key is not null,
            }, iterations, ct: ct);
        }
        finally { SqlCipher.SecureDelete(snap); }
    }

    /// <summary>Decrypts and checks a backup into a temp plaintext snapshot (caller must Restore or Discard it).</summary>
    public async Task<BackupVerification> VerifyAsync(Stream backup, string password, CancellationToken ct = default)
    {
        var snap = TempPath("restore");
        try
        {
            BackupHeader header;
            await using (var plain = new FileStream(snap, FileMode.CreateNew, FileAccess.Write))
                header = await BackupFormat.ReadAsync(backup, plain, password, ct);
            if (SqlCipher.Inspect(snap) != SqlCipher.FileKind.Plaintext)
                throw new BackupFormatException("Backup does not contain a SQLite database.");
            if (!SqlCipher.IntegrityOk(snap, null, out var detail))
                throw new BackupFormatException("Backup database failed its integrity check: " + detail);
            var counts = SqlCipher.TableCounts(snap, null);
            var missing = RequiredTables.Where(t => !counts.ContainsKey(t)).ToList();
            if (missing.Count > 0) throw new BackupFormatException("Backup is not a ClinicNode database (missing " + string.Join(", ", missing) + ").");
            SqliteConnection.ClearAllPools();
            return new BackupVerification(header, snap, counts);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            SqlCipher.SecureDelete(snap);
            throw;
        }
    }

    public static void Discard(BackupVerification v) => SqlCipher.SecureDelete(v.PlainSnapshotPath);

    /// <summary>
    /// Swaps the verified snapshot in. The current DB is kept as clinic-node.db.pre-restore-yyyyMMdd-HHmmss (older ones are removed).
    /// The app must restart afterwards: services still hold the old file open.
    /// </summary>
    public RestoreResult Restore(BackupVerification v)
    {
        SqliteConnection.ClearAllPools();
        var staged = _dbPath + ".restoring";
        if (File.Exists(staged)) File.Delete(staged);
        try
        {
            if (_encryption.Key is { } key) SqlCipher.Export(v.PlainSnapshotPath, null, staged, key);
            else File.Copy(v.PlainSnapshotPath, staged);
            if (!SqlCipher.IntegrityOk(staged, _encryption.Key, out var detail) || !SqlCipher.SameCounts(v.TableCounts, SqlCipher.TableCounts(staged, _encryption.Key)))
                throw new BackupFormatException("Restored copy failed verification: " + detail);
            SqliteConnection.ClearAllPools();

            string? previous = null;
            if (File.Exists(_dbPath))
            {
                foreach (var old in Directory.GetFiles(WorkDir, Path.GetFileName(_dbPath) + ".pre-restore-*").Where(f => !f.EndsWith("-wal") && !f.EndsWith("-shm")))
                    SqlCipher.SecureDelete(old);
                previous = $"{_dbPath}.pre-restore-{DateTime.Now:yyyyMMdd-HHmmss}";
                SqlCipher.MoveDatabase(_dbPath, previous);
            }
            File.Move(staged, _dbPath);
            return new RestoreResult(_dbPath, previous, v.TableCounts.Count);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(staged)) SqlCipher.SecureDelete(staged);
            SqlCipher.SecureDelete(v.PlainSnapshotPath);
        }
    }
}
//JJQR - END
