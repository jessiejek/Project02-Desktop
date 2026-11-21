//JJQR - AI (Nov 21, 2026) - connectivity monitor: pings GET {base}/health with a short timeout, raises Changed on online/offline flips, backs off while offline.
// The API client also reports failures / successes of real calls, so an unplugged cable is noticed on the next request, not at the next ping.
namespace ClinicNode.Api.Http;

public interface IApiConnectivity
{
    /// <summary>Last known state. false until the first check succeeds.</summary>
    bool IsOnline { get; }
    event Action<bool>? Changed;
    /// <summary>Runs the first check when the state is still unknown, then returns <see cref="IsOnline"/>.</summary>
    Task<bool> EnsureCheckedAsync(CancellationToken ct = default);
    Task<bool> CheckNowAsync(CancellationToken ct = default);
    void ReportNetworkFailure();
    void ReportSuccess();
    /// <summary>Starts the background loop (idempotent). Safe to call at app start.</summary>
    void Start();
}

public sealed class ApiConnectivity : IApiConnectivity, IDisposable
{
    private readonly ApiOptions _options;
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private bool? _online;
    private int _failures;
    private Task<bool>? _inflight;
    private CancellationTokenSource? _loop;
    private CancellationTokenSource _wake = new();

    public ApiConnectivity(ApiOptions options)
    {
        _options = options;
        _http = new HttpClient(options.HttpHandlerFactory?.Invoke() ?? new SocketsHttpHandler { ConnectTimeout = options.PingTimeout }, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public bool IsOnline { get { lock (_gate) return _online == true; } }
    public event Action<bool>? Changed;

    public async Task<bool> EnsureCheckedAsync(CancellationToken ct = default)
    {
        bool? known;
        lock (_gate) known = _online;
        return known ?? await CheckNowAsync(ct);
    }

    public Task<bool> CheckNowAsync(CancellationToken ct = default)
    {
        TaskCompletionSource<bool> tcs;
        lock (_gate)
        {
            if (_inflight is not null) return _inflight; // single flight: concurrent callers share one ping
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inflight = tcs.Task;
        }
        _ = RunCheckAsync(tcs);
        return tcs.Task;
    }

    private async Task RunCheckAsync(TaskCompletionSource<bool> tcs)
    {
        var ok = false;
        try
        {
            var baseUrl = _options.ResolveBaseUrl();
            if (baseUrl is not null)
            {
                using var cts = new CancellationTokenSource(_options.PingTimeout);
                try
                {
                    using var res = await _http.GetAsync(baseUrl + "/health", cts.Token);
                    ok = res.IsSuccessStatusCode;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException) { ok = false; }
            }
            Set(ok);
        }
        catch (Exception ex) { lock (_gate) _inflight = null; tcs.TrySetException(ex); return; }
        lock (_gate) _inflight = null;
        tcs.TrySetResult(ok);
    }

    private void Set(bool online)
    {
        bool changed;
        lock (_gate)
        {
            changed = _online != online;
            _online = online;
            _failures = online ? 0 : _failures + 1;
        }
        if (changed) Changed?.Invoke(online);
    }

    public void ReportNetworkFailure() { Set(false); WakeLoop(); }
    public void ReportSuccess() { bool? was; lock (_gate) was = _online; if (was != true) Set(true); }

    private void WakeLoop() { try { _wake.Cancel(); } catch (ObjectDisposedException) { } }

    public void Start()
    {
        lock (_gate)
        {
            if (_loop is not null) return;
            _loop = new CancellationTokenSource();
        }
        var token = _loop.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try { await CheckNowAsync(token); } catch { /* never kill the loop */ }
                TimeSpan delay;
                lock (_gate)
                {
                    delay = _online == true ? _options.OnlineInterval
                        : _options.OfflineBackoff[Math.Clamp(_failures - 1, 0, _options.OfflineBackoff.Length - 1)];
                    _wake.Dispose();
                    _wake = new CancellationTokenSource();
                }
                try { using var link = CancellationTokenSource.CreateLinkedTokenSource(token, _wake.Token); await Task.Delay(delay, link.Token); }
                catch (OperationCanceledException) { /* woken early or stopped */ }
            }
        }, token);
    }

    public void Dispose()
    {
        lock (_gate) { _loop?.Cancel(); _loop = null; }
        _http.Dispose();
    }
}
//JJQR - END
