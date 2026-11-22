// JJQR - AI (Nov 21, 2026) - offline login against local password hashes; node-issued sessions.
using ClinicApp.Auth;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicNode.Core.Auth;

public sealed class AuthService : IAuthService
{
    public const int AccessTokenMinutes = 60;

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly PasswordHasherService _hasher;
    private readonly IEmailMemory _emailMemory;
    private readonly NodeOptions _options;
    private readonly ILogger<AuthService> _logger;
    private readonly TimeProvider _clock;
    private string? _rememberedEmail;
    //JJQR - AI (Nov 21, 2026)
    private readonly ClinicNode.Core.Sync.ISyncBridge _sync;
    //JJQR - END

    public AuthService(
        IDbContextFactory<ClinicAppDbContext> factory,
        PasswordHasherService hasher,
        IEmailMemory emailMemory,
        NodeOptions options,
        ILogger<AuthService> logger,
        //JJQR - AI (Nov 21, 2026)
        ClinicNode.Core.Sync.ISyncBridge? sync = null,
        TimeProvider? clock = null)
        //JJQR - END
    {
        //JJQR - AI (Nov 21, 2026)
        _sync = sync ?? new ClinicNode.Core.Sync.NoOpSyncBridge();
        _clock = clock ?? TimeProvider.System;
        //JJQR - END
        _factory = factory;
        _hasher = hasher;
        _emailMemory = emailMemory;
        _options = options;
        _logger = logger;
    }

    public NodeSession? Current { get; private set; }

    public string? RememberedEmail => _rememberedEmail;

    public async Task LoadRememberedEmailAsync(CancellationToken ct = default) =>
        _rememberedEmail = await _emailMemory.GetAsync(ct);

    public Task SetRememberedEmailAsync(string? email, CancellationToken ct = default)
    {
        _rememberedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        return _emailMemory.SetAsync(_rememberedEmail, ct);
    }

