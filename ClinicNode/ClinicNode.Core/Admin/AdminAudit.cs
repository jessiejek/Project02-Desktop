// JJQR - AI (Nov 21, 2026) - local port of ClinicApp.Api.Auditing.AuditLogWriter + AccessLog (Api is not referenced).
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;

namespace ClinicNode.Core.Admin;

internal static class AdminAudit
{
    public static void Add(ClinicAppDbContext db, AuditEntityType entityType, Guid entityId, string action, Guid? performedBy, string? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Details = details,
            PerformedByUserId = performedBy,
            PerformedAt = DateTimeOffset.UtcNow,
        });
    }

    /// <summary>PV-9: name only the fields that changed, not their values.</summary>
    public static string? ChangedFields(params (string Field, object? OldValue, object? NewValue)[] fields)
    {
        var changed = fields.Where(f => !Equals(Normalize(f.OldValue), Normalize(f.NewValue))).Select(f => f.Field).ToList();
        return changed.Count == 0 ? null : "Changed: " + string.Join(", ", changed);
    }

    public static void RecordAccess(ClinicAppDbContext db, Guid? performedBy, Guid patientId, string area)
    {
        Add(db, AuditEntityType.Access, patientId, "Viewed", performedBy, area);
    }

    static object? Normalize(object? v) => v is string s && s.Length == 0 ? null : v;
}
// JJQR - END
