//JJQR - AI (Nov 21, 2026)
// Core-side seam to the sync client. ClinicNode.Sync references Core (not the other way round) and
// replaces the no-op default below, so Core never needs to see ISyncStatus / ISyncService.
namespace ClinicNode.Core.Sync;

public interface ISyncBridge
{
    /// <summary>Call after a local save commits so the background loop pushes straight away. Never throws, never blocks.</summary>
    void RequestSync();

    /// <summary>True when the cloud revoked this node (403 node_revoked): login must lock at once.</summary>
    Task<bool> IsRevokedAsync(CancellationToken ct = default);
}

public sealed class NoOpSyncBridge : ISyncBridge
{
    public void RequestSync() { }
    public Task<bool> IsRevokedAsync(CancellationToken ct = default) => Task.FromResult(false);
}
//JJQR - END
