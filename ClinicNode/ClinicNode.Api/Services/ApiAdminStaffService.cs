//JJQR - AI (21 Nov 2026) - Api implementation of IAdminStaffService (web /admin/staff, app/actions/inviteStaffMember.ts, revokeStaffInvite.ts, createDoctor.ts):
//   GET /api/staff-accounts/search?q&role&page&pageSize&sort · GET /api/staff-accounts/{id} · PUT /api/staff-accounts/{id} (full row: GET + patch)
//   POST /api/auth/invite { email, fullName, role, doctor? } · revoke: DELETE /api/auth/users/{userId} (only while the account is still Invited)
// The server sets no password on invite (the invitee sets it from the link). A password typed on this computer is a local sign-in for this node only,
// so SetInvitedPasswordAsync always stays on the local copy.
using System.Text.Json;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminStaffService(IApiClient api) : IAdminStaffService
{
    private static StaffListItem Map(StaffAccount s) => new(s.StaffId, s.UserId, s.FullName, s.Email, s.ContactNumber, s.Role, s.Status, s.InvitedAt);

    public async Task<AdminPage<StaffListItem>> SearchAsync(string? q, string? role, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default)
    {
        var res = await api.GetAsync<ApiPaged<StaffAccount>>("/api/staff-accounts/search",
            ApiAdmin.Q(("q", q), ("role", role), ("page", page), ("pageSize", pageSize), ("sort", sort)), ct);
        var items = (res?.Items ?? []).Select(Map).ToList();
        return new AdminPage<StaffListItem>(items, res?.TotalCount ?? items.Count, res?.Page ?? page, res?.PageSize ?? pageSize);
    }

    public async Task<StaffAccount?> GetAsync(Guid staffId, CancellationToken ct = default)
    {
        try { return await api.GetAsync<StaffAccount>($"/api/staff-accounts/{staffId:D}", null, ct); }
        catch (ApiNotFoundException) { return null; }
    }

    public async Task<AdminResult<StaffAccount>> UpdateAsync(Guid staffId, string fullName, string? contactNumber, StaffStatus? status, CancellationToken ct = default)
    {
        try
        {
            var row = await api.GetAsync<StaffAccount>($"/api/staff-accounts/{staffId:D}", null, ct);
            if (row is null) return AdminResult<StaffAccount>.Fail("Staff account not found.");
            row.FullName = (fullName ?? row.FullName).Trim();
            row.ContactNumber = ApiAdmin.Blank(contactNumber);
            if (status is { } st) row.Status = st;
            await api.SendAsync(HttpMethod.Put, $"/api/staff-accounts/{staffId:D}", row, null, ct);
            var saved = await api.GetAsync<StaffAccount>($"/api/staff-accounts/{staffId:D}", null, ct) ?? row;
            return AdminResult<StaffAccount>.Ok(saved);
        }
        catch (ApiNotFoundException) { return AdminResult<StaffAccount>.Fail("Staff account not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<StaffAccount>.Fail(ex.Message); }
    }

    public async Task<AdminResult<StaffInviteResult>> InviteAsync(StaffInviteRequest request, CancellationToken ct = default)
    {
        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.FullName))
            return AdminResult<StaffInviteResult>.Fail("Email and full name are required.");

        // POST /api/auth/invite pins its own key names (fullName, consultationFee …), so the body is built with exact keys.
        var body = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["fullName"] = request.FullName.Trim(),
            ["role"] = request.Role,
        };
        if (request.Doctor is { } d && string.Equals(request.Role, "Doctor", StringComparison.OrdinalIgnoreCase))
        {
            body["doctor"] = new Dictionary<string, object?>
            {
                ["specialization"] = d.Specialization.Trim(),
                ["consultationFee"] = d.ConsultationFee,
                ["bio"] = ApiAdmin.Blank(d.Bio),
                ["licenseNumber"] = ApiAdmin.Blank(d.LicenseNumber),
                ["ptrNumber"] = ApiAdmin.Blank(d.PtrNumber),
                ["s2Number"] = ApiAdmin.Blank(d.S2Number),
                ["slotDurationMinutes"] = d.SlotDurationMinutes <= 0 ? 30 : d.SlotDurationMinutes,
                ["schedule"] = (d.Schedule ?? []).Select(s => new Dictionary<string, object?>
                {
                    ["dayOfWeek"] = s.DayOfWeek,
                    ["isActive"] = s.IsActive,
                    ["startTime"] = s.StartTime.ToString("HH:mm"),
                    ["endTime"] = s.EndTime.ToString("HH:mm"),
                }).ToList(),
            };
        }
        try
        {
            var answer = await api.SendAsync<JsonElement>(HttpMethod.Post, "/api/auth/invite", body, null, ct);
            var userId = ApiAdmin.ReadGuid(answer, "userId", "user_id");
            var staffId = ApiAdmin.ReadGuid(answer, "staffId", "staff_id");

            // The invite endpoint has no contact / capacity / daily-limit fields: set them right after, like DoctorForm does with its follow-up saves.
            if (staffId != Guid.Empty && !string.IsNullOrWhiteSpace(request.ContactNumber))
                await UpdateAsync(staffId, request.FullName, request.ContactNumber, null, ct);
            if (staffId != Guid.Empty && request.Doctor is { } dp && (dp.SlotCapacity > 1 || dp.DailyPatientLimit is not null))
            {
                var doctor = await api.GetAsync<Doctor>($"/api/doctors/{staffId:D}", null, ct);
                if (doctor is not null)
                {
                    doctor.SlotCapacity = dp.SlotCapacity <= 0 ? 1 : dp.SlotCapacity;
                    doctor.DailyPatientLimit = dp.DailyPatientLimit;
                    await api.SendAsync(HttpMethod.Put, $"/api/doctors/{staffId:D}", doctor, null, ct);
                }
            }
            return AdminResult<StaffInviteResult>.Ok(new StaffInviteResult(userId, staffId, PasswordSet: false));
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<StaffInviteResult>.Fail(ex.Message); }
    }

    public Task<AdminResult> SetInvitedPasswordAsync(Guid userId, string password, CancellationToken ct = default) =>
        Task.FromException<AdminResult>(new ApiNotSupportedException("A password typed here is a sign-in for this computer only."));

    public async Task<AdminResult> RevokeInviteAsync(Guid userId, CancellationToken ct = default)
    {
        try
        {
            var all = await api.GetAsync<List<StaffAccount>>("/api/staff-accounts", null, ct) ?? [];
            var staff = all.FirstOrDefault(s => s.UserId == userId);
            if (staff is null) return AdminResult.Fail("Staff account not found.");
            if (staff.Status != StaffStatus.Invited) return AdminResult.Fail("Only a pending invite can be revoked.");
            await api.SendAsync(HttpMethod.Delete, $"/api/auth/users/{userId:D}", null, null, ct);
            return AdminResult.Ok();
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult.Fail(ex.Message); }
    }
}
//JJQR - END
