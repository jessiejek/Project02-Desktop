//JJQR - AI (Nov 21, 2026) - SQLCipher helpers on Microsoft.Data.Sqlite. No compile-time SQLCipher reference: the native library is whatever
// SQLitePCLRaw bundle the app ships (bundle_e_sqlcipher after the csproj swap). IsAvailable() tells the two apart at runtime, so this code
// builds and runs (as a safe no-op) before the swap, and encrypts after it.
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace ClinicNode.Core.NodeSettings;

public static class SqlCipher
{
    static readonly byte[] PlainHeader = "SQLite format 3\0"u8.ToArray();
    static bool? _available;

    /// <summary>True when the loaded native SQLite is SQLCipher (<c>PRAGMA cipher_version</c> answers).</summary>
    public static bool IsAvailable()
    {
        if (_available is { } a) return a;
        try
        {
            using var c = new SqliteConnection("Data Source=:memory:");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA cipher_version;";
            _available = cmd.ExecuteScalar() is string v && v.Length > 0;
        }
        catch { _available = false; }
        return _available.Value;
    }

    /// <summary>SQLCipher version string, or null when the native library is plain SQLite.</summary>
    public static string? Version()
    {
        if (!IsAvailable()) return null;
        using var c = new SqliteConnection("Data Source=:memory:");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA cipher_version;";
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Connection string for the node DB; <paramref name="key"/> null = plaintext. Password makes Microsoft.Data.Sqlite issue PRAGMA key on every new native connection (pool-safe).</summary>
    public static string ConnectionString(string path, string? key, bool pooling = true)
    {
        var b = new SqliteConnectionStringBuilder { DataSource = path, Pooling = pooling };
        if (!string.IsNullOrEmpty(key)) b.Password = key;
        return b.ToString();
    }

    public enum FileKind { Missing, Empty, Plaintext, EncryptedOrUnknown }

    /// <summary>Plain SQLite files start with "SQLite format 3\0"; SQLCipher files are random from byte 0.</summary>
    public static FileKind Inspect(string path)
    {
        if (!File.Exists(path)) return FileKind.Missing;
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (f.Length == 0) return FileKind.Empty;
        var head = new byte[16];
        var n = f.Read(head, 0, 16);
        return n == 16 && head.AsSpan().SequenceEqual(PlainHeader) ? FileKind.Plaintext : FileKind.EncryptedOrUnknown;
    }

    /// <summary>New random 256-bit key as 64 hex chars (used as a SQLCipher passphrase; PBKDF2 inside SQLCipher).</summary>
    public static string NewKey() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public static SqliteConnection Open(string path, string? key, bool pooling = false)
    {
        var c = new SqliteConnection(ConnectionString(path, key, pooling));
        c.Open();
        return c;
    }

    /// <summary>
    /// Copies <paramref name="sourcePath"/> (opened with <paramref name="sourceKey"/>) into a NEW file <paramref name="targetPath"/> keyed with
    /// <paramref name="targetKey"/> ("" or null = plaintext) using sqlcipher_export. Copies schema, data, views, triggers and user_version.
    /// </summary>
    public static void Export(string sourcePath, string? sourceKey, string targetPath, string? targetKey)
    {
        if (!IsAvailable()) throw new InvalidOperationException("SQLCipher is not loaded (SQLitePCLRaw.bundle_e_sqlcipher missing).");
        if (File.Exists(targetPath)) File.Delete(targetPath);
        using var c = Open(sourcePath, sourceKey);
        Exec(c, "PRAGMA wal_checkpoint(TRUNCATE);");
        var userVersion = Convert.ToInt64(Scalar(c, "PRAGMA user_version;") ?? 0L, CultureInfo.InvariantCulture);
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "ATTACH DATABASE $path AS cn_target KEY $key;";
            cmd.Parameters.AddWithValue("$path", targetPath);
            cmd.Parameters.AddWithValue("$key", targetKey ?? "");
            cmd.ExecuteNonQuery();
        }
        try
        {
            Scalar(c, "SELECT sqlcipher_export('cn_target');");
            Exec(c, $"PRAGMA cn_target.user_version = {userVersion.ToString(CultureInfo.InvariantCulture)};");
        }
        finally
        {
            Exec(c, "DETACH DATABASE cn_target;");
        }
    }

    /// <summary>Plain-SQLite snapshot (VACUUM INTO) for nodes running without SQLCipher.</summary>
    public static void VacuumInto(string sourcePath, string targetPath)
    {
        if (File.Exists(targetPath)) File.Delete(targetPath);
        using var c = Open(sourcePath, null);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "VACUUM INTO $path;";
        cmd.Parameters.AddWithValue("$path", targetPath);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Row count of every user table (name → count). Used to prove a copy holds the same data.</summary>
    public static SortedDictionary<string, long> TableCounts(SqliteConnection c)
    {
        var names = new List<string>();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) names.Add(r.GetString(0));
        }
        var result = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var n in names)
            result[n] = Convert.ToInt64(Scalar(c, $"SELECT COUNT(*) FROM \"{n.Replace("\"", "\"\"")}\";"), CultureInfo.InvariantCulture);
        return result;
    }

    public static SortedDictionary<string, long> TableCounts(string path, string? key)
    {
        using var c = Open(path, key);
        return TableCounts(c);
    }

    /// <summary>"ok" from PRAGMA integrity_check, plus a readable schema (wrong key → SqliteException "file is not a database").</summary>
    public static bool IntegrityOk(string path, string? key, out string detail)
    {
        try
        {
            using var c = Open(path, key);
            detail = Scalar(c, "PRAGMA integrity_check;") as string ?? "";
            return detail == "ok";
        }
        catch (SqliteException ex)
        {
            detail = ex.Message;
            return false;
        }
    }

    public static bool SameCounts(IReadOnlyDictionary<string, long> a, IReadOnlyDictionary<string, long> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    /// <summary>Overwrites the file with zeros, flushes, then deletes it (and its -wal/-shm/-journal). Best effort on SSDs/APFS (copy-on-write).</summary>
    public static void SecureDelete(string path)
    {
        foreach (var p in new[] { path, path + "-wal", path + "-shm", path + "-journal" })
        {
            if (!File.Exists(p)) continue;
            try
            {
                using (var f = new FileStream(p, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    var zero = new byte[1 << 16];
                    for (long left = f.Length; left > 0; left -= zero.Length)
                        f.Write(zero, 0, (int)Math.Min(zero.Length, left));
                    f.Flush(flushToDisk: true);
                }
            }
            catch (IOException) { /* still delete below */ }
            File.Delete(p);
        }
    }

    /// <summary>Moves a DB and its -wal/-shm side files together.</summary>
    public static void MoveDatabase(string from, string to)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(from + suffix)) File.Move(from + suffix, to + suffix, overwrite: true);
        File.Move(from, to, overwrite: true);
    }

    static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
//JJQR - END
