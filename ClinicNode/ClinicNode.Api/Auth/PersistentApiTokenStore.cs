//JJQR - AI (21 Nov 2026) - IApiTokenStore that keeps the 1 h access token in memory and mirrors the refresh token to IRefreshTokenVault
// (SecureStorage in the App). 7-day ROLLING refresh: every login / successful refresh stamps LastUsedAt; a refresh token unused for
// more than RefreshWindow is treated as expired and wiped. Clear() (logout, refused refresh) wipes memory AND the vault.
using ClinicNode.Api.Http;

namespace ClinicNode.Api.Auth;

public sealed class PersistentApiTokenStore : IApiTokenStore
{
    public static readonly TimeSpan AccessLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshWindow = TimeSpan.FromDays(7);

    private readonly IRefreshTokenVault _vault;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private string? _access, _refresh;
    private DateTimeOffset? _lastUsedAt;

    public PersistentApiTokenStore(IRefreshTokenVault vault, TimeProvider? clock = null)
    {
        _vault = vault;
        _clock = clock ?? TimeProvider.System;
    }

    public string? AccessToken { get { lock (_gate) return _access; } }
    public string? RefreshToken { get { lock (_gate) return _refresh; } }
    public DateTimeOffset? LastUsedAt { get { lock (_gate) return _lastUsedAt; } }

    /// <summary>True while a refresh token exists and was used within the last 7 days.</summary>
    public bool HasLiveRefreshToken
    {
        get { lock (_gate) return _refresh is { Length: > 0 } && _lastUsedAt is { } t && _clock.GetUtcNow() - t <= RefreshWindow; }
    }

    public void Set(string accessToken, string? refreshToken)
    {
        StoredRefreshToken? toSave;
        lock (_gate)
        {
            _access = accessToken;
            _refresh = refreshToken ?? _refresh;
            _lastUsedAt = _clock.GetUtcNow(); // rolling window: each login / refresh pushes the 7 days forward
            toSave = _refresh is { Length: > 0 } r ? new StoredRefreshToken(r, _lastUsedAt.Value) : null;
        }
        if (toSave is not null) Fire(_vault.SaveAsync(toSave));
    }

    public void Clear()
    {
        lock (_gate) { _access = null; _refresh = null; _lastUsedAt = null; }
        Fire(_vault.ClearAsync());
    }

    /// <summary>Loads the refresh token saved by an earlier run (no access token: call TryRefreshAsync next). false when none or older than 7 days.</summary>
    public async Task<bool> RestoreAsync(CancellationToken ct = default)
    {
        StoredRefreshToken? saved;
        try { saved = await _vault.LoadAsync(ct); }
        catch (Exception) { saved = null; } // a broken keychain entry = signed out, never a crash
        if (saved is null || string.IsNullOrEmpty(saved.RefreshToken)) return false;
        if (_clock.GetUtcNow() - saved.LastUsedAt > RefreshWindow)
        {
            Clear();
            return false;
        }
        lock (_gate)
        {
            if (_refresh is { Length: > 0 }) return true; // already signed in during this run
            _access = null;
            _refresh = saved.RefreshToken;
            _lastUsedAt = saved.LastUsedAt;
        }
        return true;
    }

    private static void Fire(Task t) => _ = t.ContinueWith(static x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted);
}
//JJQR - END
