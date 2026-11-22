//JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.5: the ONE rule for the status badge, so the top bar, the indicator and the tests agree.
// Exactly three words: Online, Offline (N waiting), Sync problem (plus a short Syncing moment). Nothing here reads the screen or the network.
namespace ClinicNode.Sync;

public enum SyncBadgeKind { Online, Syncing, Offline, Problem }

public static class SyncBadge
{
    /// <param name="apiReachable">The connection check says the API answers AND its database is ready (step 0.2).</param>
    /// <param name="phase">The sync loop's phase.</param>
    /// <param name="hasSyncProblem">Sync error, revoked, update needed, or a change stuck after 10 tries (<see cref="SyncStatus.HasSyncProblem"/>).</param>
    public static SyncBadgeKind Resolve(bool apiReachable, SyncPhase phase, bool syncing, int pending, bool hasSyncProblem)
    {
        // A server that simply cannot be reached is Offline, never a "problem": the changes are safe and waiting.
        if (!apiReachable || phase == SyncPhase.Offline) return SyncBadgeKind.Offline;
        if (hasSyncProblem) return SyncBadgeKind.Problem;
        if (syncing || pending > 0) return SyncBadgeKind.Syncing;
        return SyncBadgeKind.Online;
    }

    public static string Text(SyncBadgeKind kind, int pending) => kind switch
    {
        SyncBadgeKind.Offline => $"Offline, {pending} change{(pending == 1 ? "" : "s")} waiting",
        SyncBadgeKind.Problem => "Sync problem",
        SyncBadgeKind.Syncing => "Syncing...",
        _ => "Online, up to date",
    };
}
//JJQR - END
