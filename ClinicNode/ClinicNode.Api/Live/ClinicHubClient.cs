//JJQR - AI (21 Nov 2026) - item 4: one shared SignalR connection to {api}/hubs/clinic (same as Project02-fe lib/realtime/clinicHub.ts).
// Events: PatientCheckedIn { bookingId, doctorId, queueNumber, … } · QueueUpdated { bookingId, doctorId, status } · PaymentUpdated { bookingId, paymentId, status }.
// The payload is a HINT to refetch, not a patch. Groups are server-assigned from the JWT (staff / doctor:{id}).
// Reconnect: SignalR automatic reconnect + our own retry loop (1/2/5/10/30 s, forever) for a failed first start or a permanent close.
// Fallback: a 60 s tick raises Poll so screens refetch even while the hub is down. Resync is raised after every (re)connect (missed events are not replayed).
// Only ids and status are kept from a payload: patient names / codes are dropped and nothing is logged.
using System.Text.Json;
using ClinicNode.Api.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace ClinicNode.Api.Live;

public static class ClinicHubEvents
{
    public const string PatientCheckedIn = "PatientCheckedIn";
    public const string QueueUpdated = "QueueUpdated";
    public const string PaymentUpdated = "PaymentUpdated";
    public static readonly string[] All = [PatientCheckedIn, QueueUpdated, PaymentUpdated];
}

public sealed record ClinicHubEvent(string Name, Guid? BookingId, Guid? DoctorId, Guid? PaymentId, string? Status);

public enum LiveConnectionState { Stopped, Connecting, Connected, Reconnecting, Disconnected }

/// <summary>What screens subscribe to. All events arrive OFF the UI thread: marshal with MainThread before touching views.</summary>
public interface IClinicLiveEvents
{
    LiveConnectionState State { get; }
    bool IsConnected { get; }
    /// <summary>Any hub event (PatientCheckedIn, QueueUpdated, PaymentUpdated).</summary>
    event Action<ClinicHubEvent>? Received;
    /// <summary>After a (re)connect: refetch what may have been missed.</summary>
    event Action? Resync;
    /// <summary>Every 60 s: the poll fallback (always raised; cheap screens can ignore it while <see cref="IsConnected"/>).</summary>
    event Action? Poll;
    event Action<LiveConnectionState>? StateChanged;
    /// <summary>Subscribe to one event name; dispose to unsubscribe (call from OnDisappearing).</summary>
    IDisposable Subscribe(string eventName, Action<ClinicHubEvent> handler);
    /// <summary>Idempotent. Call after sign-in; the client needs an API access token.</summary>
    void Start();
    Task StopAsync();
}

