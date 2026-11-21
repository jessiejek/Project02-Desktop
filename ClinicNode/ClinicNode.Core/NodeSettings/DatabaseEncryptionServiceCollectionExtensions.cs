//JJQR - AI (Nov 21, 2026) - points the ClinicAppDbContext factory at the encrypted file without editing ClinicNode.Data.
// ClinicNode.Data now registers a fixed-options singleton factory (no AddDbContextFactory), so ConfigureDbContext no longer reaches it.
// We therefore replace that registration with one that mirrors it (SQLite + snake_case + SyncChangeLogEnabled=false) but with the SQLCipher key.
// If ClinicNode.Data's factory ever changes, keep the two in step, or use the cleaner alternative in docs/status/allen5.md (key in NodeOptions).
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.Core.NodeSettings;

public static class DatabaseEncryptionServiceCollectionExtensions
{
    /// <summary>Call AFTER AddClinicNodeData/AddClinicNodeCore (i.e. after MauiProgram.AddClinicNodeAppServices).</summary>
    public static IServiceCollection AddClinicNodeDatabaseEncryption(this IServiceCollection services, string dbPath, DatabaseEncryptionResult result)
    {
        services.AddSingleton(result);
        if (result.Key is { } key)
        {
            var builder = new DbContextOptionsBuilder<ClinicAppDbContext>();
            builder.UseSqlite(SqlCipher.ConnectionString(dbPath, key)).UseSnakeCaseNamingConvention();
            services.RemoveAll<IDbContextFactory<ClinicAppDbContext>>();
            services.AddSingleton<IDbContextFactory<ClinicAppDbContext>>(new EncryptedClinicDbContextFactory(builder.Options));
        }
        return services;
    }
}

/// <summary>Same behaviour as ClinicNode.Data's factory: the node syncs through its outbox, not the cloud change-log interceptor.</summary>
public sealed class EncryptedClinicDbContextFactory : IDbContextFactory<ClinicAppDbContext>
{
    private readonly DbContextOptions<ClinicAppDbContext> _options;
    public EncryptedClinicDbContextFactory(DbContextOptions<ClinicAppDbContext> options) => _options = options;

    public ClinicAppDbContext CreateDbContext()
    {
        var ctx = new ClinicAppDbContext(_options);
        ctx.SyncChangeLogEnabled = false;
        return ctx;
    }
}
//JJQR - END
