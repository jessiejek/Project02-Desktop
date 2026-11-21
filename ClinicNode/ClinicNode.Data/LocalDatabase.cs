// JJQR - AI (Nov 21, 2026) - bootstrap the local SQLite copy: schema, views, catalogs, optional demo seed.
using System.Data;
using ClinicApp.Auth;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace ClinicNode.Data;

public sealed class LocalDatabase
{
    private readonly NodeOptions _options;
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly PasswordHasherService _hasher;
    private readonly ILogger<LocalDatabase> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task? _init;

    public LocalDatabase(
        NodeOptions options,
        IDbContextFactory<ClinicAppDbContext> factory,
        PasswordHasherService hasher,
        ILogger<LocalDatabase> logger,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _factory = factory;
        _hasher = hasher;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public string DatabasePath => _options.DatabasePath;

    /// <summary>The in-flight or completed initialization. Login and the shell await this before touching the DB.</summary>
    public Task Initialization => _init ?? Task.CompletedTask;

    public string? InitializationError { get; private set; }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        if (_init is { IsCompleted: true }) return _init;
        lock (_gate)
        {
            _init ??= Run();
            return _init;
        }

        async Task Run()
        {
            try { await InitializeCoreAsync(ct); }
            catch (Exception ex)
            {
                // JJQR - AI (Nov 21, 2026) - any init failure lands in node-errors.log next to the DB
                InitializationError = ex.Message;
                NodeErrorLog.Write(_options, "LocalDatabase.InitializeCoreAsync", ex);
                throw;
                // JJQR - END
            }
        }
    }

    private async Task InitializeCoreAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_options.DataDirectory);
        _ = NodeSecret.GetOrCreate(_options.NodeSecretPath);
        // A small pointer so we can confirm which file this node opened.
        await File.WriteAllTextAsync(Path.Combine(_options.DataDirectory, "database-path.txt"), _options.DatabasePath + Environment.NewLine, ct);

        await using var db = await _factory.CreateDbContextAsync(ct);
        await EnsureSchemaAsync(db, ct);
        // JJQR - AI (Nov 21, 2026) - existing files lack sync_changes; EF writes fail without it (DemoClinicDay / seed)
        ClinicApp.Infrastructure.Sync.SyncBootstrap.EnsureSqliteSyncSchema(db);
        // add missing EF columns (row_version/deleted_at/origin_node_id, etc.) on existing files
        await SchemaRepair.EnsureModelColumnsAsync(db, _logger, ct);
        // JJQR - END

        try { SqliteDemoViews.CreateViews(db); }
        catch (Exception ex) { _logger.LogWarning(ex, "SqliteDemoViews could not run; reports may be unavailable."); }

        // WAL makes a UI thread and a sync loop less likely to block each other.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;", ct);

        await SyncSchema.EnsureCreatedAsync(db, ct);
        await SyncOutbox.GetOrCreateIdentityAsync(db, ct);
        await NodePatientCodeAllocator.EnsureDefaultBlockAsync(db, ct: ct);

        try
        {
            await Icd10Seeder.SeedAsync(db, _loggerFactory.CreateLogger("Icd10Seeder"));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Icd10Seeder could not run."); }

        try
        {
            await MedicineCatalogSeeder.SeedAsync(db, _loggerFactory.CreateLogger("MedicineCatalogSeeder"));
        }
        catch (Exception ex) { _logger.LogWarning(ex, "MedicineCatalogSeeder could not run."); }

        await EnsureVitalTemplatesAsync(db, ct);
        await EnsureClinicSettingsAsync(db, ct);

        if (_options.Demo)
        {
            // Same accounts and password as Project02-fe/e2e/playwright.config.ts DEV_PASSWORD (DevDataSeeder.DevPassword). Never log it.
            // JJQR - AI (Nov 21, 2026) - mint visit IDs per node; well-known booking GUIDs would clash if two nodes sync
            await DevDataSeeder.SeedAsync(db, _hasher, _loggerFactory.CreateLogger("DevDataSeeder"), ct, stableClinicalIds: false);
            // JJQR - END
            if (!await db.Users.AnyAsync(u => u.Email == "doctor@clinic.test", ct))
                throw new InvalidOperationException("Demo seed ran but doctor@clinic.test is not in the users table.");
            await DemoClinicDay.EnsureAsync(db, ct);
            _logger.LogWarning("Clinic Node is running in DEMO mode. Do not use this on a real clinic computer.");
        }

        // Re-reserve after demo seed so the block starts after any seeded MF- codes.
        await NodePatientCodeAllocator.EnsureDefaultBlockAsync(db, ct: ct);

        _logger.LogInformation("Local database ready at {Path} (demo={Demo}).", _options.DatabasePath, _options.Demo);
    }

    /// <summary>
    /// EnsureCreated is a no-op when the SQLite *file* already exists, even if it only has
    /// sync tables or was created empty by a login that raced ahead of startup. Create the
    /// missing EF tables in place. A junk file with no clinic data is recreated.
    /// </summary>
    private async Task EnsureSchemaAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        await db.Database.EnsureCreatedAsync(ct);
        if (await HasTableAsync(db, "users", ct)) return;

        _logger.LogWarning("Local database at {Path} has no users table (stale or incomplete file). Creating schema in place.", _options.DatabasePath);
        try
        {
            var creator = db.GetService<IRelationalDatabaseCreator>();
            await creator.CreateTablesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CreateTables failed; recreating a junk database that has no clinic data.");
            if (await HasTableAsync(db, "patients", ct) || await HasTableAsync(db, "bookings", ct) || await HasTableAsync(db, "consultations", ct))
                throw new InvalidOperationException($"The local database at {_options.DatabasePath} is missing the users table and could not be repaired in place.", ex);

            await db.Database.CloseConnectionAsync();
            foreach (var extra in new[] { "", "-wal", "-shm" })
            {
                var path = _options.DatabasePath + extra;
                if (File.Exists(path)) File.Delete(path);
            }
            await db.Database.EnsureCreatedAsync(ct);
        }

        if (!await HasTableAsync(db, "users", ct))
            throw new InvalidOperationException($"The local database at {_options.DatabasePath} still has no users table after setup.");
    }

    internal static async Task<bool> HasTableAsync(ClinicAppDbContext db, string name, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1";
        var p = cmd.CreateParameter();
        p.ParameterName = "$name";
        p.Value = name;
        cmd.Parameters.Add(p);
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    private static async Task EnsureVitalTemplatesAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        async Task Ensure(string formKey, string description, string unit, string icon)
        {
            if (await db.VitalFieldTemplates.AnyAsync(t => t.FormKey == formKey, ct)) return;
            db.VitalFieldTemplates.Add(new VitalFieldTemplate
            {
                TemplateId = Guid.NewGuid(),
                FormKey = formKey,
                Description = description,
                Unit = unit,
                Icon = icon,
                IsDefault = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await Ensure("blood_pressure", "Blood Pressure", "mmHg", "monitor_heart");
        await Ensure("pulse_rate", "Heart Rate", "bpm", "cardiology");
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureClinicSettingsAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        if (await db.ClinicSettings.AnyAsync(ct)) return;
        db.ClinicSettings.Add(new ClinicSetting
        {
            Id = 1,
            ClinicName = "Grace Medical Clinic",
            Address = "",
            FeeConsultation = 450m,
            FeeFollowUp = 350m,
            FeeSeniorPwd = 400m,
            FeeMedCert = 50m,
            DiscountPct = 0.20m,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
// JJQR - END
