//JJQR - AI (Nov 21, 2026) - SQLCipher end-to-end. Every test works in its own temp folder; never the owner's app data.
using ClinicApp.Infrastructure;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Core.NodeSettings;
using ClinicNode.Data;
using ClinicNode.Sync;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.SqlCipherTests;

public sealed class TempNode : IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "clinic-node-sqlcipher-tests", Guid.NewGuid().ToString("N"));
    public TempNode() => Directory.CreateDirectory(Dir);
    public NodeOptions Options => new() { DataDirectory = Dir, Demo = true };
    public string Db => Options.DatabasePath;

    /// <summary>The real graph (Core + Data + Sync), optionally with the encrypted connection hookup exactly as MauiProgram would do it.</summary>
    public async Task<ServiceProvider> StartAsync(DatabaseEncryptionResult? encryption)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClinicNodeCore(Options);
        services.AddClinicNodeSync();
        if (encryption is not null) services.AddClinicNodeDatabaseEncryption(Db, encryption);
        var sp = services.BuildServiceProvider();
        await sp.GetRequiredService<LocalDatabase>().InitializeAsync();
        return sp;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Dir, true); } catch { }
    }
}

public class EncryptedDatabaseTests
{
    static SyncOutboxEntry Entry(Guid id) => new()
    {
        OperationId = id, Aggregate = "consultation", EntityId = Guid.NewGuid(), OperationType = "update",
        PayloadJson = "{\"x\":1}", CreatedAt = DateTimeOffset.UtcNow,
    };

    static async Task<Guid> SeedOutboxAsync(ServiceProvider sp)
    {
        var id = Guid.NewGuid();
        await using var db = await sp.GetRequiredService<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        await sp.GetRequiredService<ISyncOutbox>().EnqueueAsync(db, Entry(id));
        return id;
    }

    [Fact]
    public void SqlCipher_is_the_loaded_native_library()
    {
        Assert.True(SqlCipher.IsAvailable());
        Assert.False(string.IsNullOrEmpty(SqlCipher.Version()));
    }

    [Fact]
    public async Task Plain_database_migrates_and_EF_plus_sync_outbox_work_on_the_encrypted_file()
    {
        using var node = new TempNode();
        Guid queued;
        int users;
        await using (var plain = await node.StartAsync(null))
        {
            queued = await SeedOutboxAsync(plain);
            await using var db = await plain.GetRequiredService<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
            users = await db.Users.CountAsync();
        }
        SqliteConnection.ClearAllPools();
        Assert.Equal(SqlCipher.FileKind.Plaintext, SqlCipher.Inspect(node.Db));
        var before = SqlCipher.TableCounts(node.Db, null);

        var store = new InMemoryDatabaseKeyStore();
        var result = await DatabaseEncryption.PrepareAsync(node.Db, store);

        Assert.Equal(DatabaseEncryptionState.Migrated, result.State);
        Assert.Equal(store.Key, result.Key);
        Assert.Equal(SqlCipher.FileKind.EncryptedOrUnknown, SqlCipher.Inspect(node.Db));
        Assert.False(File.Exists(DatabaseEncryption.PlaintextBackupPath(node.Db)));   // wiped after verification
        Assert.False(File.Exists(DatabaseEncryption.EncryptingPath(node.Db)));
        Assert.True(SqlCipher.SameCounts(before, SqlCipher.TableCounts(node.Db, result.Key)));
        Assert.False(SqlCipher.IntegrityOk(node.Db, null, out _));            // unreadable without the key
        Assert.False(SqlCipher.IntegrityOk(node.Db, SqlCipher.NewKey(), out _)); // or with another key

        await using var enc = await node.StartAsync(result);
        // EF Core reads/writes
        await using (var db = await enc.GetRequiredService<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync())
        {
            Assert.Equal(users, await db.Users.CountAsync());
            var outbox = enc.GetRequiredService<ISyncOutbox>();
            Assert.Contains(await outbox.ListPendingAsync(db, 500), e => e.OperationId == queued);
            var second = Guid.NewGuid();
            await outbox.EnqueueAsync(db, Entry(second));
            await outbox.MarkAckedAsync(db, queued);
            var pending = await outbox.ListPendingAsync(db, 500);
            Assert.Contains(pending, e => e.OperationId == second);
            Assert.DoesNotContain(pending, e => e.OperationId == queued);
        }
        // full login path (auth service, hashing, sessions) on the encrypted DB
        var login = await enc.GetRequiredService<IAuthService>().LoginAsync("admin@clinic.test", DevDataSeeder.DevPassword, rememberEmail: false);
        Assert.True(login.Succeeded, login.Error);
        // sync status reads the outbox through its own context
        var status = enc.GetRequiredService<ISyncStatus>();
        await status.RefreshAsync();
        Assert.True(status.PendingCount >= 1);
    }

    [Fact]
    public async Task Second_start_reuses_the_key_and_reports_already_encrypted()
    {
        using var node = new TempNode();
        var store = new InMemoryDatabaseKeyStore();
        var first = await DatabaseEncryption.PrepareAsync(node.Db, store);
        Assert.Equal(DatabaseEncryptionState.NewDatabase, first.State);
        await using (var sp = await node.StartAsync(first)) await SeedOutboxAsync(sp);
        SqliteConnection.ClearAllPools();
        Assert.Equal(SqlCipher.FileKind.EncryptedOrUnknown, SqlCipher.Inspect(node.Db)); // created encrypted by EF

        var again = await DatabaseEncryption.PrepareAsync(node.Db, store);
        Assert.Equal(DatabaseEncryptionState.AlreadyEncrypted, again.State);
        Assert.Equal(first.Key, again.Key);
    }

