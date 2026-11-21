// JJQR - AI (Nov 21, 2026)
namespace ClinicNode.Core.Auth;

public interface IAuthService
{
    NodeSession? Current { get; }

    string? RememberedEmail { get; }

    Task SetRememberedEmailAsync(string? email, CancellationToken ct = default);

    Task<AuthResult> LoginAsync(string email, string password, bool rememberEmail, CancellationToken ct = default);

    Task LogoutAsync(CancellationToken ct = default);

    /// <summary>True when the node has been offline longer than <c>NodeOptions.MaxOfflinePeriod</c>.</summary>
    Task<bool> IsLockedForOfflinePeriodAsync(CancellationToken ct = default);

    // JJQR - AI (Nov 21, 2026) - reload FullName after a profile name change; returns a logout message if the staff account is no longer active
    Task<string?> RefreshCurrentAsync(CancellationToken ct = default);

    /// <summary>Returns a logout message when the session has expired, the node is offline-locked, or the staff account is inactive; otherwise null.</summary>
    Task<string?> ValidateCurrentAsync(CancellationToken ct = default);
    // JJQR - END
}

public sealed record AuthResult(bool Succeeded, NodeSession? Session, string? Error, bool Locked = false);
// JJQR - END
