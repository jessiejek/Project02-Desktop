//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.3: the connection monitor. It keeps the last 60 health checks and turns the last 10 into one word.
// It only OBSERVES (the checks come from ApiConnectivity); it never changes the work mode. Suggestions (step 1.4) read it.
// Thresholds are the owner's answer to Q6 (22 Nov): Good under 300 ms; Slow 300 ms or more, or 1 failed check in the last 10; Unstable 2-4 failed;
// Down = 3 failed in a row, or 5+ failed in the last 10, or the database not ready.
namespace ClinicNode.Api.Http;

public enum ConnectionQuality { Unknown, Good, Slow, Unstable, Down }

public sealed record ConnectionStatus(ConnectionQuality Quality, string Reason, int? MedianMs);

public static class ConnectionRules
{
    public const int WindowSize = 10;
    public const int SlowMs = 300;

    /// <summary>Judges the newest samples (oldest first, any number; only the last 10 count).</summary>
    public static ConnectionStatus Evaluate(IReadOnlyList<HealthSample> samples)
    {
        if (samples.Count == 0) return new(ConnectionQuality.Unknown, "No connection check yet.", null);
        var last = samples.Count <= WindowSize ? samples : samples.Skip(samples.Count - WindowSize).ToList();
        var newest = last[^1];
        var failed = last.Count(s => !s.Ok);
        var oks = last.Where(s => s.Ok).Select(s => s.Ms).OrderBy(m => m).ToList();
        int? median = oks.Count == 0 ? null : oks[oks.Count / 2];

        if (newest.ErrorKind == "database_not_ready") return new(ConnectionQuality.Down, "Database not ready on the clinic server.", median);
        var run = 0;
        for (var i = last.Count - 1; i >= 0 && !last[i].Ok; i--) run++;
        if (run >= 3) return new(ConnectionQuality.Down, $"The clinic server is not answering ({run} failed checks in a row).", median);
        if (failed >= 5) return new(ConnectionQuality.Down, $"The clinic server is not answering ({failed} of the last {last.Count} checks failed).", median);
        if (failed >= 2) return new(ConnectionQuality.Unstable, $"The connection keeps dropping ({failed} of the last {last.Count} checks failed).", median);
        //JJQR - AI (Nov 23, 2026) - a single failed check only counts while it is among the 3 newest; once 3 good checks follow it, the status recovers instead of showing "Slow" for 10 checks (owner: it should update all the time).
        if (failed == 1 && last.Skip(Math.Max(0, last.Count - 3)).Any(s => !s.Ok)) return new(ConnectionQuality.Slow, "One of the last checks failed.", median);
        //JJQR - END
        if (median is { } m && m >= SlowMs) return new(ConnectionQuality.Slow, $"The clinic server is slow to answer (typically {m} ms).", median);
        return new(ConnectionQuality.Good, median is { } g ? $"The clinic server answers in {g} ms." : "The clinic server answers.", median);
    }
}

public interface IConnectionMonitor
{
    ConnectionStatus Status { get; }
    /// <summary>The last 60 checks, oldest first.</summary>
    IReadOnlyList<HealthSample> Samples { get; }
    /// <summary>Raised (any thread) after a new sample changed the list.</summary>
    event Action? Changed;
    /// <summary>The connection panel is on screen: check every 5 seconds instead of 15.</summary>
    bool PanelOpen { get; set; }
    TimeSpan CheckInterval { get; }
    void Start();
}

public sealed class NoProbeSource : IApiProbeSource
{
    public event Action<HealthSample>? ProbeCompleted { add { } remove { } }
}

public sealed class ConnectionMonitor : IConnectionMonitor, IDisposable
{
    public const int MaxSamples = 60;
    public static readonly TimeSpan PanelInterval = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(15);

    private readonly IApiConnectivity _connectivity;
    private readonly IApiProbeSource _source;
    private readonly object _gate = new();
    private readonly List<HealthSample> _samples = [];
    private volatile ConnectionStatus _status = new(ConnectionQuality.Unknown, "No connection check yet.", null);
    private CancellationTokenSource? _loop;
    private CancellationTokenSource _wake = new();
    private bool _panelOpen;

    public ConnectionMonitor(IApiConnectivity connectivity, IApiProbeSource source)
    {
        _connectivity = connectivity;
        _source = source;
        _source.ProbeCompleted += Record;
    }

    public ConnectionStatus Status => _status;
    public event Action? Changed;
    public IReadOnlyList<HealthSample> Samples { get { lock (_gate) return _samples.ToList(); } }
    public TimeSpan CheckInterval => PanelOpen ? PanelInterval : IdleInterval;

    public bool PanelOpen
    {
        get { lock (_gate) return _panelOpen; }
        set { bool changed; lock (_gate) { changed = _panelOpen != value; _panelOpen = value; } if (changed) WakeLoop(); }
    }

    /// <summary>Adds one check. Public so tests can script exact samples.</summary>
    public void Record(HealthSample sample)
    {
        lock (_gate)
        {
            _samples.Add(sample);
            if (_samples.Count > MaxSamples) _samples.RemoveRange(0, _samples.Count - MaxSamples);
            _status = ConnectionRules.Evaluate(_samples);
        }
        try { Changed?.Invoke(); } catch { /* a listener must never break the monitor */ }
    }

    private void WakeLoop() { try { _wake.Cancel(); } catch (ObjectDisposedException) { } }

    public void Start()
    {
        lock (_gate) { if (_loop is not null) return; _loop = new CancellationTokenSource(); }
        var token = _loop.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await _connectivity.CheckNowAsync(token); } catch { /* never kill the loop */ }
                TimeSpan delay;
                lock (_gate) { delay = _panelOpen ? PanelInterval : IdleInterval; _wake.Dispose(); _wake = new CancellationTokenSource(); }
                try { using var link = CancellationTokenSource.CreateLinkedTokenSource(token, _wake.Token); await Task.Delay(delay, link.Token); }
                catch (OperationCanceledException) { /* panel opened/closed, or stopped */ }
            }
        }, token);
    }

    public void Dispose()
    {
        _source.ProbeCompleted -= Record;
        lock (_gate) { _loop?.Cancel(); _loop = null; }
    }
}
//JJQR - END
