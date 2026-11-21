//JJQR - AI (21 Nov 2026) - Api implementation of IAdminAnnouncementService (web /admin/announcements; lib/data/admin.ts):
//   GET /api/announcements?activeOnly · POST /api/announcements { title, body, is_active } · PUT /api/announcements/{id} (current row + patch) · DELETE /api/announcements/{id}
using ClinicApp.Domain.Entities;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminAnnouncementService(IApiClient api) : IAdminAnnouncementService
{
    public async Task<IReadOnlyList<Announcement>> ListAsync(bool activeOnly = false, CancellationToken ct = default) =>
        (await api.GetAsync<List<Announcement>>("/api/announcements", ApiAdmin.Q(("activeOnly", activeOnly)), ct) ?? [])
            .OrderByDescending(a => a.CreatedAt).ToList();

    private static string? Check(AnnouncementEdit e)
    {
        if (string.IsNullOrWhiteSpace(e.Title)) return "Title is required.";
        if (string.IsNullOrWhiteSpace(e.Body)) return "Message is required.";
        return null;
    }

    public async Task<AdminResult<Announcement>> CreateAsync(AnnouncementEdit edit, CancellationToken ct = default)
    {
        if (Check(edit) is { } bad) return AdminResult<Announcement>.Fail(bad);
        try
        {
            var row = await api.SendAsync<Announcement>(HttpMethod.Post, "/api/announcements",
                new { title = edit.Title.Trim(), body = edit.Body.Trim(), is_active = edit.IsActive }, null, ct);
            return row is null ? AdminResult<Announcement>.Fail("The announcement could not be read back.") : AdminResult<Announcement>.Ok(row);
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<Announcement>.Fail(ex.Message); }
    }

    public async Task<AdminResult<Announcement>> UpdateAsync(Guid id, AnnouncementEdit edit, CancellationToken ct = default)
    {
        if (Check(edit) is { } bad) return AdminResult<Announcement>.Fail(bad);
        try
        {
            var cur = (await ListAsync(false, ct)).FirstOrDefault(a => a.Id == id);
            if (cur is null) return AdminResult<Announcement>.Fail("Announcement not found.");
            cur.Title = edit.Title.Trim();
            cur.Body = edit.Body.Trim();
            cur.IsActive = edit.IsActive;
            await api.SendAsync(HttpMethod.Put, $"/api/announcements/{id:D}", cur, null, ct);
            return AdminResult<Announcement>.Ok(cur);
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<Announcement>.Fail(ex.Message); }
    }

    public async Task<AdminResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            await api.SendAsync(HttpMethod.Delete, $"/api/announcements/{id:D}", null, null, ct);
            return AdminResult.Ok();
        }
        catch (ApiNotFoundException) { return AdminResult.Fail("Announcement not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult.Fail(ex.Message); }
    }
}
//JJQR - END
