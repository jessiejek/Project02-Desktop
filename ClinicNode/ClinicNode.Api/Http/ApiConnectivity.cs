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

//JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.2: what the last health check found (API answering, database answering, how long it took). Used by the connection monitor (Phase 1).
public interface IApiHealthDetail
{
    /// <summary>null until the first check; false when the API answered but reported its database as not ready; null when the server is an older build without /health/ready.</summary>
    bool? DatabaseReady { get; }
    /// <summary>Round trip of the last check in milliseconds (0 before the first check).</summary>
    int LastCheckMs { get; }
}
//JJQR - END

//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.3: every health check leaves one sample the connection monitor can keep
public sealed record HealthSample(DateTimeOffset At, int Ms, int? HttpStatus, bool? DatabaseOk, string? ErrorKind)
{
    /// <summary>The server answered AND its database is ready.</summary>
    public bool Ok => ErrorKind is null;
}

public interface IApiProbeSource
{
    /// <summary>Raised after every health check (any thread), whoever asked for it.</summary>
    event Action<HealthSample>? ProbeCompleted;
}
//JJQR - END

public sealed class ApiConnectivity : IApiConnectivity, IApiHealthDetail, IApiProbeSource, IDisposable
{
    private readonly ApiOptions _options;
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private bool? _online;
    private bool? _databaseReady; //JJQR - AI (Nov 22, 2026) - step 0.2 //JJQR - END
    private int _lastCheckMs;
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
    public bool? DatabaseReady { get { lock (_gate) return _databaseReady; } }
    public int LastCheckMs { get { lock (_gate) return _lastCheckMs; } }
    public event Action<bool>? Changed;
    public event Action<HealthSample>? ProbeCompleted; //JJQR - AI (Nov 22, 2026) - step 1.3 //JJQR - END

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
                var sw = System.Diagnostics.Stopwatch.StartNew();
                bool? dbReady = null;
                int? status = null; string? errorKind = null; //JJQR - AI (Nov 22, 2026) - step 1.3 //JJQR - END
                try
                {
                    //JJQR - AI (Nov 22, 2026) - step 0.2: "online" now means the API answers AND its database is ready (/health/ready: 503 = database not answering).
                    // An older server without that route (404) falls back to the plain /health, and then the database state is simply unknown.
                    using var res = await _http.GetAsync(baseUrl + "/health/ready", cts.Token);
                    status = (int)res.StatusCode;
                    if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        using var legacy = await _http.GetAsync(baseUrl + "/health", cts.Token);
                        ok = legacy.IsSuccessStatusCode;
                        status = (int)legacy.StatusCode;
                        if (!ok) errorKind = "http_" + status;
                    }
                    else
                    {
                        ok = res.IsSuccessStatusCode;
                        dbReady = ok;
                        if (!ok) errorKind = res.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ? "database_not_ready" : "http_" + status;
                    }
                    //JJQR - END
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
                { ok = false; errorKind = ex is HttpRequestException ? "unreachable" : "timeout"; }
                sw.Stop();
                lock (_gate) { _databaseReady = dbReady; _lastCheckMs = (int)sw.ElapsedMilliseconds; }
                try { ProbeCompleted?.Invoke(new HealthSample(DateTimeOffset.UtcNow, (int)sw.ElapsedMilliseconds, status, dbReady, ok ? null : errorKind ?? "unreachable")); } catch { /* a listener must never break the check */ }
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