    public async Task<bool> IsLockedForOfflinePeriodAsync(CancellationToken ct = default)
    {
        //JJQR - AI (Nov 21, 2026) - item 7: a node the cloud revoked is locked at once, no grace period
        if (await _sync.IsRevokedAsync(ct)) return true;
        //JJQR - END
        await using var db = await _factory.CreateDbContextAsync(ct);
        var identity = await SyncOutbox.GetOrCreateIdentityAsync(db, ct);
        if (identity.LastSuccessfulSyncAt is null)
        {
            // A brand-new demo node has never synced; do not lock it.
            return false;
        }
        return _clock.GetUtcNow() - identity.LastSuccessfulSyncAt.Value > _options.MaxOfflinePeriod;
    }

    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 2.6 (D9): the lock message names the dates. Offline sign-in works for 7 days after the last successful sync; day 8 is locked until it syncs.
    public async Task<string> OfflineLockedMessageAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var identity = await SyncOutbox.GetOrCreateIdentityAsync(db, ct);
        const string Fallback = "This computer has been offline too long. Connect to the clinic server and sign in once so it can sync, then try again.";
        if (identity.LastSuccessfulSyncAt is not { } last) return Fallback;
        string Day(DateTimeOffset d) => d.ToOffset(ClinicApp.Domain.ClinicClock.Offset).ToString("MMM d");
        var days = (int)_options.MaxOfflinePeriod.TotalDays;
        return $"This computer last synced on {Day(last)}. It can be used offline for {days} days, so it had to sync by {Day(last + _options.MaxOfflinePeriod)}. Connect to the clinic server and sign in once so it can sync, then try again.";
    }
    //JJQR - END

    public async Task<AuthResult> LoginAsync(string email, string password, bool rememberEmail, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();
        if (await IsLockedForOfflinePeriodAsync(ct))
        {
            //JJQR - AI (Nov 21, 2026)
            if (await _sync.IsRevokedAsync(ct))
                return new AuthResult(false, null, "This computer was disconnected from the cloud by an administrator. Contact the clinic administrator.", Locked: true);
            //JJQR - END
            return new AuthResult(false, null, await OfflineLockedMessageAsync(ct), Locked: true); //JJQR - AI (Nov 22, 2026) - step 2.6 //JJQR - END
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct);
        var now = _clock.GetUtcNow();
        if (user is not null && user.LockedUntil is { } until && until > now)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((until - now).TotalMinutes));
            return new AuthResult(false, null, $"Too many failed sign-in attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.", Locked: true);
        }

        if (user is null || !_hasher.Verify(user, user.PasswordHash, password))
        {
            if (user is not null)
            {
                user.FailedLoginCount++;
                var locked = user.FailedLoginCount >= PasswordPolicy.MaxFailedLogins;
                if (locked)
                {
                    user.LockedUntil = now + PasswordPolicy.LockoutFor;
                    user.FailedLoginCount = 0;
                }
                await db.SaveChangesAsync(ct);
            }
            return new AuthResult(false, null, "Invalid email or password.");
        }

        var staff = await db.StaffAccounts.SingleOrDefaultAsync(s => s.UserId == user.Id, ct);
        if (staff is not null && staff.Status == StaffStatus.Inactive)
            return new AuthResult(false, null, "This account has been deactivated. Please contact the clinic administrator.");

        var profile = await db.Profiles.SingleOrDefaultAsync(p => p.Id == user.Id, ct);
        var role = profile?.Role ?? staff?.Role switch
        {
            StaffRole.Admin => UserRole.Admin,
            StaffRole.Doctor => UserRole.Doctor,
            StaffRole.Staff => UserRole.Staff,
            _ => UserRole.Patient,
        };
        if (role == UserRole.Patient)
            return new AuthResult(false, null, "The patient portal is not available in the clinic node.");

        if (user.FailedLoginCount != 0 || user.LockedUntil is not null)
        {
            user.FailedLoginCount = 0;
            user.LockedUntil = null;
            await db.SaveChangesAsync(ct);
        }

        Guid? doctorId = null;
        if (staff is not null && role == UserRole.Doctor)
            doctorId = staff.StaffId;

        Current = new NodeSession
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = staff?.FullName ?? user.Email,
            Role = role,
            StaffId = staff?.StaffId,
            DoctorId = doctorId,
            IssuedAt = now,
            AccessExpiresAt = now.AddMinutes(AccessTokenMinutes),
        };

        await SetRememberedEmailAsync(rememberEmail ? email : null, ct);
        //JJQR - AI (21 Nov 2026) - no email (personal data) in the log line
        _logger.LogInformation("Signed in as {Role} on the local node.", role);
        //JJQR - END
        return new AuthResult(true, Current, null);
    }

    public Task LogoutAsync(CancellationToken ct = default)
    {
        Current = null;
        return Task.CompletedTask;
    }

    // JJQR - AI (Nov 21, 2026) - refresh the signed-in display name from StaffAccounts after a profile save; re-check staff status
    public async Task<string?> RefreshCurrentAsync(CancellationToken ct = default)
    {
        var session = Current;
        if (session is null) return null;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var staff = await db.StaffAccounts.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == session.UserId, ct);
        if (staff is not null && staff.Status == StaffStatus.Inactive)
        {
            Current = null;
            return "This account has been deactivated. Please contact the clinic administrator.";
        }
        var name = string.IsNullOrWhiteSpace(staff?.FullName) ? session.Email : staff!.FullName;
        if (string.Equals(name, session.FullName, StringComparison.Ordinal)) return null;
        Current = new NodeSession
        {
            UserId = session.UserId,
            Email = session.Email,
            FullName = name,
            Role = session.Role,
            StaffId = session.StaffId,
            DoctorId = session.DoctorId,
            IssuedAt = session.IssuedAt,
            AccessExpiresAt = session.AccessExpiresAt,
        };
        return null;
    }

    public async Task<string?> ValidateCurrentAsync(CancellationToken ct = default)
    {
        var session = Current;
        if (session is null) return null;
        if (session.IsExpired(_clock.GetUtcNow()))
        {
            Current = null;
            return "Your session has expired. Please sign in again.";
        }
        if (await IsLockedForOfflinePeriodAsync(ct))
        {
            Current = null;
            if (await _sync.IsRevokedAsync(ct))
                return "This computer was disconnected from the cloud by an administrator. Contact the clinic administrator.";
            return await OfflineLockedMessageAsync(ct); //JJQR - AI (Nov 22, 2026) - step 2.6 //JJQR - END
        }
        return await RefreshCurrentAsync(ct);
    }
    // JJQR - END
}
// JJQR - END
