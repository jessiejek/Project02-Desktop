//JJQR - AI (21 Nov 2026) - where the refresh token lives between app runs. The App plugs in a SecureStorage (Keychain / Windows vault) version;
// tests and the plain host use the in-memory one. Only the refresh token and when it was last used are kept: never the password, never the access token.
namespace ClinicNode.Api.Auth;

public sealed record StoredRefreshToken(string RefreshToken, DateTimeOffset LastUsedAt);

public interface IRefreshTokenVault
{
    Task<StoredRefreshToken?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(StoredRefreshToken token, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

public sealed class InMemoryRefreshTokenVault : IRefreshTokenVault
{
    private StoredRefreshToken? _value;
    public Task<StoredRefreshToken?> LoadAsync(CancellationToken ct = default) => Task.FromResult(_value);
    public Task SaveAsync(StoredRefreshToken token, CancellationToken ct = default) { _value = token; return Task.CompletedTask; }
    public Task ClearAsync(CancellationToken ct = default) { _value = null; return Task.CompletedTask; }
}
//JJQR - END
