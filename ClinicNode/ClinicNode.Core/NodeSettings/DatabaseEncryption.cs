//JJQR - AI (Nov 21, 2026) - encrypt the node SQLite at rest: per-install random key in the OS vault, one-time plaintext → SQLCipher migration
// with a verified plaintext backup that is wiped only after the encrypted copy proves identical. Crash-safe: every step can be re-run.
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ClinicNode.Core.NodeSettings;

/// <summary>Where the DB key lives (Keychain / DPAPI via MAUI SecureStorage in the app; memory/file in tests).</summary>
public interface IDatabaseKeyStore
{
    Task<string?> GetAsync(CancellationToken ct = default);
    Task SetAsync(string key, CancellationToken ct = default);
}

public sealed class InMemoryDatabaseKeyStore(string? key = null) : IDatabaseKeyStore
{
    public string? Key { get; private set; } = key;
    public bool FailOnSet { get; set; }
    public Task<string?> GetAsync(CancellationToken ct = default) => Task.FromResult(Key);
    public Task SetAsync(string key, CancellationToken ct = default)
    {
        if (FailOnSet) throw new InvalidOperationException("key store unavailable (test)");
        Key = key;
        return Task.CompletedTask;
    }
}

public enum DatabaseEncryptionState
{
    /// <summary>Native SQLite is not SQLCipher (bundle not swapped yet): DB stays plaintext, nothing touched.</summary>
    CipherUnavailable,
    /// <summary>The OS vault refused the key: DB stays plaintext (never encrypt with a key we could lose).</summary>
    KeyStoreUnavailable,
    /// <summary>No DB yet: EF will create it encrypted.</summary>
    NewDatabase,
    AlreadyEncrypted,
    /// <summary>A plaintext DB was converted this run.</summary>
    Migrated,
}

public sealed record DatabaseEncryptionResult(DatabaseEncryptionState State, string? Key, string Message)
{
    public bool Encrypted => Key is not null;
    public static readonly DatabaseEncryptionResult Plaintext = new(DatabaseEncryptionState.CipherUnavailable, null, "not prepared");
}

public sealed class DatabaseKeyUnavailableException(string message) : Exception(message);

public static class DatabaseEncryption
{
    public static string PlaintextBackupPath(string dbPath) => dbPath + ".plaintext-backup";
    public static string EncryptingPath(string dbPath) => dbPath + ".encrypting";

