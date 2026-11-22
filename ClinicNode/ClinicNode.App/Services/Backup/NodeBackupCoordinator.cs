//JJQR - AI (Nov 21, 2026) - glue for the Settings page: backup → temp file → user-picked folder; restore: pick → verify → confirm → swap → restart.
using ClinicNode.Core.NodeSettings;
using ClinicNode.Data;

namespace ClinicNode.App.Services.Backup;

public sealed class NodeBackupCoordinator
{
    readonly NodeOptions _options;
    readonly DatabaseEncryptionResult _encryption;
    readonly IBackupFileExchange _files;
    readonly string? _cacheDir, _appVersion, _machine;

    /// <summary>cacheDir / appVersion / machine default to the MAUI values; tests pass their own (MAUI Essentials is not available off-device).</summary>
    public NodeBackupCoordinator(NodeOptions options, DatabaseEncryptionResult encryption, IBackupFileExchange files,
        string? cacheDir = null, string? appVersion = null, string? machine = null)
    {
        _options = options;
        _encryption = encryption;
        _files = files;
        _cacheDir = cacheDir;
        _appVersion = appVersion;
        _machine = machine;
    }

    string CacheDir => _cacheDir ?? FileSystem.CacheDirectory;

    BackupService Service => new(_options.DatabasePath, _encryption);

    /// <summary>Returns where the backup was saved, or null if the user cancelled the picker.</summary>
    public async Task<string?> ExportAsync(string password, CancellationToken ct = default)
    {
        var name = BackupService.SuggestedFileName(DateTimeOffset.Now);
        var temp = Path.Combine(CacheDir, Guid.NewGuid().ToString("N") + BackupFormat.FileExtension);
        try
        {
            await using (var fs = File.Create(temp))
                await Task.Run(() => Service.CreateAsync(fs, password, _appVersion ?? AppInfo.Current.VersionString, _machine ?? ComputerName.Get(), ct: ct), ct);
            return await _files.ExportAsync(temp, name, ct);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); } // ciphertext only, plain delete is fine
    }

    /// <summary>Pick + verify. Null when cancelled. The caller confirms, then calls <see cref="RestoreAsync"/> or <see cref="BackupService.Discard"/>.</summary>
    public async Task<BackupVerification?> PickAndVerifyAsync(string password, CancellationToken ct = default)
    {
        var path = await _files.PickBackupAsync(ct);
        if (path is null) return null;
        try
        {
            await using var fs = File.OpenRead(path);
            return await Task.Run(() => Service.VerifyAsync(fs, password, ct), ct);
        }
        finally { File.Delete(path); }
    }

    public Task<RestoreResult> RestoreAsync(BackupVerification verified) => Task.Run(() => Service.Restore(verified));
}
//JJQR - END
