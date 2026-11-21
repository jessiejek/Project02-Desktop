//JJQR - AI (21 Nov 2026) - shared live-update helper for any page (staff / doctor / admin): subscribe to /hubs/clinic events on appear,
// coalesce bursts into ONE reload on the UI thread, refetch after a hub reconnect (Resync: SignalR does not replay missed events),
// unsubscribe on disappear. No DI registration needed: it resolves IClinicLiveEvents from the IServiceProvider you pass in.
// Usage (see docs/ONLINE-FIRST.md §9):
//   _live = new LiveHubSubscription(sp, [ClinicHubEvents.QueueUpdated, ClinicHubEvents.PatientCheckedIn], ReloadAsync, e => e.DoctorId == _doctorId);
//   OnAppearing    → _live.Attach();
//   OnDisappearing → _live.Detach();
using ClinicNode.Api.Live;

namespace ClinicNode.App.Views.Shared.Live;

public sealed class LiveHubSubscription : IDisposable
{
    private readonly IServiceProvider _sp;
    private readonly IReadOnlyList<string> _events;
    private readonly Func<Task> _reload;
    private readonly Func<ClinicHubEvent, bool>? _filter;
    private readonly TimeSpan _debounce;
    private readonly List<IDisposable> _subs = [];
    private IClinicLiveEvents? _hub;
    private int _reloadQueued;

    /// <param name="sp">Service provider (the page's / app's).</param>
    /// <param name="events">ClinicHubEvents names that should refresh the page. Empty = the helper does nothing.</param>
    /// <param name="reload">The page's reload (must be safe to call repeatedly; runs on the UI thread).</param>
    /// <param name="filter">Optional: only events that match (e.g. this booking / this doctor). Resync always reloads.</param>
    /// <param name="debounce">Burst window; default 300 ms (check-in sends QueueUpdated + PatientCheckedIn together).</param>
    public LiveHubSubscription(IServiceProvider sp, IReadOnlyList<string> events, Func<Task> reload,
        Func<ClinicHubEvent, bool>? filter = null, TimeSpan? debounce = null)
    {
        _sp = sp;
        _events = events;
        _reload = reload;
        _filter = filter;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(300);
    }

    /// <summary>True between Attach and Detach (and only when the hub service exists).</summary>
    public bool IsAttached => _subs.Count > 0;

    /// <summary>Call from OnAppearing. Idempotent. Also starts the hub (idempotent; it only connects for an online sign-in).</summary>
    public void Attach()
    {
        if (IsAttached || _events.Count == 0) return;
        _hub = _sp.GetService(typeof(IClinicLiveEvents)) as IClinicLiveEvents;
        if (_hub is null) return;
        foreach (var name in _events)
            _subs.Add(_hub.Subscribe(name, OnEvent));
        _hub.Resync += QueueReload;
        try { _hub.Start(); } catch { /* offline / offline sign-in: the 60 s poll and the local DB still work */ }
    }

    /// <summary>Call from OnDisappearing. Idempotent; a reload already queued is skipped.</summary>
    public void Detach()
    {
        foreach (var sub in _subs) sub.Dispose();
        _subs.Clear();
        if (_hub is not null) _hub.Resync -= QueueReload;
        _hub = null;
    }

    public void Dispose() => Detach();

    private void OnEvent(ClinicHubEvent e)
    {
        bool wanted;
        try { wanted = _filter?.Invoke(e) ?? true; }
        catch { wanted = true; }
        if (wanted) QueueReload();
    }

    /// <summary>Events arrive off the UI thread and in bursts: at most one reload per debounce window.</summary>
    private void QueueReload()
    {
        if (Interlocked.Exchange(ref _reloadQueued, 1) == 1) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Task.Delay(_debounce);
                Interlocked.Exchange(ref _reloadQueued, 0);
                if (!IsAttached) return; // page closed meanwhile
                await _reload();
            }
            catch
            {
                Interlocked.Exchange(ref _reloadQueued, 0); // the page shows its own load error; never crash the UI thread
            }
        });
    }
}
//JJQR - END