    [Fact]
    public async Task Missing_key_for_an_encrypted_database_is_a_hard_stop_not_a_reset()
    {
        using var node = new TempNode();
        var first = await DatabaseEncryption.PrepareAsync(node.Db, new InMemoryDatabaseKeyStore());
        await using (var sp = await node.StartAsync(first)) { }
        SqliteConnection.ClearAllPools();
        var size = new FileInfo(node.Db).Length;
        await Assert.ThrowsAsync<DatabaseKeyUnavailableException>(() => DatabaseEncryption.PrepareAsync(node.Db, new InMemoryDatabaseKeyStore()));
        Assert.Equal(size, new FileInfo(node.Db).Length); // untouched
    }

    [Fact]
    public async Task Vault_failure_leaves_the_plain_database_untouched()
    {
        using var node = new TempNode();
        await using (var sp = await node.StartAsync(null)) await SeedOutboxAsync(sp);
        SqliteConnection.ClearAllPools();
        var bytes = await File.ReadAllBytesAsync(node.Db);
        var result = await DatabaseEncryption.PrepareAsync(node.Db, new InMemoryDatabaseKeyStore { FailOnSet = true });
        Assert.Equal(DatabaseEncryptionState.KeyStoreUnavailable, result.State);
        Assert.Null(result.Key);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(node.Db));
    }

    [Fact]
    public async Task Interrupted_swap_is_recovered_from_the_plaintext_backup()
    {
        using var node = new TempNode();
        await using (var sp = await node.StartAsync(null)) await SeedOutboxAsync(sp);
        SqliteConnection.ClearAllPools();
        var before = SqlCipher.TableCounts(node.Db, null);
        // simulate a crash right after "move original aside"
        SqlCipher.MoveDatabase(node.Db, DatabaseEncryption.PlaintextBackupPath(node.Db));
        await File.WriteAllTextAsync(DatabaseEncryption.EncryptingPath(node.Db), "partial");

        var result = await DatabaseEncryption.PrepareAsync(node.Db, new InMemoryDatabaseKeyStore());
        Assert.Equal(DatabaseEncryptionState.Migrated, result.State);
        Assert.True(SqlCipher.SameCounts(before, SqlCipher.TableCounts(node.Db, result.Key)));
        Assert.False(File.Exists(DatabaseEncryption.PlaintextBackupPath(node.Db)));
    }

    [Fact]
    public async Task Backup_from_one_encrypted_node_restores_on_another_with_a_different_key()
    {
        using var a = new TempNode();
        using var b = new TempNode();
        var keyA = await DatabaseEncryption.PrepareAsync(a.Db, new InMemoryDatabaseKeyStore());
        var keyB = await DatabaseEncryption.PrepareAsync(b.Db, new InMemoryDatabaseKeyStore());
        Guid queued;
        await using (var sp = await a.StartAsync(keyA)) queued = await SeedOutboxAsync(sp);
        await using (var sp = await b.StartAsync(keyB)) { }
        SqliteConnection.ClearAllPools();

        var file = Path.Combine(a.Dir, "out" + BackupFormat.FileExtension);
        await using (var fs = File.Create(file))
            await new BackupService(a.Db, keyA).CreateAsync(fs, "correct horse", "test", "mac", iterations: 20_000);
        var raw = await File.ReadAllBytesAsync(file);
        Assert.Equal(-1, IndexOf(raw, "SQLite format 3"u8.ToArray()));   // nothing readable in the backup
        Assert.Equal(-1, IndexOf(raw, "node_sync_outbox"u8.ToArray()));

        var svcB = new BackupService(b.Db, keyB);
        await using (var fs = File.OpenRead(file))
            await Assert.ThrowsAsync<BackupFormatException>(() => svcB.VerifyAsync(fs, "wrong password"));
        BackupVerification v;
        await using (var fs = File.OpenRead(file)) v = await svcB.VerifyAsync(fs, "correct horse");
        var restored = svcB.Restore(v);
        Assert.False(File.Exists(v.PlainSnapshotPath));
        Assert.NotNull(restored.PreviousDatabasePath);
        Assert.True(File.Exists(restored.PreviousDatabasePath));
        Assert.Equal(SqlCipher.FileKind.EncryptedOrUnknown, SqlCipher.Inspect(b.Db)); // re-encrypted with B's key
        Assert.False(SqlCipher.IntegrityOk(b.Db, keyA.Key, out _));

        await using var spB = await b.StartAsync(keyB);
        await using var db = await spB.GetRequiredService<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        Assert.Contains(await spB.GetRequiredService<ISyncOutbox>().ListPendingAsync(db, 500), e => e.OperationId == queued);
        Assert.Empty(Directory.GetFiles(b.Dir, "*.tmp", SearchOption.AllDirectories)); // no plaintext temp left behind
        Assert.Empty(Directory.GetFiles(a.Dir, ".*.tmp"));
    }

    static int IndexOf(byte[] hay, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= hay.Length; i++)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }
}
//JJQR - END
