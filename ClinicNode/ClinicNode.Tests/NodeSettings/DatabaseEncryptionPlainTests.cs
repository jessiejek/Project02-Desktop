//JJQR - AI (Nov 21, 2026) - what happens when the native library is plain SQLite (this project's runtime): nothing is touched.
// The SQLCipher-enabled behaviour is in packaging/sqlcipher-tests.
using ClinicNode.Core.NodeSettings;

namespace ClinicNode.Tests.NodeSettings;

public class DatabaseEncryptionPlainTests
{
    [Fact]
    public void Test_runtime_is_plain_sqlite()
    {
        Assert.False(SqlCipher.IsAvailable());
        Assert.Null(SqlCipher.Version());
    }

    [Fact]
    public async Task Without_sqlcipher_a_plain_database_is_left_alone_and_no_key_is_created()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cn-enc-plain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var db = Path.Combine(dir, "clinic-node.db");
            using (var c = SqlCipher.Open(db, null)) { using var cmd = c.CreateCommand(); cmd.CommandText = "CREATE TABLE t(x)"; cmd.ExecuteNonQuery(); }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var bytes = await File.ReadAllBytesAsync(db);
            var store = new InMemoryDatabaseKeyStore();
            var r = await DatabaseEncryption.PrepareAsync(db, store);
            Assert.Equal(DatabaseEncryptionState.CipherUnavailable, r.State);
            Assert.False(r.Encrypted);
            Assert.Null(store.Key);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(db));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task An_encrypted_database_without_the_library_is_a_hard_error_not_a_silent_reset()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cn-enc-plain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var db = Path.Combine(dir, "clinic-node.db");
            await File.WriteAllBytesAsync(db, System.Security.Cryptography.RandomNumberGenerator.GetBytes(4096)); // looks like SQLCipher: random from byte 0
            await Assert.ThrowsAsync<DatabaseKeyUnavailableException>(() => DatabaseEncryption.PrepareAsync(db, new InMemoryDatabaseKeyStore("k")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(false, "Data Source=/x/y.db")]
    [InlineData(true, "Password=abc")]
    public void Connection_string_carries_the_key_only_when_given(bool withKey, string expected)
    {
        var cs = SqlCipher.ConnectionString("/x/y.db", withKey ? "abc" : null);
        Assert.Contains(expected.Split('=')[0], cs, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(withKey, cs.Contains("Password=abc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void File_inspection_tells_plain_from_encrypted_from_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cn-enc-plain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(SqlCipher.FileKind.Missing, SqlCipher.Inspect(Path.Combine(dir, "nope")));
            var empty = Path.Combine(dir, "empty"); File.WriteAllBytes(empty, []);
            Assert.Equal(SqlCipher.FileKind.Empty, SqlCipher.Inspect(empty));
            var plain = Path.Combine(dir, "plain"); File.WriteAllBytes(plain, [.. "SQLite format 3\0"u8, .. new byte[100]]);
            Assert.Equal(SqlCipher.FileKind.Plaintext, SqlCipher.Inspect(plain));
            var enc = Path.Combine(dir, "enc"); File.WriteAllBytes(enc, [.. new byte[16].Select((_, i) => (byte)(i + 1)), .. new byte[100]]);
            Assert.Equal(SqlCipher.FileKind.EncryptedOrUnknown, SqlCipher.Inspect(enc));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Keys_are_random_256_bit_hex() 
    {
        var a = SqlCipher.NewKey(); var b = SqlCipher.NewKey();
        Assert.Equal(64, a.Length);
        Assert.Matches("^[0-9A-F]{64}$", a);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Secure_delete_removes_the_file_and_its_side_files()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cn-enc-plain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var f = Path.Combine(dir, "x.db");
            foreach (var p in new[] { f, f + "-wal", f + "-shm" }) File.WriteAllBytes(p, new byte[1000]);
            SqlCipher.SecureDelete(f);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
//JJQR - END
