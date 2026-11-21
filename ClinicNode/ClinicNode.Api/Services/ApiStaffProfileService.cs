//JJQR - AI (21 Nov 2026) - Api implementation of IStaffProfileService, the same calls the web /staff/profile makes:
//   GET /api/staff-accounts/me · PUT /api/staff-accounts/{staffId} (the full row back with full_name / contact_number changed)
//   POST /api/auth/change-password { currentPassword, newPassword }   (camelCase auth DTO; the server checks the current password and PasswordPolicy)
// Passwords are only passed through to the server: never stored, logged or cached here.
using System.Text.Json.Nodes;
using ClinicApp.Auth;
using ClinicNode.Api.Http;
using ClinicNode.Core.Staff;
using static ClinicNode.Api.Services.ApiStaff;

namespace ClinicNode.Api.Services;

public sealed class ApiStaffProfileService(IApiClient api) : IStaffProfileService
{
    internal sealed record StaffAccountDto(Guid StaffId, string? FullName, string? ContactNumber, string? Email);

    public async Task<StaffProfile?> GetMineAsync(CancellationToken ct = default)
    {
        StaffAccountDto? s;
        try { s = await api.GetAsync<StaffAccountDto>("/api/staff-accounts/me", null, ct); }
        catch (ApiNotFoundException) { return null; }
        return s is null ? null : new StaffProfile(s.StaffId, s.FullName ?? "", s.ContactNumber ?? "", s.Email ?? "");
    }

    public async Task<StaffResult> SaveAsync(string? fullName, string? contactNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return StaffResult.Fail("Enter your full name.");
        if ((StaffRules.Length(fullName, 200, "Full name") ?? StaffRules.Phone(contactNumber)) is { } problem) return StaffResult.Fail(problem);
        JsonObject? row;
        try { row = await api.GetAsync<JsonObject>("/api/staff-accounts/me", null, ct); }
        catch (ApiNotFoundException) { return StaffResult.Fail("Your profile wasn’t saved. Please try again."); }
        if (row is null || !Guid.TryParse(row["staff_id"]?.ToString(), out var staffId)) return StaffResult.Fail("Your profile wasn’t saved. Please try again.");
        row["full_name"] = fullName;
        row["contact_number"] = string.IsNullOrWhiteSpace(contactNumber) ? null : contactNumber;
        return await Write(() => api.SendAsync(HttpMethod.Put, $"/api/staff-accounts/{staffId:D}", row, null, ct),
            staffId, "Your profile wasn’t saved. Please try again.");
    }

    public bool CanUpdatePassword(string? current, string? newPassword, string? confirm) =>
        !string.IsNullOrWhiteSpace(current) && (newPassword ?? "").Length >= PasswordPolicy.MinLength && newPassword == confirm;

    public Task<StaffResult> ChangePasswordAsync(string? current, string? newPassword, string? confirm, CancellationToken ct = default)
    {
        if (newPassword != confirm) return Task.FromResult(StaffResult.Fail("Passwords don’t match yet."));
        if (PasswordPolicy.Check(newPassword) is { } weak) return Task.FromResult(StaffResult.Fail(weak));
        if (string.IsNullOrEmpty(current)) return Task.FromResult(StaffResult.Fail("Current password is incorrect."));
        // Dictionary keys are sent verbatim (the auth DTO is camelCase, unlike the snake_case data resources).
        var body = new Dictionary<string, string?> { ["currentPassword"] = current, ["newPassword"] = newPassword };
        return Write(() => api.SendAsync(HttpMethod.Post, "/api/auth/change-password", body, null, ct), null, "Could not update the password.");
    }
}
//JJQR - END
