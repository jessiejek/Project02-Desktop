//JJQR - AI (21 Nov 2026) - Api implementation of IAdminAuditLogService (web /admin/audit-logs): GET /api/audit-logs/search?q&entityType&page&pageSize,
// and the "who" column from GET /api/staff-accounts (user_id → full_name), exactly as the web page resolves it.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminAuditLogService(IApiClient api) : IAdminAuditLogService
{
    public async Task<AdminPage<AuditLogListItem>> SearchAsync(string? q, AuditEntityType? entityType, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var res = await api.GetAsync<ApiPaged<AuditLog>>("/api/audit-logs/search",
            ApiAdmin.Q(("q", q), ("entityType", entityType), ("page", page), ("pageSize", pageSize)), ct);
        var rows = res?.Items ?? [];
        Dictionary<Guid, string> names = new();
        if (rows.Any(r => r.PerformedByUserId is not null))
        {
            try
            {
                foreach (var s in await api.GetAsync<List<StaffAccount>>("/api/staff-accounts", null, ct) ?? [])
                    names[s.UserId] = s.FullName;
            }
            catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { /* names are a nicety: the log itself still shows */ }
        }
        var items = rows.Select(r => new AuditLogListItem(r.Id, r.PerformedAt, r.Action, r.EntityType, r.EntityId,
            r.PerformedByUserId is { } u && names.TryGetValue(u, out var who) ? who : r.PerformedByUserId?.ToString() ?? "Unknown", r.Details)).ToList();
        return new AdminPage<AuditLogListItem>(items, res?.TotalCount ?? items.Count, res?.Page ?? page, res?.PageSize ?? pageSize);
    }
}
//JJQR - END
