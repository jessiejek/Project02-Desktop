//JJQR - AI (21 Nov 2026) - online-first sign-in. Wraps the local Core AuthService:
//   1. API login first (POST /api/auth/login). Success → tokens stored (refresh token in SecureStorage), session built from the API user (Doctor/Staff/Admin).
//   2. API says wrong password (401) or any other real answer (403 deactivated, 423/429 locked, 400 …) → that error is shown. NO local fallback.
//   3. Only when the API cannot be reached (ApiOfflineException: no endpoint, network error, timeout, 502/503/504) → local SQLite login.
// Session = 1 h access token + 7-day rolling refresh (PersistentApiTokenStore). Logout clears both. Only the EMAIL is remembered, never the password.
// Nothing here logs an email, password, token or patient data.
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Api.Http;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicNode.Api.Auth;

/// <summary>Extra surface of the online-first auth service for the login page, top bar and shell (cast <c>IAuthService</c> to this).</summary>
public interface IOnlineAuthSession
{
    /// <summary>True when the current session came from the clinic server (an API token exists); false for an offline (local) sign-in.</summary>
    bool IsOnlineSession { get; }
    /// <summary>Raised after sign-in, sign-out, resume or a session drop. Arrives on any thread.</summary>
    event Action? SessionChanged;
    /// <summary>App start: uses the refresh token saved in SecureStorage (≤ 7 days old) to sign back in without a password. Online only.</summary>
    Task<bool> TryResumeAsync(CancellationToken ct = default);
}

public sealed class OnlineFirstAuthService : IAuthService, IOnlineAuthSession
{
    public const string WrongPassword = "Invalid email or password.";
    public const string Deactivated = "This account has been deactivated. Please contact the clinic administrator.";
    public const string NoPatientPortal = "The patient portal is not available in the clinic node.";
    public const string Expired = "Your session has expired. Please sign in again.";
    public const string Revoked = "This computer was disconnected from the cloud by an administrator. Contact the clinic administrator.";
    public const string OfflineTooLong = "This computer has been offline too long. Connect to the internet so it can sync, then try again.";
    private static readonly TimeSpan ProfileRecheck = TimeSpan.FromMinutes(5);

    private readonly AuthService _local;
    private readonly IApiAuthClient _apiAuth;
    private readonly IApiClient _api;
    private readonly IApiTokenStore _tokens;
    private readonly IApiConnectivity _connectivity;
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncBridge _sync;
    private readonly ILogger<OnlineFirstAuthService> _logger;
    private readonly TimeProvider _clock;
    private NodeSession? _apiSession;
    private DateTimeOffset _profileCheckedAt;

    public OnlineFirstAuthService(
        AuthService local,
        IApiAuthClient apiAuth,
        IApiClient api,
        IApiTokenStore tokens,
        IApiConnectivity connectivity,
        IDbContextFactory<ClinicAppDbContext> factory,
        ILogger<OnlineFirstAuthService> logger,
        ISyncBridge? sync = null,
        TimeProvider? clock = null)
    {
        _local = local;
        _apiAuth = apiAuth;
        _api = api;
        _tokens = tokens;
        _connectivity = connectivity;
        _factory = factory;
        _logger = logger;
        _sync = sync ?? new NoOpSyncBridge();
        _clock = clock ?? TimeProvider.System;
    }

    public event Action? SessionChanged;

    public NodeSession? Current => _apiSession ?? _local.Current;
    public bool IsOnlineSession => _apiSession is not null && _tokens.RefreshToken is { Length: > 0 };
    public string? RememberedEmail => _local.RememberedEmail;

    public Task SetRememberedEmailAsync(string? email, CancellationToken ct = default) => _local.SetRememberedEmailAsync(email, ct);

    public async Task<bool> IsLockedForOfflinePeriodAsync(CancellationToken ct = default)
    {
        // Online sessions are only locked by a revoke; the 7-day offline lock is for local sign-ins.
        if (_apiSession is not null && _connectivity.IsOnline) return await _sync.IsRevokedAsync(ct);
        return await _local.IsLockedForOfflinePeriodAsync(ct);
    }

