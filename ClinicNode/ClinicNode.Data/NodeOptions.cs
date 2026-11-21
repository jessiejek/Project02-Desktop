// JJQR - AI (Nov 21, 2026) - local node configuration (data folder, demo seed, cloud URL).
namespace ClinicNode.Data;

/// <summary>Where the node keeps its SQLite file and whether demo accounts may be seeded.</summary>
public sealed class NodeOptions
{
    /// <summary>Folder that holds clinic-node.db, node-secret.key and future backups.</summary>
    public string DataDirectory { get; set; } = "";

    /// <summary>
    /// When true, <c>DevDataSeeder</c> and <c>DemoClinicDay</c> run.
    /// Defaults to on in DEBUG and off in Release; a real clinic install must leave this false.
    /// Clinic visit rows (patients, bookings, payments) still get fresh GUIDs per node so two
    /// demo nodes cannot consult the same visit and clash on sync.
    /// </summary>
    // JJQR - AI (Nov 21, 2026)
    public bool Demo { get; set; } =
#if DEBUG
        true;
#else
        false;
#endif
    // JJQR - END

    /// <summary>Cloud API origin used by the sync client (empty = sync stays local-only).</summary>
    public string CloudBaseUrl { get; set; } = "";

    /// <summary>Maximum time the node may stay without a successful cloud sync before login is locked.</summary>
    public TimeSpan MaxOfflinePeriod { get; set; } = TimeSpan.FromDays(7);

    public string DatabasePath => Path.Combine(DataDirectory, "clinic-node.db");

    public string NodeSecretPath => Path.Combine(DataDirectory, "node-secret.key");

    public string SqliteConnectionString => $"Data Source={DatabasePath}";
}
// JJQR - END
