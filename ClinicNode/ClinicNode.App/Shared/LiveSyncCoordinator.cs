//JJQR - AI (21 Nov 2026) - owner rules 2–6: keep the local SQLite copy continuously up to date while online.
//   2. hub event (PatientCheckedIn / QueueUpdated / PaymentUpdated / any other) → immediate pull of the changed records (sync cycle: push + pull since cursor)
//   3. every 60 s → pull (safety net, on top of SyncLoop's own poll)
//   4. app start + every time the connection returns → catch-up pull from the saved sync cursor; the hub reconnects and screens get Resync
//   5. writes: API first (OnlineFirstProxy); unreachable → Local service saves + outbox row; the sync loop pushes it when back online (wake-up here)
//   6. OnlineStatusIndicator: "Online, up to date" · "Syncing..." · "Offline, N changes waiting"
// Start() is idempotent: TopBarView and LoginPage call it, so MauiProgram / App.xaml.cs need no change beyond the SharedModule line.
using ClinicNode.Api.Auth;
using ClinicNode.Api.Http;
using ClinicNode.Api.Live;
using ClinicNode.Core.Auth;
using ClinicNode.Sync;

namespace ClinicNode.App.Shared;

public sealed class LiveSyncCoordinator : IDisposable
{
    public static readonly TimeSpan SafetyNetInterval = TimeSpan.FromSeconds(60);

    private readonly IClinicLiveEvents _hub;
    private readonly IApiConnectivity _connectivity;
    private readonly ISyncStatus _sync;
    private readonly IAuthService _auth;
    private readonly object _gate = new();
    private readonly StandaloneVaccinationPush? _vaccines; //JJQR - AI (21 Nov 2026) //JJQR - END
    private bool _started;
    private CancellationTokenSource? _cts;

    public LiveSyncCoordinator(IClinicLiveEvents hub, IApiConnectivity connectivity, ISyncStatus sync, IAuthService auth,
        StandaloneVaccinationPush? vaccines = null) //JJQR - AI (21 Nov 2026) - + standalone vaccination push //JJQR - END
    {
        _hub = hub; _connectivity = connectivity; _sync = sync; _auth = auth;
        _vaccines = vaccines; //JJQR - AI (21 Nov 2026) //JJQR - END
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
            _cts = new CancellationTokenSource();
        }
        _hub.Received += OnHubEvent;
        _hub.Resync += Pull;
        _connectivity.Changed += OnConnectivity;
        if (_auth is IOnlineAuthSession online) online.SessionChanged += OnSessionChanged;
        _ = SafetyNetAsync(_cts.Token);
        _connectivity.Start();
        OnSessionChanged();
        Pull(); // startup catch-up from the saved cursor
    }

    private void OnHubEvent(ClinicHubEvent e) => Pull();

    private void OnConnectivity(bool online)
    {
        if (!online) return;
        Pull();                        // catch-up + push of everything queued while offline
        if (IsOnlineSession) _hub.Start();
    }

    private void OnSessionChanged()
    {
        _vaccines?.ClearSessionBlocks(); //JJQR - AI (21 Nov 2026) - TASK 3: rows parked by a 401 may go up after a new sign-in //JJQR - END
        if (IsOnlineSession) _hub.Start();
        else if (_auth.Current is null) _ = _hub.StopAsync();
    }

    private bool IsOnlineSession => _auth is IOnlineAuthSession { IsOnlineSession: true };

    private void Pull()
    {
        try { _sync.RequestSync(); } catch (Exception) { /* never break a screen */ }
        //JJQR - AI (21 Nov 2026) - standalone vaccinations go up via POST /api/patient-vaccinations (user session), only online + signed in online;
        // rows whose patient is not in the cloud yet wait for a later run (the sync cycle pushes the patient first).
        if (_vaccines is not null && IsOnlineSession && _connectivity.IsOnline) _ = _vaccines.RunAsync();
        //JJQR - END
    }

    private async Task SafetyNetAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(SafetyNetInterval);
        try { while (await timer.WaitForNextTickAsync(ct)) if (_connectivity.IsOnline) Pull(); }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _hub.Received -= OnHubEvent;
        _hub.Resync -= Pull;
        _connectivity.Changed -= OnConnectivity;
        if (_auth is IOnlineAuthSession online) online.SessionChanged -= OnSessionChanged;
    }
}

public enum OnlineIndicatorKind { Online, Syncing, Offline }

/// <summary>Owner rule 6: one short line for the top bar. Changed is raised off the UI thread.</summary>
public sealed class OnlineStatusIndicator
{
    private readonly IApiConnectivity _connectivity;
    private readonly ISyncStatus _sync;

    public OnlineStatusIndicator(IApiConnectivity connectivity, ISyncStatus sync)
    {
        _connectivity = connectivity; _sync = sync;
        _connectivity.Changed += _ => Changed?.Invoke();
        _sync.PropertyChanged += (_, _) => Changed?.Invoke();
    }

    public event Action? Changed;

    public OnlineIndicatorKind Kind
    {
        get
        {
            var offline = !_connectivity.IsOnline || _sync.Phase is SyncPhase.Offline;
            if (offline) return OnlineIndicatorKind.Offline;
            if (_sync.IsSyncing || _sync.PendingCount > 0) return OnlineIndicatorKind.Syncing;
            return OnlineIndicatorKind.Online;
        }
    }

    public string Text => Kind switch
    {
        OnlineIndicatorKind.Offline => $"Offline, {_sync.PendingCount} change{(_sync.PendingCount == 1 ? "" : "s")} waiting",
        OnlineIndicatorKind.Syncing => "Syncing...",
        _ => "Online, up to date",
    };

    /// <summary>Theme colour key for the status dot (Colors.xaml).</summary>
    public string ColorKey => Kind switch
    {
        OnlineIndicatorKind.Offline => "StatusWarning",
        OnlineIndicatorKind.Syncing => "Primary",
        _ => "StatusSuccess",
    };
}
//JJQR - END
