// JJQR - AI (Nov 21, 2026) - cloud push/pull contract. The cloud endpoints do not exist yet.
//JJQR - AI (Nov 21, 2026) - allen2: real client contract per docs/SYNC-API-CONTRACT.md v1
// (per-operation results, pulled aggregate changes, failure kinds for online/offline detection).
// Wire-agnostic: the JSON shapes are in Wire/.
using System.Text.Json.Nodes;
using ClinicNode.Data;

namespace ClinicNode.Sync;

public enum SyncFailureKind
{
    None,
    /// <summary>No route / DNS / timeout / connection refused: the node is offline.</summary>
    Offline,
    /// <summary>401 that survived one token refresh, or invalid node credentials.</summary>
    Unauthorized,
    /// <summary>403 node_revoked: stop syncing and lock (study §5.8).</summary>
    Revoked,
    /// <summary>426 protocol_unsupported: the node must update before syncing.</summary>
    VersionMismatch,
    /// <summary>5xx / 429: retry with backoff.</summary>
    Server,
    /// <summary>Unreadable response or other 4xx (e.g. invalid_cursor, batch_too_large).</summary>
    Protocol,
    /// <summary>No cloud URL configured.</summary>
    NotConfigured,
    /// <summary>Cloud URL set but this node has no node secret yet (admin must register it, §2.1).</summary>
    NotRegistered,
}

public enum PushOperationStatus
{
    Applied,
    Merged,
    Duplicate,
    Conflict,
    Rejected,
    Retry,
}

public sealed record PushOperationOutcome(Guid OperationId, PushOperationStatus Status)
{
    public long? RowVersion { get; init; }
    public string? ReasonCode { get; init; }
    public string? Message { get; init; }
    public IReadOnlyList<string>? ConflictFields { get; init; }
    /// <summary>Cloud copy after merge / on conflict (same shape as pull data).</summary>
    public JsonNode? Current { get; init; }
}

/// <summary>An outbox row with the full snapshot built from the current local rows (contract §3.1).</summary>
public sealed record OutgoingOperation(SyncOutboxEntry Entry, JsonNode? Data, long? BaseRowVersion)
{
    /// <summary>Patient updates: snake_case fields the node changed (contract §3.1 payload.changed_fields).</summary>
    public IReadOnlyList<string>? ChangedFields { get; init; }
}

public sealed record PulledChange(string Aggregate, string EntityId, bool IsDelete, JsonNode? Data)
{
    public long? RowVersion { get; init; }
    public Guid? OriginNodeId { get; init; }
    public DateTimeOffset? ChangedAt { get; init; }
}

public sealed record SyncPushResult(bool Acknowledged, string? Error)
{
    public IReadOnlyList<PushOperationOutcome> Operations { get; init; } = Array.Empty<PushOperationOutcome>();
    public SyncFailureKind Failure { get; init; }
    public DateTimeOffset? ServerTime { get; init; }
}

public sealed record SyncPullResult(bool Succeeded, string? NewCursor, string? Error, int Applied = 0)
{
    public IReadOnlyList<PulledChange> Changes { get; init; } = Array.Empty<PulledChange>();
    public bool HasMore { get; init; }
    public SyncFailureKind Failure { get; init; }
    public DateTimeOffset? ServerTime { get; init; }
}

public interface ICloudSyncClient
{
    bool IsConfigured { get; }
    Task<SyncPushResult> PushAsync(IReadOnlyList<OutgoingOperation> batch, CancellationToken ct = default);
    Task<SyncPullResult> PullAsync(string? sinceCursor, CancellationToken ct = default);
    /// <summary>Cheap reachability probe (GET /health). Never throws.</summary>
    Task<bool> PingAsync(CancellationToken ct = default);
    //JJQR - AI (21 Nov 2026) - POST /api/sync/queue-blocks (sync-node token). Null = not reserved (offline, refused, old server). Default keeps fakes compiling.
    Task<QueueBlockReservation?> ReserveQueueBlockAsync(DateOnly date, string lane, int size, CancellationToken ct = default) =>
        Task.FromResult<QueueBlockReservation?>(null);
    //JJQR - END
    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 4.1 (desktop side): POST /api/sync/review-items. Problems only this computer knows about (a change the server refused, one stuck after repeated tries) go to the server's review list for the admin.
    Task<ReviewUploadResult> UploadReviewItemsAsync(IReadOnlyList<ReviewItemUpload> items, CancellationToken ct = default) =>
        Task.FromResult(new ReviewUploadResult(false, []));
    //JJQR - END
}

/// <summary>
/// Used when <c>NodeOptions.CloudBaseUrl</c> is empty. Reports "not configured" so the outbox
/// accumulates and the UI can show pending sync, without pretending a push succeeded.
/// </summary>
public sealed class UnconfiguredCloudSyncClient : ICloudSyncClient
{
    public const string Message = "Cloud sync API is not configured on this node yet.";

    public bool IsConfigured => false;

    public Task<SyncPushResult> PushAsync(IReadOnlyList<OutgoingOperation> batch, CancellationToken ct = default) =>
        Task.FromResult(new SyncPushResult(false, Message) { Failure = SyncFailureKind.NotConfigured });

    public Task<SyncPullResult> PullAsync(string? sinceCursor, CancellationToken ct = default) =>
        Task.FromResult(new SyncPullResult(false, sinceCursor, Message) { Failure = SyncFailureKind.NotConfigured });

    public Task<bool> PingAsync(CancellationToken ct = default) => Task.FromResult(false);
}
//JJQR - END
// JJQR - END

//JJQR - AI (21 Nov 2026) - contract: {date:"yyyy-MM-dd", lane:"R"|"S", size?:20} -> {date, lane, from, to, size, first:"R-041", last:"R-060"}
public sealed record QueueBlockReservation(DateOnly Date, string Lane, int From, int To, int Size, string? First, string? Last);
//JJQR - END

//JJQR - AI (Nov 22, 2026) - step 4.1 (desktop side)
/// <summary>One problem to show the admin. The id is this computer's own, so sending it twice is harmless. Only a short description and ids go up: never the patient data of the change.</summary>
public sealed record ReviewItemUpload(Guid Id, string Kind, string Title, object? Details, string Aggregate, string? EntityId, DateTimeOffset CreatedAt);

/// <summary>Succeeded = the server answered; Done = the ids it now has (new or already known) or will never take, so they are not sent again.</summary>
public sealed record ReviewUploadResult(bool Succeeded, IReadOnlyList<Guid> Done);
//JJQR - END

