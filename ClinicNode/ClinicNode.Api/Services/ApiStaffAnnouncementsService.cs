//JJQR - AI (21 Nov 2026) - Api implementation of IStaffAnnouncementsService, the same calls the web /staff/announcements makes:
//   GET /api/announcements?activeOnly=true (newest first) · GET /api/staff-accounts (poster names; optional — "Clinic admin" when unavailable)
using ClinicNode.Api.Http;
using ClinicNode.Core.Staff;
using static ClinicNode.Api.Services.ApiStaff;

namespace ClinicNode.Api.Services;

public sealed class ApiStaffAnnouncementsService(IApiClient api) : IStaffAnnouncementsService
{
    internal sealed record AnnouncementDto(Guid Id, string? Title, string? Body, bool IsActive, Guid? PostedByUserId, DateTimeOffset CreatedAt);
    internal sealed record StaffAccountDto(Guid? UserId, string? FullName);

    public async Task<IReadOnlyList<AnnouncementRow>> ListActiveAsync(CancellationToken ct = default)
    {
        var rows = await api.GetAsync<List<AnnouncementDto>>("/api/announcements", Q(("activeOnly", "true")), ct) ?? [];
        var staff = await Optional(() => api.GetAsync<List<StaffAccountDto>>("/api/staff-accounts", null, ct), null) ?? [];
        var names = staff.Where(s => s.UserId is not null && !string.IsNullOrWhiteSpace(s.FullName))
            .GroupBy(s => s.UserId!.Value).ToDictionary(g => g.Key, g => g.First().FullName!);
        return rows.Where(a => a.IsActive).OrderByDescending(a => a.CreatedAt)
            .Select(a => new AnnouncementRow(a.Id, a.Title ?? "", a.Body ?? "", a.CreatedAt,
                a.PostedByUserId is { } u && names.TryGetValue(u, out var n) ? n : "Clinic admin"))
            .ToList();
    }
}
//JJQR - END
