// JJQR - AI (Nov 21, 2026)
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Data;

/// <summary>
/// Transactional outbox. Call <see cref="EnqueueAsync"/> after mutating entities
/// but before / as part of <c>SaveChanges</c>, then <see cref="AttachToTransaction"/>
/// so the INSERT uses the same SQLite connection and transaction.
/// </summary>
public interface ISyncOutbox
{
    Task EnsureSchemaAsync(ClinicApp.Infrastructure.ClinicAppDbContext db, CancellationToken ct = default);

    /// <summary>Insert an outbox row on the same connection/transaction as <paramref name="db"/>.</summary>
    Task EnqueueAsync(ClinicApp.Infrastructure.ClinicAppDbContext db, SyncOutboxEntry entry, CancellationToken ct = default);

    Task<IReadOnlyList<SyncOutboxEntry>> ListPendingAsync(ClinicApp.Infrastructure.ClinicAppDbContext db, int take = 50, CancellationToken ct = default);

    Task MarkAckedAsync(ClinicApp.Infrastructure.ClinicAppDbContext db, Guid operationId, CancellationToken ct = default);

    Task MarkAttemptAsync(ClinicApp.Infrastructure.ClinicAppDbContext db, Guid operationId, string? error, CancellationToken ct = default);

    Task<int> CountPendingAsync(ClinicApp.Infrastructure.ClinicAppDbContext db, CancellationToken ct = default);
}
// JJQR - END
