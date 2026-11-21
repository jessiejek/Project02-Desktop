// JJQR - AI (Nov 21, 2026) - ported from AuditLogsController (search + default exclusion of Access/Auth).
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Admin;

public sealed record AuditLogListItem(
    Guid Id,
    DateTimeOffset PerformedAt,
    string Action,
    AuditEntityType EntityType,
    Guid EntityId,
    string PerformedBy,
    string? Details);

public interface IAdminAuditLogService
{
    Task<AdminPage<AuditLogListItem>> SearchAsync(string? q, AuditEntityType? entityType, int page = 1, int pageSize = 25, CancellationToken ct = default);
}

public sealed class AdminAuditLogService : IAdminAuditLogService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;

    public AdminAuditLogService(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    public async Task<AdminPage<AuditLogListItem>> SearchAsync(string? q, AuditEntityType? entityType, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (entityType is not null) query = query.Where(a => a.EntityType == entityType);
        else query = query.Where(a => a.EntityType != AuditEntityType.Access && a.EntityType != AuditEntityType.Auth);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            if (Guid.TryParse(s, out var rid))
                query = query.Where(a => a.EntityId == rid || a.Action.Contains(s) || (a.Details != null && a.Details.Contains(s)));
            else
                query = query.Where(a => a.Action.Contains(s) || (a.Details != null && a.Details.Contains(s)));
        }

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.PerformedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(r => r.PerformedByUserId).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        var staff = await db.StaffAccounts.AsNoTracking().Where(s => ids.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, ct);
        var items = rows.Select(r =>
        {
            //JJQR - AI (21 Nov 2026) - web "By": the name, else the raw user id, else "Unknown"
            var who = r.PerformedByUserId is { } u && staff.TryGetValue(u, out var st) ? st.FullName : r.PerformedByUserId?.ToString() ?? "Unknown";
            //JJQR - END
            return new AuditLogListItem(r.Id, r.PerformedAt, r.Action, r.EntityType, r.EntityId, who, r.Details);
        }).ToList();
        return new AdminPage<AuditLogListItem>(items, total, page, pageSize);
    }
}
// JJQR - END