    public async Task<AuthResult> LoginAsync(string email, string password, bool rememberEmail, CancellationToken ct = default)
    {
        email = (email ?? "").Trim().ToLowerInvariant();
        if (email.Length == 0 || string.IsNullOrEmpty(password))
            return new AuthResult(false, null, "Enter your email and password.");
        if (await _sync.IsRevokedAsync(ct))
            return new AuthResult(false, null, Revoked, Locked: true);

        ApiSession api;
        try
        {
            api = await _apiAuth.LoginAsync(email, password, ct);
        }
        catch (ApiOfflineException)
        {
            // The ONLY local fallback: the clinic server could not be reached.
            _logger.LogInformation("Clinic server unreachable; signing in against the local node.");
            await DropApiSessionAsync(callServer: false);
            var local = await _local.LoginAsync(email, password, rememberEmail, ct);
            if (local.Succeeded) SessionChanged?.Invoke();
            return local;
        }
        catch (ApiUnauthorizedException ex)
        {
            return new AuthResult(false, null, Friendly(ex.Message, WrongPassword));
        }
        catch (ApiException ex)
        {
            var locked = ex.Status is 423 or 429;
            return new AuthResult(false, null, Friendly(ex.Message, ex.Status == 403 ? Deactivated : "Could not sign in. Please try again."), Locked: locked);
        }

        var (session, error) = await BuildSessionAsync(api.User, ct);
        if (session is null)
        {
            await DropApiSessionAsync(callServer: true);
            return new AuthResult(false, null, error);
        }

        await _local.LogoutAsync(ct); // never two sessions at once
        _apiSession = session;
        _profileCheckedAt = _clock.GetUtcNow();
        await _local.SetRememberedEmailAsync(rememberEmail ? email : null, ct);
        _logger.LogInformation("Signed in as {Role} via the clinic server.", session.Role);
        SessionChanged?.Invoke();
        return new AuthResult(true, session, null);
    }

    public async Task<bool> TryResumeAsync(CancellationToken ct = default)
    {
        if (Current is not null) return true;
        if (_tokens is not PersistentApiTokenStore store || !await store.RestoreAsync(ct)) return false;
        if (await _sync.IsRevokedAsync(ct)) { await DropApiSessionAsync(callServer: false); return false; }
        if (!await _connectivity.EnsureCheckedAsync(ct)) return false; // keep the saved token; resume next time we are online
        try
        {
            if (!await _apiAuth.TryRefreshAsync(ct)) return false; // refused → tokens already cleared
            var user = await _apiAuth.MeAsync(ct);
            if (user is null) { await DropApiSessionAsync(callServer: true); return false; }
            var (session, _) = await BuildSessionAsync(user, ct);
            if (session is null) { await DropApiSessionAsync(callServer: true); return false; }
            _apiSession = session;
            _profileCheckedAt = _clock.GetUtcNow();
            _logger.LogInformation("Resumed a {Role} session via the clinic server.", session.Role);
            SessionChanged?.Invoke();
            return true;
        }
        catch (ApiOfflineException) { return false; }
        catch (ApiException) { await DropApiSessionAsync(callServer: false); return false; }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await DropApiSessionAsync(callServer: true);
        await _local.LogoutAsync(ct);
        SessionChanged?.Invoke();
    }

    public async Task<string?> RefreshCurrentAsync(CancellationToken ct = default)
    {
        var session = _apiSession;
        if (session is null)
        {
            var before = _local.Current?.FullName;
            var message = await _local.RefreshCurrentAsync(ct);
            if (!string.Equals(before, _local.Current?.FullName, StringComparison.Ordinal)) SessionChanged?.Invoke(); // offline rename / sign-out
            return message;
        }

        var staff = await LookupStaffAsync(session.UserId, session.Email, preferApi: _connectivity.IsOnline, ct);
        if (staff?.Inactive == true)
        {
            await DropApiSessionAsync(callServer: true);
            SessionChanged?.Invoke();
            return Deactivated;
        }
        _profileCheckedAt = _clock.GetUtcNow();
        var name = string.IsNullOrWhiteSpace(staff?.FullName) ? session.FullName : staff!.FullName!;
        if (!string.Equals(name, session.FullName, StringComparison.Ordinal))
        {
            _apiSession = Copy(session, fullName: name);
            SessionChanged?.Invoke(); // top bar / shell pick up the new name at once (staff profile save)
        }
        return null;
    }

