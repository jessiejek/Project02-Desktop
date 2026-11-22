//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.1: the work mode (Online / Offline) the owner chooses for the day. Chosen by a person, never by the app.
namespace ClinicNode.Core.Sync;

public enum WorkMode { Online, Offline }

/// <summary>The current mode and who set it last (null before the first change).</summary>
public sealed record WorkModeInfo(WorkMode Mode, Guid? ChangedByUserId, DateTimeOffset? ChangedAt);

public sealed record WorkModeChangeResult(bool Succeeded, WorkMode Mode, string? Error);

public interface IWorkModeService
{
    /// <summary>The mode right now. <see cref="InitializeAsync"/> must have run (at start-up, before the login page) so a restart keeps the mode.</summary>
    WorkMode Mode { get; }
    WorkModeInfo Info { get; }
    bool IsOffline => Mode == WorkMode.Offline;

    /// <summary>True when the signed-in user may switch: the Doctor only (owner Q2: Staff and Admin cannot use the computer in Offline mode). Staff, Admin and signed-out never.</summary>
    bool CanSwitch { get; }

    /// <summary>Raised after the mode really changed (any thread).</summary>
    event EventHandler<WorkModeInfo>? Changed;

    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>Switch. Refused (not thrown) for Staff, for a signed-out user, and for Offline on a computer that cannot sync (not registered).
    /// Writes one local audit row and keeps the new mode across restarts.</summary>
    Task<WorkModeChangeResult> SetAsync(WorkMode target, CancellationToken ct = default);
}
//JJQR - END
