//JJQR - AI (21 Nov 2026) - item 5: POST /api/auth/forgot-password { email } (same as Project02-fe lib/auth/account.ts requestPasswordReset).
// Online only. The server answers 200 for every address, so the app never learns (or shows) whether an account exists.
using ClinicNode.Api.Http;

namespace ClinicNode.Api.Auth;

public interface IPasswordResetClient
{
    /// <summary>Throws ApiOfflineException when the clinic server cannot be reached ("needs internet").</summary>
    Task RequestResetAsync(string email, CancellationToken ct = default);
}

public sealed class PasswordResetClient(IApiClient api, IApiConnectivity connectivity) : IPasswordResetClient
{
    public const string NeedsInternet = "Resetting a password needs an internet connection. Connect this computer and try again.";

    public async Task RequestResetAsync(string email, CancellationToken ct = default)
    {
        if (!await connectivity.EnsureCheckedAsync(ct) && !await connectivity.CheckNowAsync(ct))
            throw new ApiOfflineException(NeedsInternet);
        try
        {
            // camelCase body ({ email }); ApiClient serialises with snake_case, which is identical for a one-word property
            await api.SendAsync(HttpMethod.Post, "/api/auth/forgot-password", new { email = email.Trim() }, null, ct);
        }
        catch (ApiOfflineException) { throw new ApiOfflineException(NeedsInternet); }
    }
}
//JJQR - END
