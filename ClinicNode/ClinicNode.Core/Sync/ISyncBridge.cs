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

    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.4: why a change may NOT be saved on this computer (null = it may). A computer that is not registered with the clinic server
    // (or was revoked / needs an update) could never send what it saves, so the save is refused instead of quietly piling up. Default = allowed (fakes, stand-alone demo).
    Task<string?> LocalSaveBlockedReasonAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    //JJQR - END
}

//JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.4: thrown instead of saving locally on a computer that cannot sync
public sealed class LocalWriteBlockedException(string message) : InvalidOperationException(message);
//JJQR - END

//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.2 (rule R1): in ONLINE mode a save that cannot reach the clinic server is NOT kept on this computer. It throws this, the shell offers "Switch to Offline mode".
public sealed class OnlineWriteFailedException(string message) : InvalidOperationException(message);
//JJQR - END

public sealed class NoOpSyncBridge : ISyncBridge
{
    public void RequestSync() { }
    public Task<bool> IsRevokedAsync(CancellationToken ct = default) => Task.FromResult(false);
}
//JJQR - END