public sealed class ClinicHubClient : IClinicLiveEvents, IAsyncDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private readonly ApiOptions _options;
    private readonly IApiTokenStore _tokens;
    private readonly IApiAuthClient _auth;
    private readonly IApiConnectivity _connectivity;
    private readonly ILogger<ClinicHubClient> _logger;
    private readonly object _gate = new();
    private HubConnection? _connection;
    private string? _connectionBase;
    private CancellationTokenSource? _life;
    private int _retryAttempt;
    private bool _starting;
    private LiveConnectionState _state = LiveConnectionState.Stopped;

    public ClinicHubClient(ApiOptions options, IApiTokenStore tokens, IApiAuthClient auth, IApiConnectivity connectivity, ILogger<ClinicHubClient> logger)
    {
        _options = options; _tokens = tokens; _auth = auth; _connectivity = connectivity; _logger = logger;
        _connectivity.Changed += online => { if (online && _life is not null) { _retryAttempt = 0; _ = AttemptStartAsync(); } };
    }

    public LiveConnectionState State { get { lock (_gate) return _state; } }
    public bool IsConnected => State == LiveConnectionState.Connected;
    public event Action<ClinicHubEvent>? Received;
    public event Action? Resync;
    public event Action? Poll;
    public event Action<LiveConnectionState>? StateChanged;

    public IDisposable Subscribe(string eventName, Action<ClinicHubEvent> handler)
    {
        void Filter(ClinicHubEvent e) { if (string.Equals(e.Name, eventName, StringComparison.Ordinal)) handler(e); }
        Received += Filter;
        return new Unsubscriber(() => Received -= Filter);
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_life is not null) return;
            _life = new CancellationTokenSource();
        }
        _ = PollLoopAsync(_life.Token);
        _ = AttemptStartAsync();
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? life; HubConnection? conn;
        lock (_gate) { life = _life; _life = null; conn = _connection; _connection = null; _connectionBase = null; }
        life?.Cancel();
        if (conn is not null)
        {
            try { await conn.StopAsync(); } catch (Exception) { }
            try { await conn.DisposeAsync(); } catch (Exception) { }
        }
        SetState(LiveConnectionState.Stopped);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                Raise(Poll);
                if (!IsConnected) _ = AttemptStartAsync(); // also nudges a stuck connection
            }
        }
        catch (OperationCanceledException) { }
    }

    private HubConnection? GetConnection()
    {
        var baseUrl = _options.ResolveBaseUrl();
        if (baseUrl is null) return null;
        lock (_gate)
        {
            if (_connection is not null && _connectionBase == baseUrl) return _connection;
        }
        var conn = new HubConnectionBuilder()
            .WithUrl(baseUrl + "/hubs/clinic", o =>
            {
                o.AccessTokenProvider = () => Task.FromResult(_tokens.AccessToken);
                if (_options.HttpHandlerFactory is { } f) o.HttpMessageHandlerFactory = _ => f();
            })
            .WithAutomaticReconnect()
            .Build();
        foreach (var name in ClinicHubEvents.All)
        {
            var n = name;
            conn.On<JsonElement>(n, payload => OnHubEvent(n, payload));
        }
        conn.Reconnecting += _ => { SetState(LiveConnectionState.Reconnecting); return Task.CompletedTask; };
        conn.Reconnected += _ => { SetState(LiveConnectionState.Connected); Raise(Resync); return Task.CompletedTask; };
        conn.Closed += _ => { SetState(LiveConnectionState.Disconnected); ScheduleRetry(); return Task.CompletedTask; };
        HubConnection? old;
        lock (_gate) { old = _connection; _connection = conn; _connectionBase = baseUrl; }
        if (old is not null) _ = old.DisposeAsync().AsTask();
        return conn;
    }

    private async Task AttemptStartAsync()
    {
        CancellationToken ct;
        lock (_gate)
        {
            if (_life is null || _starting) return;
            ct = _life.Token;
            _starting = true;
        }
        try
        {
            if (string.IsNullOrEmpty(_tokens.AccessToken))
            {
                // offline sign-in or not signed in yet: try a refresh once, otherwise wait for the poll tick
                try { if (_tokens.RefreshToken is null || !await _auth.TryRefreshAsync(ct)) return; }
                catch (ApiException) { return; }
            }
            var conn = GetConnection();
            if (conn is null || conn.State != HubConnectionState.Disconnected) return;
            SetState(LiveConnectionState.Connecting);
            await conn.StartAsync(ct);
            _retryAttempt = 0;
            SetState(LiveConnectionState.Connected);
            Raise(Resync); // first connect or a comeback: catch up
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized })
            {
                try { await _auth.TryRefreshAsync(ct); } catch (Exception) { }
            }
            _logger.LogDebug("Clinic hub start failed: {Error}", ex.GetType().Name);
            SetState(LiveConnectionState.Disconnected);
            ScheduleRetry();
        }
        finally
        {
            lock (_gate) _starting = false;
        }
    }

    private void ScheduleRetry()
    {
        CancellationToken ct;
        lock (_gate) { if (_life is null) return; ct = _life.Token; }
        var delay = RetryDelays[Math.Min(_retryAttempt, RetryDelays.Length - 1)];
        _retryAttempt++;
        _ = Task.Delay(delay, ct).ContinueWith(t => { if (!t.IsCanceled) _ = AttemptStartAsync(); }, TaskScheduler.Default);
    }

    private void OnHubEvent(string name, JsonElement payload)
    {
        var e = new ClinicHubEvent(name, G(payload, "bookingId"), G(payload, "doctorId"), G(payload, "paymentId"), S(payload, "status"));
        var handlers = Received;
        if (handlers is null) return;
        foreach (Action<ClinicHubEvent> h in handlers.GetInvocationList())
        {
            try { h(e); } catch (Exception ex) { _logger.LogWarning("A {Event} handler failed: {Error}", name, ex.GetType().Name); }
        }
    }

    private static JsonElement? Prop(JsonElement p, string name)
    {
        if (p.ValueKind != JsonValueKind.Object) return null;
        foreach (var x in p.EnumerateObject())
            if (string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name.Replace("_", ""), name, StringComparison.OrdinalIgnoreCase))
                return x.Value;
        return null;
    }
    private static Guid? G(JsonElement p, string name) => Prop(p, name) is { ValueKind: JsonValueKind.String } v && Guid.TryParse(v.GetString(), out var g) ? g : null;
    private static string? S(JsonElement p, string name) => Prop(p, name) is { } v ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString() : null;

    private void SetState(LiveConnectionState s)
    {
        lock (_gate) { if (_state == s) return; _state = s; }
        try { StateChanged?.Invoke(s); } catch (Exception) { }
    }

    private void Raise(Action? a)
    {
        if (a is null) return;
        foreach (Action h in a.GetInvocationList())
        {
            try { h(); } catch (Exception ex) { _logger.LogWarning("A live-refresh handler failed: {Error}", ex.GetType().Name); }
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed class Unsubscriber(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
//JJQR - END
