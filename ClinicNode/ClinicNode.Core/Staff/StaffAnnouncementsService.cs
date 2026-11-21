//JJQR - AI (Nov 21, 2026) - "Clinic announcements" (view-only for staff): active rows newest first; poster name from staff accounts, else "Clinic admin".
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Staff;

public sealed record AnnouncementRow(Guid Id, string Title, string Body, DateTimeOffset PostedDate, string PostedBy);

public interface IStaffAnnouncementsService
{
    Task<IReadOnlyList<AnnouncementRow>> ListActiveAsync(CancellationToken ct = default);
}

public sealed class StaffAnnouncementsService : IStaffAnnouncementsService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;

    public StaffAnnouncementsService(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<AnnouncementRow>> ListActiveAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.Announcements.AsNoTracking().Where(a => a.IsActive).ToListAsync(ct);
        var posters = rows.Where(a => a.PostedByUserId != null).Select(a => a.PostedByUserId!.Value).Distinct().ToList();
        var names = await db.StaffAccounts.AsNoTracking().Where(s => posters.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, s => s.FullName, ct);
        return rows.OrderByDescending(a => a.CreatedAt)
            .Select(a => new AnnouncementRow(a.Id, a.Title, a.Body, a.CreatedAt,
                a.PostedByUserId is { } u && names.TryGetValue(u, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "Clinic admin"))
            .ToList();
    }
}
//JJQR - END
