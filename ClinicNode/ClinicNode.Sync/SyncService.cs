// JJQR - AI (Nov 21, 2026) - sync loop: push outbox (idempotent by operation id), pull since cursor, backoff.
//JJQR - AI (Nov 21, 2026) - allen2: real push/pull cycle per docs/SYNC-API-CONTRACT.md v1:
// snapshots built at push time, per-operation acks (all but "retry"), merged/conflict copies applied,
// pull pages applied + cursor stored in one local transaction, last synced = cloud server_time.
using System.ComponentModel;
using System.Text.Json.Nodes;
using ClinicApp.Infrastructure;
using ClinicNode.Data;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicNode.Sync;

public interface ISyncService : ISyncStatusSource
{
    Task<SyncStatus> RefreshStatusAsync(CancellationToken ct = default);
    Task<SyncCycleResult> RunOnceAsync(CancellationToken ct = default);
}

public sealed record SyncCycleResult(bool Succeeded, SyncFailureKind Failure, string? Error)
{
    public int Pushed { get; init; }
    public int Acked { get; init; }
    public int RetryLater { get; init; }
    public int Pulled { get; init; }
    public int Applied { get; init; }
}

public sealed class SyncService : ISyncService, ISyncStatus, ClinicNode.Core.Sync.ISyncBridge
{
    public const string CloudStream = "cloud";

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly ICloudSyncClient _cloud;
    private readonly SyncOptions _options;
    private readonly ILogger<SyncService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    //JJQR - AI (Nov 21, 2026)
    private readonly ISyncEndpoint? _endpoint;
    private const string CloudUrlKey = "cloud_url";
    //JJQR - END
    private SyncStatus _current = new();

    public SyncService(
        IDbContextFactory<ClinicAppDbContext> factory,
        ISyncOutbox outbox,
        ICloudSyncClient cloud,
        SyncOptions options,
        ILogger<SyncService> logger,
        //JJQR - AI (Nov 21, 2026)
        ISyncEndpoint? endpoint = null)
        //JJQR - END
    {
        //JJQR - AI (Nov 21, 2026) - a settings change wakes the loop at once
        _endpoint = endpoint;
        if (endpoint is not null) endpoint.Changed += _ => RequestSync();
        //JJQR - END
        _factory = factory;
        _outbox = outbox;
        _cloud = cloud;
        _options = options;
        _logger = logger;
    }

    // ── ISyncStatus / ISyncStatusSource ───────────────────────────────────
    public SyncStatus Current => _current;
    public event Action<SyncStatus>? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;
    public SyncPhase Phase => _current.Phase;
    public int PendingCount => _current.PendingCount;
    public int ConflictCount => _current.ConflictCount;
    public DateTimeOffset? LastSyncedAt => _current.LastSuccessfulSyncAt;
    public string? LastError => _current.LastError;
    public bool IsOnline => _current.IsOnline;
    public bool IsSyncing => _current.Phase == SyncPhase.Syncing;
    public string Banner => _current.Banner;

    //JJQR - AI (Nov 21, 2026) - reprint signal (queue slips, patient cards); see ISyncStatus
    private int _reprintCount;
    private int _requeued;
    public int ReprintCount => _reprintCount;
    public event EventHandler<ReprintFlag>? ReprintRequired;