    /// <summary>
    /// Call once at startup BEFORE anything opens the DB. Returns the key to put in the connection string (null = plaintext).
    /// Never pass the owner's live data folder from tests: use a temp copy.
    /// </summary>
    public static async Task<DatabaseEncryptionResult> PrepareAsync(string dbPath, IDatabaseKeyStore store, ILogger? logger = null, CancellationToken ct = default)
    {
        logger ??= NullLogger.Instance;
        var backup = PlaintextBackupPath(dbPath);
        var encrypting = EncryptingPath(dbPath);

        if (!SqlCipher.IsAvailable())
        {
            if (SqlCipher.Inspect(dbPath) == SqlCipher.FileKind.EncryptedOrUnknown)
                throw new DatabaseKeyUnavailableException("The clinic database is encrypted but this build has no SQLCipher library (SQLitePCLRaw.bundle_e_sqlcipher).");
            logger.LogWarning("SQLCipher not loaded; the local database stays unencrypted.");
            return new(DatabaseEncryptionState.CipherUnavailable, null, "SQLCipher not loaded; database not encrypted.");
        }

        // leftovers of an interrupted run
        if (File.Exists(encrypting)) File.Delete(encrypting);
        var key = await store.GetAsync(ct);
        if (File.Exists(backup))
        {
            if (SqlCipher.Inspect(dbPath) == SqlCipher.FileKind.EncryptedOrUnknown && key is not null && SqlCipher.IntegrityOk(dbPath, key, out _))
            {
                SqlCipher.SecureDelete(backup);   // migration had finished; only the wipe was missed
                logger.LogInformation("Removed leftover plaintext backup after a verified migration.");
            }
            else if (SqlCipher.Inspect(dbPath) is SqlCipher.FileKind.Missing or SqlCipher.FileKind.Empty)
            {
                SqlCipher.MoveDatabase(backup, dbPath); // swap was interrupted: put the original back and migrate again
                logger.LogWarning("Restored the plaintext original after an interrupted migration.");
            }
        }

        switch (SqlCipher.Inspect(dbPath))
        {
            case SqlCipher.FileKind.Missing:
            case SqlCipher.FileKind.Empty:
                key ??= await CreateKeyAsync(store, logger, ct);
                return key is null
                    ? new(DatabaseEncryptionState.KeyStoreUnavailable, null, "Secure storage unavailable; new database will not be encrypted.")
                    : new(DatabaseEncryptionState.NewDatabase, key, "New encrypted database.");

            case SqlCipher.FileKind.Plaintext:
                key ??= await CreateKeyAsync(store, logger, ct);
                if (key is null) return new(DatabaseEncryptionState.KeyStoreUnavailable, null, "Secure storage unavailable; database left unencrypted.");
                Migrate(dbPath, key, logger);
                return new(DatabaseEncryptionState.Migrated, key, "Database encrypted (SQLCipher).");

            default:
                if (key is null)
                    throw new DatabaseKeyUnavailableException("The clinic database is encrypted but its key is missing from this computer's secure storage. Restore from a backup.");
                if (!SqlCipher.IntegrityOk(dbPath, key, out var detail))
                    throw new DatabaseKeyUnavailableException("The clinic database could not be opened with the stored key: " + detail);
                return new(DatabaseEncryptionState.AlreadyEncrypted, key, "Database encrypted (SQLCipher).");
        }
    }

    /// <summary>Generate, store, and read back the key; null when the vault does not keep it.</summary>
    static async Task<string?> CreateKeyAsync(IDatabaseKeyStore store, ILogger logger, CancellationToken ct)
    {
        var key = SqlCipher.NewKey();
        try
        {
            await store.SetAsync(key, ct);
            if (await store.GetAsync(ct) == key) return key;
            logger.LogError("Secure storage did not return the database key that was just saved.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Secure storage refused the database key.");
        }
        return null;
    }

    /// <summary>Plaintext → encrypted, in place. Original kept as .plaintext-backup until the encrypted file verifies, then wiped.</summary>
    public static void Migrate(string dbPath, string key, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var backup = PlaintextBackupPath(dbPath);
        var encrypting = EncryptingPath(dbPath);

        SortedDictionary<string, long> before;
        using (var plain = SqlCipher.Open(dbPath, null)) before = SqlCipher.TableCounts(plain);

        SqlCipher.Export(dbPath, null, encrypting, key);
        if (!SqlCipher.IntegrityOk(encrypting, key, out var detail) || !SqlCipher.SameCounts(before, SqlCipher.TableCounts(encrypting, key)))
        {
            File.Delete(encrypting);
            throw new InvalidOperationException("Encrypted copy failed verification; plaintext database left as is. " + detail);
        }

        SqliteConnection.ClearAllPools();
        SqlCipher.MoveDatabase(dbPath, backup);
        File.Move(encrypting, dbPath);

        if (!SqlCipher.IntegrityOk(dbPath, key, out detail) || !SqlCipher.SameCounts(before, SqlCipher.TableCounts(dbPath, key)))
        {
            File.Delete(dbPath);
            SqlCipher.MoveDatabase(backup, dbPath);
            throw new InvalidOperationException("Encrypted database failed verification after the swap; original restored. " + detail);
        }

        SqlCipher.SecureDelete(backup);
        logger.LogInformation("Local database encrypted with SQLCipher ({Tables} tables verified).", before.Count);
    }
}
//JJQR - END
