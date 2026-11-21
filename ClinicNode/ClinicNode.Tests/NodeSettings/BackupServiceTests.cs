//JJQR - AI (Nov 21, 2026) - BackupService on a plain SQLite node (ClinicNode.Tests ships e_sqlite3, so these cover the unencrypted path;
// the SQLCipher path is covered by packaging/sqlcipher-tests). Temp folders only.
using ClinicApp.Infrastructure;
using ClinicNode.Core.NodeSettings;
using ClinicNode.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.NodeSettings;

public class BackupServiceTests
{
    static readonly DatabaseEncryptionResult Plain = DatabaseEncryptionResult.Plaintext;
    const int Fast = 10_000;

    static async Task<int> CountOutboxAsync(TestHost host)
    {
        await using var db = await host.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        return await host.Get<ISyncOutbox>().CountPendingAsync(db);
    }

    static async Task EnqueueAsync(TestHost host, int n)
    {
        await using var db = await host.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        for (var i = 0; i < n; i++)
            await host.Get<ISyncOutbox>().EnqueueAsync(db, new SyncOutboxEntry
            {
                OperationId = Guid.NewGuid(), Aggregate = "consultation", EntityId = Guid.NewGuid(), OperationType = "update",
                PayloadJson = "{}", CreatedAt = DateTimeOffset.UtcNow,
            });
    }

    [Fact]
    public async Task Backup_then_restore_brings_back_the_old_data_and_keeps_the_current_db_aside()
    {
        await using var host = await TestHost.StartAsync();
        var dbPath = host.Get<NodeOptions>().DatabasePath;
        await EnqueueAsync(host, 3);
        var svc = new BackupService(dbPath, Plain);

        using var file = new MemoryStream();
        var header = await svc.CreateAsync(file, "correct horse", "9.9", "test-mac", Fast);
        Assert.False(header.SourceEncryptedAtRest);
        Assert.True(header.DatabaseBytes > 0);
        Assert.StartsWith("ClinicNode-backup-", BackupService.SuggestedFileName(DateTimeOffset.Now));

        await EnqueueAsync(host, 5);
        Assert.Equal(8, await CountOutboxAsync(host));

        file.Position = 0;
        var verified = await svc.VerifyAsync(file, "correct horse");
        Assert.Equal("9.9", verified.Header.AppVersion);
        Assert.Equal(3, verified.TableCounts["node_sync_outbox"]);
        SqliteConnection.ClearAllPools();
        var restored = svc.Restore(verified);

        Assert.False(File.Exists(verified.PlainSnapshotPath));         // plaintext snapshot is gone
        Assert.NotNull(restored.PreviousDatabasePath);
        Assert.Equal(8, SqlCipher.TableCounts(restored.PreviousDatabasePath!, null)["node_sync_outbox"]); // the pre-restore DB is preserved
        Assert.Equal(3, SqlCipher.TableCounts(dbPath, null)["node_sync_outbox"]);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(dbPath)!, ".*.tmp"));
        Assert.False(File.Exists(dbPath + ".restoring"));
    }

    [Fact]
    public async Task Wrong_password_or_garbage_never_touches_the_database()
    {
        await using var host = await TestHost.StartAsync();
        var dbPath = host.Get<NodeOptions>().DatabasePath;
        var svc = new BackupService(dbPath, Plain);
        using var file = new MemoryStream();
        await svc.CreateAsync(file, "correct horse", iterations: Fast);
        var before = SqlCipher.TableCounts(dbPath, null);

        file.Position = 0;
        await Assert.ThrowsAsync<BackupFormatException>(() => svc.VerifyAsync(file, "wrong password"));
        await Assert.ThrowsAsync<BackupFormatException>(() => svc.VerifyAsync(new MemoryStream("definitely not a backup"u8.ToArray()), "correct horse"));
        Assert.True(SqlCipher.SameCounts(before, SqlCipher.TableCounts(dbPath, null)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(dbPath)!, ".*.tmp")); // failed verify cleans its temp file
    }

    [Fact]
    public async Task A_valid_backup_of_some_other_database_is_refused()
    {
        await using var host = await TestHost.StartAsync();
        var dbPath = host.Get<NodeOptions>().DatabasePath;
        var other = Path.Combine(Path.GetDirectoryName(dbPath)!, "other.db");
        using (var c = new SqliteConnection($"Data Source={other};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE notes (id INTEGER); INSERT INTO notes VALUES (1);";
            cmd.ExecuteNonQuery();
        }
        using var file = new MemoryStream();
        await new BackupService(other, Plain).CreateAsync(file, "correct horse", iterations: Fast);
        file.Position = 0;
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => new BackupService(dbPath, Plain).VerifyAsync(file, "correct horse"));
        Assert.Contains("not a ClinicNode database", ex.Message);
    }

    [Fact]
    public async Task Only_the_latest_pre_restore_copy_is_kept()
    {
        await using var host = await TestHost.StartAsync();
        var dbPath = host.Get<NodeOptions>().DatabasePath;
        var svc = new BackupService(dbPath, Plain);
        using var file = new MemoryStream();
        await svc.CreateAsync(file, "correct horse", iterations: Fast);
        for (var i = 0; i < 2; i++)
        {
            file.Position = 0;
            svc.Restore(await svc.VerifyAsync(file, "correct horse"));
            await Task.Delay(1100); // pre-restore names carry seconds
        }
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(dbPath)!, "clinic-node.db.pre-restore-*").Where(f => !f.EndsWith("-wal") && !f.EndsWith("-shm")));
    }

    [Fact]
    public async Task Short_backup_password_is_refused_before_anything_is_written()
    {
        await using var host = await TestHost.StartAsync();
        var svc = new BackupService(host.Get<NodeOptions>().DatabasePath, Plain);
        using var file = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.CreateAsync(file, "short"));
        Assert.Equal(0, file.Length);
    }
}
//JJQR - END
