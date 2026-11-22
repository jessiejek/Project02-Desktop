//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.1: stores the work mode in the local key/value store (key "work_mode") and audits every change.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Sync;

public sealed class WorkModeService : IWorkModeService
{
    private readonly IDbContextFactory<ClinicAppDbContext> factory;
    private readonly Func<IAuthService> _auth; // looked up when needed: sign-in itself reads the work mode (step 2.6), so a direct dependency would be circular
    private readonly ISyncBridge? sync;
    private IAuthService auth => _auth();

    public WorkModeService(IDbContextFactory<ClinicAppDbContext> factory, IAuthService auth, ISyncBridge? sync = null) : this(factory, () => auth, sync) { }

    public WorkModeService(IDbContextFactory<ClinicAppDbContext> factory, Func<IAuthService> auth, ISyncBridge? sync = null)
    {
        this.factory = factory; _auth = auth; this.sync = sync;
    }

    public const string StateKey = "work_mode";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile WorkModeInfo _info = new(WorkMode.Online, null, null);

    public WorkMode Mode => _info.Mode;
    public WorkModeInfo Info => _info;
    //JJQR - AI (Nov 22, 2026) - step 2.1 (owner Q2): Staff and Admin cannot use the computer in Offline mode, so an Admin switching to Offline would lock themselves out. Only the doctor switches.
    public bool CanSwitch => auth.Current is { IsDoctor: true };
    //JJQR - END
    public event EventHandler<WorkModeInfo>? Changed;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await SyncLocalStore.EnsureSchemaAsync(db, ct);
            _info = Parse(await SyncLocalStore.GetStateAsync(db, StateKey, ct));
        }
        finally { _gate.Release(); }
    }

    public async Task<WorkModeChangeResult> SetAsync(WorkMode target, CancellationToken ct = default)
    {
        var who = auth.Current;
        if (who is null) return new(false, Mode, "Sign in first.");
        if (!who.IsDoctor) return new(false, Mode, "Only the doctor can switch the work mode.");

        await _gate.WaitAsync(ct);
        try
        {
            var before = _info.Mode;
            if (before == target) return new(true, before, null);
            if (target == WorkMode.Offline && sync is not null && await sync.LocalSaveBlockedReasonAsync(ct) is { } why)
                return new(false, before, "Offline mode needs this computer to be registered with the clinic server first. " + why);

            var info = new WorkModeInfo(target, who.UserId, DateTimeOffset.UtcNow);
            await using var db = await factory.CreateDbContextAsync(ct);
            await SyncLocalStore.EnsureSchemaAsync(db, ct);
            await SyncLocalStore.SetStateAsync(db, StateKey, Format(info), ct);
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(), EntityType = AuditEntityType.Settings, EntityId = who.UserId,
                Action = "WorkModeChanged", Details = $"{before} -> {target}", PerformedByUserId = who.UserId, PerformedAt = info.ChangedAt!.Value,
            });
            await db.SaveChangesAsync(ct);
            _info = info;
            Changed?.Invoke(this, info);
            return new(true, target, null);
        }
        finally { _gate.Release(); }
    }

    // "Offline|<user id>|<utc ticks>"; anything unreadable means Online (the safe default).
    private static string Format(WorkModeInfo i) => $"{i.Mode}|{i.ChangedByUserId:D}|{i.ChangedAt?.UtcTicks}";

    public static WorkModeInfo Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new(WorkMode.Online, null, null);
        var p = raw.Split('|');
        if (!Enum.TryParse<WorkMode>(p[0], out var mode)) return new(WorkMode.Online, null, null);
        Guid? by = p.Length > 1 && Guid.TryParse(p[1], out var g) ? g : null;
        DateTimeOffset? at = p.Length > 2 && long.TryParse(p[2], out var t) ? new DateTimeOffset(t, TimeSpan.Zero) : null;
        return new(mode, by, at);
    }
}
//JJQR - END
