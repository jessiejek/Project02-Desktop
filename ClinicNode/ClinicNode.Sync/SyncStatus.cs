// JJQR - AI (Nov 21, 2026) - pending-sync indicator for the shell.
namespace ClinicNode.Sync;

public enum SyncPhase
{
    Idle,
    Syncing,
    Offline,
    Error,
    //JJQR - AI (Nov 21, 2026) - allen2: states from SYNC-API-CONTRACT §1/§2
    /// <summary>Cloud URL set, but no node secret yet (admin must register this computer).</summary>
    NotRegistered,
    /// <summary>The cloud revoked this node (403 node_revoked).</summary>
    Revoked,
    /// <summary>426 protocol_unsupported: update the app before it can sync.</summary>
    NeedsUpdate,
    //JJQR - END
}

public sealed class SyncStatus
{
    public SyncPhase Phase { get; init; } = SyncPhase.Idle;
    public int PendingCount { get; init; }
    public DateTimeOffset? LastSuccessfulSyncAt { get; init; }
    public string? LastError { get; init; }
    //JJQR - AI (Nov 21, 2026) - allen2
    /// <summary>Open items on the sync review list (conflicts, rejected ops, replaced patient codes).</summary>
    public int ConflictCount { get; init; }
    /// <summary>Last connectivity verdict (health probe or last request). False until first contact.</summary>
    public bool IsOnline { get; init; }
    //JJQR - END
    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.5: changes that keep being refused by the server (10+ tries). Any of them means "Sync problem".
    public int StuckCount { get; init; }
    /// <summary>The badge word set: Sync problem = the sync broke (error, revoked, update needed) or something is stuck.</summary>
    public bool HasSyncProblem => Phase is SyncPhase.Error or SyncPhase.Revoked or SyncPhase.NeedsUpdate || StuckCount > 0;
    //JJQR - END

    public string Banner
    {
        get
        {
            //JJQR - AI (Nov 22, 2026) - step 0.5: something is stuck (the sync itself is working, but this change will not go through)
            if (StuckCount > 0 && Phase is not (SyncPhase.Revoked or SyncPhase.NeedsUpdate or SyncPhase.Error))
                return $"Sync problem — {StuckCount} change{(StuckCount == 1 ? "" : "s")} could not be sent after several tries";
            //JJQR - END
            if (Phase == SyncPhase.Syncing) return PendingCount > 0 ? $"Syncing… ({PendingCount} pending)" : "Syncing…";
            if (Phase == SyncPhase.Offline) return PendingCount > 0 ? $"Offline — {PendingCount} waiting to sync" : "Offline";
            //JJQR - AI (Nov 21, 2026) - allen2
            if (Phase == SyncPhase.NotRegistered) return PendingCount > 0 ? $"Not registered for cloud sync — {PendingCount} change(s) pending sync" : "Not registered for cloud sync";
            if (Phase == SyncPhase.Revoked) return "This computer was disconnected from the cloud by an admin";
            if (Phase == SyncPhase.NeedsUpdate) return "Update the app to resume cloud sync";
            //JJQR - END
            if (Phase == SyncPhase.Error) return LastError is { Length: > 0 } ? $"Sync error: {LastError}" : "Sync error";
            if (PendingCount > 0) return $"{PendingCount} change(s) pending sync";
            if (LastSuccessfulSyncAt is { } at) return $"Last synced {at.ToOffset(TimeSpan.FromHours(8)):yyyy-MM-dd HH:mm} (Asia/Manila)";
            return "Not yet synced to the cloud";
        }
    }
}

public interface ISyncStatusSource
{
    SyncStatus Current { get; }
    event Action<SyncStatus>? Changed;
}
// JJQR - END
