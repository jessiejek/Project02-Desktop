// JJQR - AI (Nov 21, 2026) - local live refresh: in-process clinic events plus a short poll (SignalR stand-in).
using ClinicNode.Core.Events;

namespace ClinicNode.App.Services;

public sealed class LiveRefresh : IDisposable
{
    private readonly IClinicEventBus _bus;
    private readonly Func<Task> _reload;
    private readonly IDispatcherTimer _timer;
    private bool _busy;

    public LiveRefresh(Page page, IClinicEventBus bus, Func<Task> reload, TimeSpan? interval = null)
    {
        _bus = bus;
        _reload = reload;
        _bus.Changed += OnEvent;
        _timer = page.Dispatcher.CreateTimer();
        _timer.Interval = interval ?? TimeSpan.FromSeconds(10);
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    void OnEvent(ClinicEvent _) =>
        MainThread.BeginInvokeOnMainThread(async () => await TickAsync());

    async Task TickAsync()
    {
        if (_busy) return;
        _busy = true;
        try { await _reload(); }
        catch { /* keep the page up; the next tick retries */ }
        finally { _busy = false; }
    }

    public void Dispose()
    {
        _bus.Changed -= OnEvent;
        _timer.Stop();
    }
}
// JJQR - END
