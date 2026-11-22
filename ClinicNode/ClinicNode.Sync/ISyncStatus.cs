//JJQR - AI (Nov 21, 2026)
// Bindable sync status for the UI (allen wires it into the shell).
// PropertyChanged / Changed are raised on the sync thread: marshal to the UI thread
// (MainThread.BeginInvokeOnMainThread) before touching views.
using System.ComponentModel;
using ClinicNode.Sync.Local;

namespace ClinicNode.Sync;

public interface ISyncStatus : INotifyPropertyChanged, ISyncStatusSource
{
    SyncPhase Phase { get; }
    /// <summary>Unacked outbox rows.</summary>
    int PendingCount { get; }
    /// <summary>Open review-list items (conflicts, rejected ops, replaced patient codes).</summary>
    int ConflictCount { get; }
    /// <summary>Cloud server_time of the last fully successful push+pull cycle.</summary>
    DateTimeOffset? LastSyncedAt { get; }
    string? LastError { get; }
    bool IsOnline { get; }
    bool IsSyncing { get; }
    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.5 (default members so older fakes still compile)
    /// <summary>Changes the server refused 10+ times (stuck).</summary>
    int StuckCount => 0;
    /// <summary>True for the "Sync problem" badge: sync error, revoked, update needed, or something stuck.</summary>
    bool HasSyncProblem => Phase is SyncPhase.Error or SyncPhase.Revoked or SyncPhase.NeedsUpdate || StuckCount > 0;
    //JJQR - END
    /// <summary>Ready-made one-line text, e.g. "Offline — 3 waiting to sync".</summary>
    string Banner { get; }

    /// <summary>Re-read pending/conflict counts from the database.</summary>
    Task RefreshAsync(CancellationToken ct = default);
    /// <summary>Ask the background loop to sync now (e.g. after a save, or a "Sync now" button).</summary>
    void RequestSync();
    Task<IReadOnlyList<SyncConflict>> GetOpenConflictsAsync(CancellationToken ct = default);
    Task ResolveConflictAsync(Guid conflictId, CancellationToken ct = default);

    //JJQR - AI (Nov 21, 2026) - reprint signal: a queue slip (cloud renumbered a booking) or patient card (code replaced) must be printed again
    /// <summary>Open (not dismissed) reprint flags. PropertyChanged("ReprintCount") fires when it changes.</summary>
    int ReprintCount { get; }
    /// <summary>Raised once per new flag after a sync cycle (sync thread: marshal to the UI thread).</summary>
    event EventHandler<ReprintFlag>? ReprintRequired;
    Task<IReadOnlyList<ReprintFlag>> GetReprintFlagsAsync(CancellationToken ct = default);
    /// <summary>Call after the slip/card was reprinted.</summary>
    Task DismissReprintFlagAsync(Guid flagId, CancellationToken ct = default);
    //JJQR - END
}
//JJQR - END
