// JJQR - AI (Nov 21, 2026) - DI for the local EF Core SQLite database.
using ClinicApp.Auth;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClinicNodeData(this IServiceCollection services, NodeOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
        Directory.CreateDirectory(options.DataDirectory);

        services.AddSingleton(options);
        services.AddSingleton<PasswordHasherService>();
        services.AddSingleton<ISyncOutbox, SyncOutbox>();
        services.AddSingleton<ClinicApp.Infrastructure.IPatientCodeAllocator, NodePatientCodeAllocator>();
        services.AddSingleton<LocalDatabase>();

        // The node syncs through its outbox, not the cloud change-log interceptor.
        // Existing files may also lack sync_changes; writing with the log on would throw.
        var builder = new DbContextOptionsBuilder<ClinicAppDbContext>();
        builder.UseSqlite(options.SqliteConnectionString)
               .UseSnakeCaseNamingConvention();
        services.AddSingleton<IDbContextFactory<ClinicAppDbContext>>(
            new ClinicNodeDbContextFactory(builder.Options));

        return services;
    }
}

sealed class ClinicNodeDbContextFactory : IDbContextFactory<ClinicAppDbContext>
{
    private readonly DbContextOptions<ClinicAppDbContext> _options;

    public ClinicNodeDbContextFactory(DbContextOptions<ClinicAppDbContext> options) => _options = options;

    public ClinicAppDbContext CreateDbContext()
    {
        var ctx = new ClinicAppDbContext(_options);
        ctx.SyncChangeLogEnabled = false;
        return ctx;
    }
}
// JJQR - END
