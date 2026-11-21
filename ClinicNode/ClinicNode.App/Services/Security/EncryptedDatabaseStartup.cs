//JJQR - AI (Nov 21, 2026) - one call from MauiProgram.CreateMauiApp (before Build) that prepares the encrypted DB and hooks EF to it.
// The work runs on the thread pool (SecureStorage + a possible one-time migration) and is awaited synchronously because
// CreateMauiApp is synchronous; nothing has opened the database yet at that point.
using ClinicNode.Core.NodeSettings;
using ClinicNode.Data;
using Microsoft.Extensions.Logging;

namespace ClinicNode.App.Services.Security;

public static class EncryptedDatabaseStartup
{
    /// <summary>
    /// Call AFTER AddClinicNodeAppServices. Encrypts a plaintext DB once (verified, original wiped), then registers
    /// <see cref="DatabaseEncryptionResult"/> and the keyed connection string for ClinicAppDbContext.
    /// A missing key for an already-encrypted DB throws <see cref="DatabaseKeyUnavailableException"/> (never silently resets data).
    /// </summary>
    public static DatabaseEncryptionResult AddEncryptedClinicDatabase(this IServiceCollection services, NodeOptions options,
        IDatabaseKeyStore? keyStore = null, ILogger? logger = null)
    {
        Directory.CreateDirectory(options.DataDirectory);
        var store = keyStore ?? new SecureStorageDatabaseKeyStore();
        var result = Task.Run(() => DatabaseEncryption.PrepareAsync(options.DatabasePath, store, logger)).GetAwaiter().GetResult();
        services.AddClinicNodeDatabaseEncryption(options.DatabasePath, result);
        return result;
    }
}
//JJQR - END
