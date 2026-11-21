//JJQR - AI (Nov 21, 2026) - DB key in MAUI SecureStorage: Keychain on Mac Catalyst, DPAPI-protected app data on Windows.
using ClinicNode.Core.NodeSettings;

namespace ClinicNode.App.Services.Security;

public sealed class SecureStorageDatabaseKeyStore : IDatabaseKeyStore
{
    public const string KeyName = "clinicnode.db.sqlcipher_key";

    public Task<string?> GetAsync(CancellationToken ct = default) => SecureStorage.Default.GetAsync(KeyName);

    public Task SetAsync(string key, CancellationToken ct = default) => SecureStorage.Default.SetAsync(KeyName, key);
}
//JJQR - END