    public async Task<IReadOnlyList<ReprintFlag>> GetReprintFlagsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await SyncLocalStore.EnsureSchemaAsync(db, ct);
        return await SyncLocalStore.ListReprintFlagsAsync(db, ct: ct);
    }

    public async Task DismissReprintFlagAsync(Guid flagId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await SyncLocalStore.EnsureSchemaAsync(db, ct);
        await SyncLocalStore.DismissReprintFlagAsync(db, flagId, ct);
        await NotifyReprintAsync(db, ct);
    }

    /// <summary>Updates ReprintCount (PropertyChanged) and raises ReprintRequired once per new flag.</summary>
    private async Task NotifyReprintAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        var fresh = await SyncLocalStore.ListReprintFlagsAsync(db, onlyUnnotified: true, ct: ct);
        if (fresh.Count > 0) await SyncLocalStore.MarkReprintNotifiedAsync(db, ct);
        var count = await SyncLocalStore.CountReprintFlagsAsync(db, ct);
        if (count != _reprintCount)
        {
            _reprintCount = count;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReprintCount)));
        }
        foreach (var f in fresh) ReprintRequired?.Invoke(this, f);
    }
    //JJQR - END

    /// <summary>Set by the background loop so RequestSync can wake it.</summary>
    internal Action? SyncRequested { get; set; }
    public void RequestSync() => SyncRequested?.Invoke();

    public async Task RefreshAsync(CancellationToken ct = default) => await RefreshStatusAsync(ct);

    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.4: "registered" = a cloud address is set AND this computer holds a node secret AND it is not revoked AND the app is not too old.
    // No cloud address at all (a stand-alone demo computer) = nothing to register with, so saving locally stays allowed.
    public async Task<string?> LocalSaveBlockedReasonAsync(CancellationToken ct = default)
    {
        if (!_cloud.IsConfigured) return null;
        if (await IsRevokedAsync(ct))
            return "This computer was disconnected from the clinic server by an admin, so it can't save changes. Ask an admin to register it again.";
        if (_current.Phase == SyncPhase.NeedsUpdate)
            return "This app is too old to sync with the clinic server. Update it before saving changes on this computer.";
        if (_cloud is ClinicNode.Sync.Wire.ISyncNodeRegistration reg && !await reg.IsRegisteredAsync(ct))
            return "Register this computer for sync first (Admin › Sync computers). Until then, changes made here can't be sent to the clinic server.";
        return null;
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - Core's ISyncBridge: login locks at once for a revoked node (flag survives restarts)
    public async Task<bool> IsRevokedAsync(CancellationToken ct = default)
    {
        if (_current.Phase == SyncPhase.Revoked) return true;
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            await SyncLocalStore.EnsureSchemaAsync(db, ct);
            return await SyncLocalStore.GetStateAsync(db, RevokedKey, ct) == "1";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the revoked flag; treating the node as not revoked.");
            return false;
        }
    }

    private const string RevokedKey = "revoked";
    //JJQR - END

    public async Task<IReadOnlyList<SyncConflict>> GetOpenConflictsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await SyncLocalStore.EnsureSchemaAsync(db, ct);
        return await SyncLocalStore.ListOpenConflictsAsync(db, ct: ct);
    }

    public async Task ResolveConflictAsync(Guid conflictId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await SyncLocalStore.EnsureSchemaAsync(db, ct);
        await SyncLocalStore.ResolveConflictAsync(db, conflictId, ct);
        await RefreshStatusAsync(ct);
    }

    public async Task<SyncStatus> RefreshStatusAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await SyncLocalStore.EnsureSchemaAsync(db, ct);
        var pending = await _outbox.CountPendingAsync(db, ct);
        var conflicts = await SyncLocalStore.CountOpenConflictsAsync(db, ct);
        await NotifyReprintAsync(db, ct); //JJQR - AI (Nov 21, 2026) // JJQR - END
        var identity = await SyncOutbox.GetOrCreateIdentityAsync(db, ct);
        var phase = !_cloud.IsConfigured
            ? (pending > 0 ? SyncPhase.Offline : SyncPhase.Idle)
            : _current.Phase;
        Publish(new SyncStatus
        {
            Phase = phase,
            PendingCount = pending,
            ConflictCount = conflicts,
            LastSuccessfulSyncAt = identity.LastSuccessfulSyncAt,
            LastError = phase is SyncPhase.Idle or SyncPhase.Offline && !_cloud.IsConfigured ? null : _current.LastError,
            IsOnline = _current.IsOnline,
        });
        return _current;
    }

    internal void ReportConnectivity(bool online)
    {
        if (online == _current.IsOnline && (online || _current.Phase == SyncPhase.Offline)) return;
        Publish(With(_current, online ? _current.Phase : SyncPhase.Offline, online, online ? _current.LastError : "Cloud unreachable"));
    }

    // ── one cycle ─────────────────────────────────────────────────────────
    public async Task<SyncCycleResult> RunOnceAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return await RunCycleAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SyncCycleResult> RunCycleAsync(CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await SyncLocalStore.EnsureSchemaAsync(db, ct);

        if (!_cloud.IsConfigured)
        {
            await RefreshStatusAsync(ct);
            return new SyncCycleResult(false, SyncFailureKind.NotConfigured, UnconfiguredCloudSyncClient.Message);
        }

        //JJQR - AI (Nov 21, 2026) - the cloud URL changed since the last cycle (settings or restart): start pulling afresh
        if (_endpoint is not null)
        {
            var inUse = await SyncLocalStore.GetStateAsync(db, CloudUrlKey, ct);
            if (!string.Equals(inUse, _endpoint.BaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                if (inUse is not null)
                {
                    _logger.LogInformation("Cloud URL changed from {Old} to {New}; resetting the pull cursor.", inUse, _endpoint.BaseUrl);
                    await SyncLocalStore.ResetForNewCloudAsync(db, ct);
                }
                await SyncLocalStore.SetStateAsync(db, CloudUrlKey, _endpoint.BaseUrl, ct);
            }
        }
        //JJQR - END
        //JJQR - AI (Nov 21, 2026) - queue audit rows written without an outbox hook
        try { await SyncLocalStore.SweepAuditLogsAsync(db, _outbox, 500, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Audit sweep failed; will retry next cycle."); }
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - consents recorded on this computer (offline too) go up as append-only patient_consent ops
        try { await SyncLocalStore.SweepConsentsAsync(db, _outbox, 500, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning("Consent sweep failed ({Type}); will retry next cycle.", ex.GetType().Name); }
        //JJQR - END
        Publish(With(_current, SyncPhase.Syncing, _current.IsOnline, null, await _outbox.CountPendingAsync(db, ct)));

        // 1) push
        int pushed = 0, acked = 0, retry = 0;
        for (var batchNo = 0; batchNo < _options.MaxPushBatchesPerCycle; batchNo++)
        {
            var now = DateTimeOffset.UtcNow;
            var window = await _outbox.ListPendingAsync(db, Math.Max(_options.PushBatchSize * 4, 200), ct);
            //JJQR - AI (21 Nov 2026) - TASK CLINIC-SETTINGS OUTBOX: clinic fee / setting changes are ONLINE-ONLY (no replay). Any clinic-settings outbox
            // row (queued before today by the old Admin settings save, or still queued by it now) is dropped here WITHOUT being pushed: acked locally,
            // plus one "skipped" review item so the admin knows to re-enter the change online. No push route is invented. The local settings row is untouched.
            // (21 Nov 2026, Jessie: "Staff invites and revokes: online-only.") announcement / staff_account / doctor_schedule rows are dropped the same way;
            // the local announcement / staff / doctor rows themselves are left as they are.
            var onlineOnly = window.Where(e => SyncReasonCodes.IsOnlineOnlyAggregate(e.Aggregate)).ToList();
            if (onlineOnly.Count > 0)
            {
                foreach (var e in onlineOnly)
                {
                    await _outbox.MarkAckedAsync(db, e.OperationId, ct);
                    await SyncLocalStore.AddConflictAsync(db, new SyncConflict
                    {
                        Aggregate = e.Aggregate, EntityId = e.EntityId.ToString(), Kind = "skipped", ReasonCode = SyncReasonCodes.OnlineOnly,
                        Message = SyncReasonCodes.OnlineOnlyMessage(e.Aggregate), //JJQR - AI (21 Nov 2026) - per kind (clinic settings, announcement, staff_account, doctor_schedule) //JJQR - END
                    }, ct);
                }
                db.ChangeTracker.Clear();
                window = window.Where(e => !SyncReasonCodes.IsOnlineOnlyAggregate(e.Aggregate)).ToList();
            }
            //JJQR - END
            var batch = window
                .Where(e => e.Attempts == 0 || e.LastAttemptAt is null || e.LastAttemptAt.Value + Backoff.DelayAfter(e.Attempts) <= now)
                .Take(_options.PushBatchSize)
                .ToList();
            if (batch.Count == 0) break;

            var ops = new List<OutgoingOperation>(batch.Count);
            foreach (var e in batch)
            {
                var data = e.OperationType == "delete" ? new JsonObject { ["id"] = e.EntityId.ToString() } : await AggregateDocuments.BuildSnapshotAsync(db, e, ct);
                var baseVersion = await SyncLocalStore.GetRowVersionAsync(db, e.Aggregate, e.EntityId.ToString(), ct);
                ops.Add(new OutgoingOperation(e, data, baseVersion) { ChangedFields = ChangedFieldsOf(e) });
            }
            db.ChangeTracker.Clear();

            var result = await _cloud.PushAsync(ops, ct);
            //JJQR - AI (Nov 21, 2026) - a batch-level 400/409 (invalid_operation, id_conflict, ...) means ONE operation is bad and
            // nothing was applied: push the operations one by one so only the culprit is rejected, never retry the batch blindly.
            if (!result.Acknowledged && result.Failure == SyncFailureKind.Protocol
                && SyncReasonCodes.IsPoisonBatchError(SyncReasonCodes.ErrorCodeOf(result.Error)))
            {
                var code = SyncReasonCodes.ErrorCodeOf(result.Error)!;
                if (ops.Count > 1)
                {
                    var merged = new List<PushOperationOutcome>();
                    foreach (var single in ops)
                    {
                        var one = await _cloud.PushAsync(new[] { single }, ct);
                        if (one.Acknowledged) { merged.AddRange(one.Operations); continue; }
                        if (one.Failure == SyncFailureKind.Protocol && SyncReasonCodes.IsPoisonBatchError(SyncReasonCodes.ErrorCodeOf(one.Error)))
                        {
                            merged.Add(new PushOperationOutcome(single.Entry.OperationId, PushOperationStatus.Rejected)
                            { ReasonCode = SyncReasonCodes.ErrorCodeOf(one.Error), Message = one.Error });
                            continue;
                        }
                        return await FailAsync(db, one.Failure, one.Error, ct, pushed, acked);
                    }
                    result = new SyncPushResult(true, null) { Operations = merged };
                }
                else
                {
                    result = new SyncPushResult(true, null)
                    {
                        Operations = new[] { new PushOperationOutcome(ops[0].Entry.OperationId, PushOperationStatus.Rejected) { ReasonCode = code, Message = result.Error } },
                    };
                }
            }
            //JJQR - END
            if (!result.Acknowledged)
                return await FailAsync(db, result.Failure, result.Error, ct, pushed, acked);

            pushed += batch.Count;
            var byId = result.Operations.GroupBy(o => o.OperationId).ToDictionary(g => g.Key, g => g.Last());
            var batchAcked = 0;
            foreach (var op in ops)
            {
                if (!byId.TryGetValue(op.Entry.OperationId, out var outcome))
                {
                    await _outbox.MarkAttemptAsync(db, op.Entry.OperationId, "No result returned for this operation.", ct);
                    retry++;
                    continue;
                }
                if (await HandleOutcomeAsync(db, op, outcome, ct)) { acked++; batchAcked++; }
                else retry++;
            }
            //JJQR - AI (Nov 21, 2026) - an automatic amendment retry was queued: push it in this same cycle
            if (Interlocked.Exchange(ref _requeued, 0) > 0) continue;
            //JJQR - END
            if (batchAcked == 0 || batch.Count < _options.PushBatchSize) break;
        }

        // 2) pull
        var cursor = await SyncOutbox.GetCursorAsync(db, CloudStream, ct);
        int pulled = 0, applied = 0;
        DateTimeOffset? serverTime = null;
        for (var page = 0; page < _options.MaxPullPagesPerCycle; page++)
        {
            var pull = await _cloud.PullAsync(cursor, ct);
            if (!pull.Succeeded)
                return await FailAsync(db, pull.Failure, pull.Error, ct, pushed, acked);
            serverTime = pull.ServerTime ?? serverTime;
            pulled += pull.Changes.Count;
            applied += await ApplyPageAsync(db, pull, ct);
            cursor = pull.NewCursor;
            if (!pull.HasMore) break;
        }

        //JJQR - AI (21 Nov 2026) - online: keep queue-number blocks for today..today+6 in lanes R and S (refill at <= 5 left); never fails the cycle
        try
        {
            var today = ClinicApp.Domain.ClinicClock.Today;
            await QueueBlocks.PruneAsync(db, today, ct);
            var blocks = await QueueBlocks.TopUpAsync(db, _cloud, today, ct);
            if (blocks > 0) _logger.LogInformation("Reserved {Blocks} queue-number block(s).", blocks);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning("Queue block top-up failed ({Type}); will retry next cycle.", ex.GetType().Name); }
        //JJQR - END
        var at = serverTime ?? DateTimeOffset.UtcNow;
        await SyncOutbox.SetLastSuccessfulSyncAsync(db, at, ct);
        await NotifyReprintAsync(db, ct); //JJQR - AI (Nov 21, 2026) // JJQR - END
        //JJQR - AI (Nov 21, 2026)
        await SyncLocalStore.SetStateAsync(db, RevokedKey, null, ct);
        //JJQR - END
        Publish(new SyncStatus
        {
            Phase = SyncPhase.Idle,
            PendingCount = await _outbox.CountPendingAsync(db, ct),
            ConflictCount = await SyncLocalStore.CountOpenConflictsAsync(db, ct),
            LastSuccessfulSyncAt = at,
            IsOnline = true,
        });
        _logger.LogInformation("Sync cycle: pushed {Pushed} (acked {Acked}, retry {Retry}); pulled {Pulled}, applied {Applied}.", pushed, acked, retry, pulled, applied);
        return new SyncCycleResult(true, SyncFailureKind.None, null) { Pushed = pushed, Acked = acked, RetryLater = retry, Pulled = pulled, Applied = applied };
    }

    /// <summary>Contract §3.3. Returns true when the operation was acked.</summary>
    private async Task<bool> HandleOutcomeAsync(ClinicAppDbContext db, OutgoingOperation op, PushOperationOutcome o, CancellationToken ct)
    {
        var e = op.Entry;
        if (o.Status == PushOperationStatus.Retry)
        {
            await _outbox.MarkAttemptAsync(db, e.OperationId, Describe(o), ct);
            return false;
        }

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            try
            {
                if (o.Status == PushOperationStatus.Rejected)
                    await _outbox.MarkAttemptAsync(db, e.OperationId, Describe(o), ct);
                await _outbox.MarkAckedAsync(db, e.OperationId, ct);
                if (o.RowVersion is { } v)
                    await SyncLocalStore.SetRowVersionAsync(db, e.Aggregate, e.EntityId.ToString(), v, ct);

                //JJQR - AI (Nov 21, 2026) - an "applied" payment that overwrote a cloud edit (cloud_edit_overwritten) is also listed
                var overwrote = o.Status == PushOperationStatus.Applied && o.ReasonCode == SyncReasonCodes.CloudEditOverwritten;
                //JJQR - AI (21 Nov 2026) - "applied" + or_number_clash: success (acked above, never retried), but listed for review as "flagged for admin"
                var orClash = o.Status == PushOperationStatus.Applied && string.Equals(o.ReasonCode, SyncReasonCodes.OrNumberClash, StringComparison.OrdinalIgnoreCase);
                //JJQR - AI (21 Nov 2026) - same pattern for a flagged duplicate queue number (code not documented yet: queue_number_clash expected). Success, no local number change.
                var queueClash = o.Status == PushOperationStatus.Applied && string.Equals(o.ReasonCode, SyncReasonCodes.QueueNumberClash, StringComparison.OrdinalIgnoreCase);
                //JJQR - END
                //JJQR - END
                //JJQR - AI (21 Nov 2026) - a merged result with queue_number_reassigned is listed under its own kind (it was already listed as "merged").
                // Its cloud copy is applied as before, but ApplyBookingAsync never takes a new queue number for a booking that has one (Jessie: keep the printed number).
                var queueReassigned = o.Status == PushOperationStatus.Merged
                    && string.Equals(o.ReasonCode, SyncReasonCodes.QueueNumberReassigned, StringComparison.OrdinalIgnoreCase);
                //JJQR - END
                //JJQR - AI (Nov 21, 2026) - v1.1: a stale_amendment is pulled (the conflict's `current` is the cloud copy), the doctor's amendment is
                // re-applied on top of it and pushed again automatically, ONCE (retry_of marks the second attempt). The second failure goes on the review list.
                var autoRetry = o.Status == PushOperationStatus.Conflict && o.ReasonCode == "stale_amendment"
                    && string.Equals(e.Aggregate, Aggregates.Consultation, StringComparison.OrdinalIgnoreCase)
                    && o.Current is not null && op.Data is not null && !IsAmendRetry(e);
                //JJQR - END
                if (!autoRetry && (o.Status is PushOperationStatus.Merged or PushOperationStatus.Conflict or PushOperationStatus.Rejected || overwrote || orClash || queueClash)) //JJQR - AI (21 Nov 2026) - + orClash, queueClash //JJQR - END
                {
                    await SyncLocalStore.AddConflictAsync(db, new SyncConflict
                    {
                        Aggregate = e.Aggregate,
                        EntityId = e.EntityId.ToString(),
                        Kind = queueClash ? SyncReasonCodes.QueueNumberClash : queueReassigned ? SyncReasonCodes.QueueNumberReassigned : orClash ? SyncReasonCodes.OrNumberClash : overwrote ? SyncReasonCodes.CloudEditOverwritten : o.Status.ToString().ToLowerInvariant(), //JJQR - AI (21 Nov 2026) - or_number_clash kind //JJQR - END
                        ReasonCode = o.ReasonCode,
                        Message = queueClash ? SyncReasonCodes.Describe(SyncReasonCodes.QueueNumberClash) + (string.IsNullOrWhiteSpace(o.Message) ? "" : " " + o.Message) : //JJQR - AI (21 Nov 2026) //JJQR - END
                            orClash ? SyncReasonCodes.Describe(SyncReasonCodes.OrNumberClash) + (string.IsNullOrWhiteSpace(o.Message) ? "" : " " + o.Message) : //JJQR - AI (21 Nov 2026) //JJQR - END
                            (IsAmendRetry(e) && o.ReasonCode == "stale_amendment" ? "The automatic retry also found a newer cloud copy. " : "")
                            + (o.Message ?? SyncReasonCodes.Describe(o.ReasonCode) ?? (o.ConflictFields is { Count: > 0 } f ? "Cloud kept: " + string.Join(", ", f) : null)),
                        OperationId = e.OperationId,
                        LocalJson = op.Data?.ToJsonString(),
                        CloudJson = o.Current?.ToJsonString(),
                    }, ct);
                }
                if (o.Status is PushOperationStatus.Merged or PushOperationStatus.Conflict && o.Current is not null)
                {
                    await AggregateDocuments.ApplyAsync(db, new PulledChange(e.Aggregate, e.EntityId.ToString(), false, o.Current), ApplyMode.ServerResult, ct);
                }
                //JJQR - AI (Nov 21, 2026)
                if (autoRetry)
                {
                    // the cloud copy is in; lay the doctor's amendment over it and queue ONE new push with the fresh base_row_version
                    await AggregateDocuments.ApplyAsync(db, new PulledChange(e.Aggregate, e.EntityId.ToString(), false, op.Data), ApplyMode.ServerResult, ct);
                    await _outbox.EnqueueAsync(db, new SyncOutboxEntry
                    {
                        OperationId = Guid.NewGuid(), Aggregate = e.Aggregate, EntityId = e.EntityId, OperationType = "update",
                        PayloadJson = new JsonObject { ["retry_of"] = e.OperationId.ToString() }.ToJsonString(),
                        CreatedAt = DateTimeOffset.UtcNow,
                    }, ct);
                    Interlocked.Increment(ref _requeued);
                }
                //JJQR - END
                await tx.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Could not apply the cloud copy for {Aggregate} {Id}; acking and listing for review.", e.Aggregate, e.EntityId);
                await _outbox.MarkAckedAsync(db, e.OperationId, ct);
                await SyncLocalStore.AddConflictAsync(db, new SyncConflict
                {
                    Aggregate = e.Aggregate, EntityId = e.EntityId.ToString(), Kind = "apply_failed", ReasonCode = o.ReasonCode,
                    Message = ex.Message, OperationId = e.OperationId, LocalJson = op.Data?.ToJsonString(), CloudJson = o.Current?.ToJsonString(),
                }, ct);
            }
        }
        db.ChangeTracker.Clear();
        return true;
    }

    //JJQR - AI (Nov 21, 2026)
    private static bool IsAmendRetry(SyncOutboxEntry e)
    {
        try { return JsonNode.Parse(e.PayloadJson)?["retry_of"] is not null; }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return false; }
    }

    private static IReadOnlyList<string>? ChangedFieldsOf(SyncOutboxEntry e)
    {
        if (!string.Equals(e.Aggregate, Aggregates.Patient, StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var arr = JsonNode.Parse(e.PayloadJson)?["changed_fields"] as JsonArray;
            var list = arr?.Select(n => n?.GetValue<string>()).Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
            return list is { Count: > 0 } ? list : null;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }
    //JJQR - END

    private static string Describe(PushOperationOutcome o) =>
        string.Join(": ", new[] { o.Status.ToString().ToLowerInvariant(), o.ReasonCode, o.Message ?? SyncReasonCodes.Describe(o.ReasonCode) }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>Applies one pull page and stores its cursor in one transaction (contract §4/§9).</summary>
    private async Task<int> ApplyPageAsync(ClinicAppDbContext db, SyncPullResult page, CancellationToken ct)
    {
        var ordered = page.Changes
            .OrderBy(c => c.IsDelete ? 1 : 0)
            .ThenBy(c => c.IsDelete ? -Aggregates.Rank(c.Aggregate) : Aggregates.Rank(c.Aggregate))
            .ToList();
        var applied = 0;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var failed = new List<(PulledChange Change, Exception Error)>();
        //JJQR - AI (Nov 21, 2026) - catalogs (icd10 ~12k rows): runs of one cloud-owned aggregate are applied in bulk (one load, one SaveChanges, one
        // multi-row row_version write per chunk of 500). A chunk that fails falls back to the per-change path below.
        var bulkDone = new HashSet<PulledChange>(ReferenceEqualityComparer.Instance);
        var bulkable = ordered.Where(AggregateDocuments.IsBulkable).GroupBy(c => c.Aggregate.ToLowerInvariant()).ToList();
        var bulkNo = 0;
        foreach (var group in bulkable)
        {
            foreach (var chunk in group.Chunk(500))
            {
                if (chunk.Length < 20) continue; // small runs: the per-change path is just as good
                var sp = $"b{bulkNo++}";
                await tx.CreateSavepointAsync(sp, ct);
                try
                {
                    var n = await AggregateDocuments.ApplyBulkAsync(db, group.Key, chunk, ct);
                    if (n < 0) { await tx.ReleaseSavepointAsync(sp, ct); db.ChangeTracker.Clear(); break; }   // composite key: per-change path
                    await SyncLocalStore.SetRowVersionsAsync(db,
                        chunk.Where(c => c.RowVersion is not null).Select(c => (c.Aggregate, c.EntityId, c.RowVersion!.Value)).ToList(), ct);
                    await tx.ReleaseSavepointAsync(sp, ct);
                    foreach (var c in chunk) bulkDone.Add(c);
                    applied += n;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await tx.RollbackToSavepointAsync(sp, ct);
                    _logger.LogInformation(ex, "Bulk apply of {Aggregate} failed; applying its changes one by one.", group.Key);
                }
                db.ChangeTracker.Clear();
            }
        }
        if (bulkDone.Count > 0) ordered = ordered.Where(c => !bulkDone.Contains(c)).ToList();
        //JJQR - END
        for (var pass = 0; pass < 2; pass++)
        {
            var work = pass == 0 ? ordered : failed.Select(f => f.Change).ToList();
            if (pass == 1) failed = new();
            var i = 0;
            foreach (var change in work)
            {
                var sp = $"c{pass}_{i++}";
                await tx.CreateSavepointAsync(sp, ct);
                try
                {
                    if (await AggregateDocuments.ApplyAsync(db, change, ApplyMode.Pull, ct)) applied++;
                    if (change.RowVersion is { } v)
                        await SyncLocalStore.SetRowVersionAsync(db, change.Aggregate, change.EntityId, v, ct);
                    await tx.ReleaseSavepointAsync(sp, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await tx.RollbackToSavepointAsync(sp, ct);
                    failed.Add((change, ex));
                }
                db.ChangeTracker.Clear();
            }
            if (failed.Count == 0) break;
        }
        foreach (var (change, ex) in failed)
        {
            _logger.LogWarning(ex, "Pulled {Aggregate} {Id} could not be applied.", change.Aggregate, change.EntityId);
            await SyncLocalStore.AddConflictAsync(db, new SyncConflict
            {
                Aggregate = change.Aggregate, EntityId = change.EntityId, Kind = "apply_failed",
                Message = (ex.InnerException ?? ex).Message, CloudJson = change.Data?.ToJsonString(),
            }, ct);
        }
        if (page.NewCursor is not null)
            await SyncOutbox.SetCursorAsync(db, CloudStream, page.NewCursor, ct);
        await tx.CommitAsync(ct);
        return applied;
    }

    private async Task<SyncCycleResult> FailAsync(ClinicAppDbContext db, SyncFailureKind kind, string? error, CancellationToken ct, int pushed, int acked)
    {
        var phase = kind switch
        {
            SyncFailureKind.Offline => SyncPhase.Offline,
            SyncFailureKind.NotRegistered => SyncPhase.NotRegistered,
            SyncFailureKind.Revoked => SyncPhase.Revoked,
            SyncFailureKind.VersionMismatch => SyncPhase.NeedsUpdate,
            SyncFailureKind.NotConfigured => SyncPhase.Offline,
            _ => SyncPhase.Error,
        };
        var online = kind is not (SyncFailureKind.Offline or SyncFailureKind.NotConfigured);
        //JJQR - AI (Nov 21, 2026)
        if (kind == SyncFailureKind.Revoked) await SyncLocalStore.SetStateAsync(db, RevokedKey, "1", ct);
        //JJQR - END
        Publish(new SyncStatus
        {
            Phase = phase,
            PendingCount = await _outbox.CountPendingAsync(db, ct),
            ConflictCount = await SyncLocalStore.CountOpenConflictsAsync(db, ct),
            LastSuccessfulSyncAt = (await SyncOutbox.GetOrCreateIdentityAsync(db, ct)).LastSuccessfulSyncAt,
            LastError = error,
            IsOnline = online,
        });
        _logger.LogWarning("Sync cycle failed ({Kind}): {Error}", kind, error);
        return new SyncCycleResult(false, kind, error) { Pushed = pushed, Acked = acked };
    }

    private static SyncStatus With(SyncStatus s, SyncPhase phase, bool online, string? error, int? pending = null) => new()
    {
        Phase = phase,
        PendingCount = pending ?? s.PendingCount,
        ConflictCount = s.ConflictCount,
        LastSuccessfulSyncAt = s.LastSuccessfulSyncAt,
        LastError = error,
        IsOnline = online,
    };

    private void Publish(SyncStatus status)
    {
        var old = _current;
        _current = status;
        Changed?.Invoke(status);
        var handler = PropertyChanged;
        if (handler is null) return;
        void Raise(string name) => handler(this, new PropertyChangedEventArgs(name));
        if (old.Phase != status.Phase) { Raise(nameof(Phase)); Raise(nameof(IsSyncing)); }
        if (old.PendingCount != status.PendingCount) Raise(nameof(PendingCount));
        if (old.ConflictCount != status.ConflictCount) Raise(nameof(ConflictCount));
        if (old.LastSuccessfulSyncAt != status.LastSuccessfulSyncAt) Raise(nameof(LastSyncedAt));
        if (old.LastError != status.LastError) Raise(nameof(LastError));
        if (old.IsOnline != status.IsOnline) Raise(nameof(IsOnline));
        if (old.Banner != status.Banner) Raise(nameof(Banner));
        Raise(nameof(Current));
    }
}
//JJQR - END
// JJQR - END
