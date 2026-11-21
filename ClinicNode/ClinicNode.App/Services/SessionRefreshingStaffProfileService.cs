// JJQR - AI (Nov 21, 2026) - after a staff profile name change, refresh IAuthService.Current.FullName (does not edit Core/Staff).
using ClinicNode.Core.Auth;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Services;

public sealed class SessionRefreshingStaffProfileService : IStaffProfileService
{
    private readonly IStaffProfileService _inner;
    private readonly IAuthService _auth;

    public SessionRefreshingStaffProfileService(IStaffProfileService inner, IAuthService auth)
    {
        _inner = inner;
        _auth = auth;
    }

    public Task<StaffProfile?> GetMineAsync(CancellationToken ct = default) => _inner.GetMineAsync(ct);

    public async Task<StaffResult> SaveAsync(string? fullName, string? contactNumber, CancellationToken ct = default)
    {
        var result = await _inner.SaveAsync(fullName, contactNumber, ct);
        if (result.Succeeded)
            await _auth.RefreshCurrentAsync(ct);
        return result;
    }

    public bool CanUpdatePassword(string? current, string? newPassword, string? confirm) =>
        _inner.CanUpdatePassword(current, newPassword, confirm);

    public Task<StaffResult> ChangePasswordAsync(string? current, string? newPassword, string? confirm, CancellationToken ct = default) =>
        _inner.ChangePasswordAsync(current, newPassword, confirm, ct);
}
// JJQR - END