    public async Task<string?> ValidateCurrentAsync(CancellationToken ct = default)
    {
        var session = _apiSession;
        if (session is null) return await _local.ValidateCurrentAsync(ct);

        var now = _clock.GetUtcNow();
        if (session.IsExpired(now))
        {
            bool? refreshed;
            try { refreshed = await _apiAuth.TryRefreshAsync(ct); }
            catch (ApiOfflineException) { refreshed = null; }

            if (refreshed == false || _tokens.RefreshToken is not { Length: > 0 })
            {
                await DropApiSessionAsync(callServer: false);
                SessionChanged?.Invoke();
                return Expired;
            }
            if (refreshed == null && _tokens is PersistentApiTokenStore store && !store.HasLiveRefreshToken)
            {
                await DropApiSessionAsync(callServer: false);
                SessionChanged?.Invoke();
                return Expired;
            }
            // refreshed online, or offline inside the 7-day window: one more hour (screens fall back to local until we are back online)
            _apiSession = session = Copy(session, accessExpiresAt: now + PersistentApiTokenStore.AccessLifetime);
        }

        if (await IsLockedForOfflinePeriodAsync(ct))
        {
            var revoked = await _sync.IsRevokedAsync(ct);
            await DropApiSessionAsync(callServer: false);
            SessionChanged?.Invoke();
            return revoked ? Revoked : OfflineTooLong;
        }
        if (now - _profileCheckedAt < ProfileRecheck) return null; // the shell checks often; the profile does not change often
        return await RefreshCurrentAsync(ct);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private sealed record StaffInfo(Guid StaffId, string? FullName, bool Inactive);
    private sealed record StaffMeDto(Guid StaffId, string? FullName, StaffStatus? Status);

    private async Task<(NodeSession? Session, string? Error)> BuildSessionAsync(ApiUser user, CancellationToken ct)
    {
        if (!Enum.TryParse<UserRole>(user.Role, ignoreCase: true, out var role) || role is not (UserRole.Doctor or UserRole.Staff or UserRole.Admin))
            return (null, NoPatientPortal);

        var email = (user.Email ?? "").Trim().ToLowerInvariant();
        var staff = await LookupStaffAsync(user.Id, email, preferApi: true, ct);
        if (staff?.Inactive == true) return (null, Deactivated);

        var now = _clock.GetUtcNow();
        return (new NodeSession
        {
            UserId = user.Id,
            Email = email,
            FullName = !string.IsNullOrWhiteSpace(staff?.FullName) ? staff!.FullName! : !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : email,
            Role = role,
            StaffId = staff?.StaffId,
            DoctorId = role == UserRole.Doctor ? staff?.StaffId : null, // same rule as the local AuthService: Doctors.DoctorId == StaffAccounts.StaffId
            IssuedAt = now,
            AccessExpiresAt = now + PersistentApiTokenStore.AccessLifetime,
        }, null);
    }

    /// <summary>GET /api/staff-accounts/me when online, else (or when that fails) the local StaffAccounts row.</summary>
    private async Task<StaffInfo?> LookupStaffAsync(Guid userId, string email, bool preferApi, CancellationToken ct)
    {
        if (preferApi)
        {
            try
            {
                var me = await _api.GetAsync<StaffMeDto>("/api/staff-accounts/me", null, ct);
                if (me is not null && me.StaffId != Guid.Empty)
                    return new StaffInfo(me.StaffId, me.FullName, me.Status == StaffStatus.Inactive);
            }
            catch (ApiException) { /* offline or not found: use the local copy */ }
            catch (System.Text.Json.JsonException) { }
        }
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var row = await db.StaffAccounts.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, ct)
                      ?? await db.StaffAccounts.AsNoTracking().FirstOrDefaultAsync(s => s.Email == email, ct);
            return row is null ? null : new StaffInfo(row.StaffId, row.FullName, row.Status == StaffStatus.Inactive);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Local staff lookup failed: {Error}", ex.GetType().Name);
            return null;
        }
    }

    private async Task DropApiSessionAsync(bool callServer)
    {
        _apiSession = null;
        if (callServer)
        {
            try { await _apiAuth.LogoutAsync(); } // clears memory + SecureStorage first, then tells the server
            catch (Exception) { _tokens.Clear(); }
        }
        else _tokens.Clear();
    }

    private static NodeSession Copy(NodeSession s, string? fullName = null, DateTimeOffset? accessExpiresAt = null) => new()
    {
        UserId = s.UserId,
        Email = s.Email,
        FullName = fullName ?? s.FullName,
        Role = s.Role,
        StaffId = s.StaffId,
        DoctorId = s.DoctorId,
        IssuedAt = s.IssuedAt,
        AccessExpiresAt = accessExpiresAt ?? s.AccessExpiresAt,
    };

    private static string Friendly(string? serverMessage, string fallback) =>
        string.IsNullOrWhiteSpace(serverMessage) || serverMessage.StartsWith("Request failed", StringComparison.OrdinalIgnoreCase) ? fallback : serverMessage;
}
//JJQR - END
