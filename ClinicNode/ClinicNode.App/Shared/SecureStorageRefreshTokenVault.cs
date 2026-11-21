//JJQR - AI (21 Nov 2026) - refresh token in SecureStorage (macOS Keychain / Windows credential vault). Never the password, never the access token.
using System.Globalization;
using ClinicNode.Api.Auth;

namespace ClinicNode.App.Shared;

public sealed class SecureStorageRefreshTokenVault : IRefreshTokenVault
{
    const string TokenKey = "clinicnode.api.refresh_token";
    const string LastUsedKey = "clinicnode.api.refresh_last_used";

    //JJQR - AI (Nov 21, 2026) - DEBUG builds only: an unsigned / ad-hoc signed Mac build has no Keychain entitlement (MissingEntitlement), so the refresh token was never saved and every relaunch logged the user out.
    // It now falls back to an owner-only file in the app's data folder (7-day rolling refresh unchanged). RELEASE builds stay Keychain-only.
#if DEBUG
    static readonly string FallbackPath = Path.Combine(FileSystem.AppDataDirectory, "ClinicNode", "api-refresh-token.json");
    sealed record FileToken(string Token, string LastUsed);
#endif

    public async Task<StoredRefreshToken?> LoadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var token = await SecureStorage.Default.GetAsync(TokenKey);
            var used = await SecureStorage.Default.GetAsync(LastUsedKey);
            if (!string.IsNullOrWhiteSpace(token)
                && DateTimeOffset.TryParse(used, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var lastUsed))
                return new StoredRefreshToken(token, lastUsed);
        }
        catch (Exception) when (IsDebug) { /* no Keychain in this build: use the file */ }
#if DEBUG
        try
        {
            if (!File.Exists(FallbackPath)) return null;
            var f = System.Text.Json.JsonSerializer.Deserialize<FileToken>(await File.ReadAllTextAsync(FallbackPath, ct));
            if (f is null || string.IsNullOrWhiteSpace(f.Token)) return null;
            if (!DateTimeOffset.TryParse(f.LastUsed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)) return null;
            return new StoredRefreshToken(f.Token, when);
        }
        catch (Exception) { return null; }
#else
        return null;
#endif
    }

    public async Task SaveAsync(StoredRefreshToken token, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await SecureStorage.Default.SetAsync(TokenKey, token.RefreshToken);
            await SecureStorage.Default.SetAsync(LastUsedKey, token.LastUsedAt.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception) when (IsDebug)
        {
#if DEBUG
            Directory.CreateDirectory(Path.GetDirectoryName(FallbackPath)!);
            var tmp = FallbackPath + ".tmp";
            await File.WriteAllTextAsync(tmp, System.Text.Json.JsonSerializer.Serialize(new FileToken(token.RefreshToken, token.LastUsedAt.ToString("O", CultureInfo.InvariantCulture))), ct);
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tmp, FallbackPath, overwrite: true);
#endif
        }
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        try { SecureStorage.Default.Remove(TokenKey); SecureStorage.Default.Remove(LastUsedKey); } catch (Exception) { /* nothing there */ }
#if DEBUG
        try { if (File.Exists(FallbackPath)) File.Delete(FallbackPath); } catch (Exception) { }
#endif
        return Task.CompletedTask;
    }

#if DEBUG
    const bool IsDebug = true;
#else
    const bool IsDebug = false;
#endif
    //JJQR - END
}
//JJQR - END
