//JJQR - AI (21 Nov 2026) - admin management of sync computers (nodes): GET /api/sync/nodes, POST /api/sync/nodes/{id}/rotate-secret, POST /api/sync/nodes/{id}/revoke.
// The node registry lives on the clinic server only; the local fallback can show this computer and nothing else, and never changes anything.
using ClinicApp.Infrastructure;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Admin;

public sealed record SyncComputerItem(
    Guid NodeId,
    string Name,
    string? Platform,
    string? AppVersion,
    string Status,
    DateTimeOffset? RegisteredAt,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? RevokedAt,
    bool IsThisComputer,
    //JJQR - AI (21 Nov 2026) - GET /api/sync/nodes: last_push_at, operation_count_24h, rejected_count_24h (null when offline / not reported)
    DateTimeOffset? LastPushAt = null,
    int? OperationCount24h = null,
    int? RejectedCount24h = null);
    //JJQR - END

/// <summary>A freshly rotated node secret. Shown once; never logged or stored by the admin screen.</summary>
public sealed record SyncComputerSecret(Guid NodeId, string NodeSecret);

/// <summary><paramref name="FromServer"/> is false when the list could only be built from this computer (offline).</summary>
public sealed record SyncComputerList(IReadOnlyList<SyncComputerItem> Items, Guid ThisComputerId, bool FromServer, DateTimeOffset? LastSuccessfulSyncAt);

public interface IAdminNodeService
{
    Task<SyncComputerList> ListAsync(CancellationToken ct = default);
    Task<AdminResult<SyncComputerSecret>> RotateSecretAsync(Guid nodeId, CancellationToken ct = default);
    Task<AdminResult> RevokeAsync(Guid nodeId, CancellationToken ct = default);
}

/// <summary>Offline side of <see cref="IAdminNodeService"/>: this computer only, read-only.</summary>
public sealed class LocalAdminNodeService : IAdminNodeService
{
    public const string NeedsServer = "Managing sync computers needs a connection to the clinic server and an online sign-in.";
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;

    public LocalAdminNodeService(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    public static async Task<(Guid NodeId, DateTimeOffset? LastSync)> ThisComputerAsync(IDbContextFactory<ClinicAppDbContext> factory, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var identity = await SyncOutbox.GetOrCreateIdentityAsync(db, ct);
        return (identity.NodeId, identity.LastSuccessfulSyncAt);
    }

    public async Task<SyncComputerList> ListAsync(CancellationToken ct = default)
    {
        var (id, lastSync) = await ThisComputerAsync(_factory, ct);
        var name = string.IsNullOrWhiteSpace(Environment.MachineName) ? "This computer" : Environment.MachineName;
        var self = new SyncComputerItem(id, name, null, null, "Unknown", null, lastSync, null, true);
        return new SyncComputerList([self], id, false, lastSync);
    }

    public Task<AdminResult<SyncComputerSecret>> RotateSecretAsync(Guid nodeId, CancellationToken ct = default) =>
        Task.FromResult(AdminResult<SyncComputerSecret>.Fail(NeedsServer));

    public Task<AdminResult> RevokeAsync(Guid nodeId, CancellationToken ct = default) =>
        Task.FromResult(AdminResult.Fail(NeedsServer));
}
//JJQR - END
