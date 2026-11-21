//JJQR - AI (Nov 21, 2026)
// HTTP implementation of ICloudSyncClient for docs/SYNC-API-CONTRACT.md v1.
// - node token from POST /api/sync/token (cached until shortly before expires_at; on 401 refresh once and retry)
// - X-Sync-Protocol: 1 on every sync call
// - maps transport errors to SyncFailureKind so the loop can tell "offline" from "server said no".
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicNode.Data;
using ClinicNode.Sync.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ClinicNode.Sync.Wire;

public interface ISyncNodeRegistration
{
    Task<bool> IsRegisteredAsync(CancellationToken ct = default);
    /// <summary>§2.1: register this node with a cloud Admin access token and store the node secret.</summary>
    Task<SyncRegistrationResult> RegisterAsync(string adminAccessToken, string name, string? platform, string? appVersion, CancellationToken ct = default);
    /// <summary>§1 handshake. Null when unreachable.</summary>
    Task<HandshakeResponseDto?> HandshakeAsync(CancellationToken ct = default);
}

public sealed record SyncRegistrationResult(bool Succeeded, Guid? NodeId, string? Error);

public sealed class HttpCloudSyncClient : ICloudSyncClient, ISyncNodeRegistration, IDisposable
{
    private readonly SyncOptions _options;
    //JJQR - AI (Nov 21, 2026)
    private readonly ISyncEndpoint _endpoint;
    private string? _tokenFor;
    //JJQR - END
    private readonly INodeCredentialStore _credentials;
    private readonly Func<CancellationToken, Task<Guid>> _localNodeId;
    private readonly HttpClient _http;
    private readonly ILogger<HttpCloudSyncClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpires;

    public HttpCloudSyncClient(
        SyncOptions options,
        INodeCredentialStore credentials,
        Func<CancellationToken, Task<Guid>> localNodeId,
        ILogger<HttpCloudSyncClient> logger,
        ISyncEndpoint? endpoint = null)
    {
        _options = options;
        //JJQR - AI (Nov 21, 2026) - URL is read per request so settings can change it without a restart
        _endpoint = endpoint ?? new FileSyncEndpoint(options, Path.Combine(Path.GetTempPath(), "clinic-node-endpoint-" + Guid.NewGuid().ToString("N") + ".json"));
        //JJQR - END
        _credentials = credentials;
        _localNodeId = localNodeId;
        _logger = logger;
        HttpMessageHandler handler = options.HttpHandlerFactory?.Invoke()
            ?? new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan, // per-request timeouts below
        };
    }

    /// <summary>DI constructor: local node id from node_identity.</summary>
    public HttpCloudSyncClient(
        SyncOptions options,
        INodeCredentialStore credentials,
        IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext> factory,
        ILogger<HttpCloudSyncClient> logger,
        ISyncEndpoint? endpoint = null)
        : this(options, credentials, async ct =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            return (await SyncOutbox.GetOrCreateIdentityAsync(db, ct)).NodeId;
        }, logger, endpoint) { }

    public bool IsConfigured => !string.IsNullOrEmpty(_endpoint.BaseUrl);

    // ── §3 push ───────────────────────────────────────────────────────────
    public async Task<SyncPushResult> PushAsync(IReadOnlyList<OutgoingOperation> batch, CancellationToken ct = default)
    {
        if (!IsConfigured) return new SyncPushResult(false, UnconfiguredCloudSyncClient.Message) { Failure = SyncFailureKind.NotConfigured };
        var body = new PushRequestDto
        {
            Operations = batch.Select(o => new PushOperationDto
            {
                OperationId = o.Entry.OperationId,
                Aggregate = o.Entry.Aggregate,
                EntityId = o.Entry.EntityId,
                OperationType = WireOperationType(o.Entry.OperationType),
                BaseRowVersion = o.BaseRowVersion,
                CreatedAt = o.Entry.CreatedAt,
                Payload = new PushPayloadDto { Data = o.Data?.DeepClone(), ChangedFields = o.ChangedFields?.ToList() },
            }).ToList(),
        };

        var (resp, failure, error) = await SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Post, Url(_options.PushPath)) { Content = JsonContent.Create(body, options: SyncWire.Json) }, ct);
        if (resp is null) return new SyncPushResult(false, error) { Failure = failure };
        using (resp)
        {
            try
            {
                var dto = await resp.Content.ReadFromJsonAsync<PushResponseDto>(SyncWire.Json, ct) ?? new PushResponseDto();
                return new SyncPushResult(true, null)
                {
                    ServerTime = dto.ServerTime,
                    Operations = (dto.Results ?? new()).Select(r => new PushOperationOutcome(r.OperationId, ParseStatus(r.Status))
                    {
                        RowVersion = r.RowVersion,
                        ReasonCode = r.ReasonCode,
                        Message = r.Message,
                        ConflictFields = r.ConflictFields,
                        Current = r.Current,
                    }).ToList(),
                };
            }
            catch (JsonException ex)
            {
                return new SyncPushResult(false, "Unreadable push response: " + ex.Message) { Failure = SyncFailureKind.Protocol };
            }
        }
    }

    // ── §4 pull ───────────────────────────────────────────────────────────
    public async Task<SyncPullResult> PullAsync(string? sinceCursor, CancellationToken ct = default)
    {
        if (!IsConfigured) return new SyncPullResult(false, sinceCursor, UnconfiguredCloudSyncClient.Message) { Failure = SyncFailureKind.NotConfigured };
        var url = $"{Url(_options.PullPath)}?since={Uri.EscapeDataString(sinceCursor ?? "")}&limit={_options.PullPageSize}&include_own=false";
        var (resp, failure, error) = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);
        if (resp is null) return new SyncPullResult(false, sinceCursor, error) { Failure = failure };
        using (resp)
        {
            try
            {
                var dto = await resp.Content.ReadFromJsonAsync<PullResponseDto>(SyncWire.Json, ct)
                    ?? throw new JsonException("empty body");
                var changes = dto.Changes.Select(c => new PulledChange(
                    c.Aggregate,
                    c.EntityId,
                    string.Equals(c.Operation, "delete", StringComparison.OrdinalIgnoreCase),
                    c.Data)
                {
                    RowVersion = c.RowVersion,
                    OriginNodeId = c.OriginNodeId,
                    ChangedAt = c.ChangedAt,
                }).ToList();
                return new SyncPullResult(true, dto.NextCursor ?? sinceCursor, null)
                {
                    Changes = changes,
                    HasMore = dto.HasMore,
                    ServerTime = dto.ServerTime,
                };
            }
            catch (JsonException ex)
            {
                return new SyncPullResult(false, sinceCursor, "Unreadable pull response: " + ex.Message) { Failure = SyncFailureKind.Protocol };
            }
        }
    }

    //JJQR - AI (21 Nov 2026) - queue blocks (sync-node token, X-Sync-Protocol: 1 via SendAuthorizedAsync). 400 invalid lane/date, 403 revoked node -> null.
    private sealed record QueueBlockRequestDto(DateOnly Date, string Lane, int Size);
    private sealed record QueueBlockResponseDto(DateOnly? Date, string? Lane, int? From, int? To, int? Size, string? First, string? Last);

    public async Task<QueueBlockReservation?> ReserveQueueBlockAsync(DateOnly date, string lane, int size, CancellationToken ct = default)
    {
        if (!IsConfigured) return null;
        var (resp, failure, error) = await SendAuthorizedAsync(
            () => new HttpRequestMessage(HttpMethod.Post, Url("/api/sync/queue-blocks"))
            { Content = JsonContent.Create(new QueueBlockRequestDto(date, lane, size), options: SyncWire.Json) }, ct);
        if (resp is null)
        {
            _logger.LogInformation("Queue block {Lane} {Date} not reserved ({Kind}).", lane, date, failure);
            return null;
        }
        using (resp)
        {
            try
            {
                var dto = await resp.Content.ReadFromJsonAsync<QueueBlockResponseDto>(SyncWire.Json, ct);
                if (dto?.From is not { } from || dto.To is not { } to || to < from) return null;
                return new QueueBlockReservation(dto.Date ?? date, string.IsNullOrEmpty(dto.Lane) ? lane : dto.Lane, from, to, dto.Size ?? (to - from + 1), dto.First, dto.Last);
            }
            catch (JsonException) { return null; }
        }
    }
    //JJQR - END

    // ── /health ───────────────────────────────────────────────────────────
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        if (!IsConfigured) return false;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_options.PingTimeout);
            using var resp = await _http.GetAsync(Url(_options.HealthPath), cts.Token);
            return (int)resp.StatusCode < 500;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Cloud health probe failed.");
            return false;
        }
    }

    // ── §2 registration / §1 handshake ───────────────────────────────────
    public async Task<bool> IsRegisteredAsync(CancellationToken ct = default) => await _credentials.GetAsync(ct) is not null;

    public async Task<SyncRegistrationResult> RegisterAsync(string adminAccessToken, string name, string? platform, string? appVersion, CancellationToken ct = default)
    {
        if (!IsConfigured) return new(false, null, UnconfiguredCloudSyncClient.Message);
        var nodeId = await _localNodeId(ct);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, Url("/api/sync/nodes"))
            {
                Content = JsonContent.Create(new RegisterNodeRequestDto { NodeId = nodeId, Name = name, Platform = platform, AppVersion = appVersion }, options: SyncWire.Json),
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAccessToken);
            using var resp = await SendRawAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return new(false, null, await DescribeAsync(resp, ct));
            var dto = await resp.Content.ReadFromJsonAsync<RegisterNodeResponseDto>(SyncWire.Json, ct);
            if (dto is null || string.IsNullOrEmpty(dto.NodeSecret)) return new(false, null, "Registration response had no node_secret.");
            await _credentials.SaveAsync(new NodeCredential(dto.NodeId, dto.NodeSecret), ct);
            InvalidateToken();
            return new(true, dto.NodeId, null);
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return new(false, null, "Cloud unreachable: " + ex.Message);
        }
    }

    public async Task<HandshakeResponseDto?> HandshakeAsync(CancellationToken ct = default)
    {
        var (resp, _, _) = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, Url("/api/sync/handshake")), ct);
        if (resp is null) return null;
        using (resp) return await resp.Content.ReadFromJsonAsync<HandshakeResponseDto>(SyncWire.Json, ct);
    }

    // ── plumbing ──────────────────────────────────────────────────────────
    private async Task<(HttpResponseMessage? resp, SyncFailureKind failure, string? error)> SendAuthorizedAsync(
        Func<HttpRequestMessage> build, CancellationToken ct)
    {
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var (token, tf, te) = await GetTokenAsync(forceRefresh: attempt > 0, ct);
                if (token is null) return (null, tf, te);
                using var req = build();
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var resp = await SendRawAsync(req, ct);
                if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    resp.Dispose();
                    InvalidateToken();
                    continue; // §2.2: get a new token and retry once
                }
                if (resp.IsSuccessStatusCode) return (resp, SyncFailureKind.None, null);
                var error = await DescribeAsync(resp, ct);
                var kind = Classify(resp.StatusCode, error);
                resp.Dispose();
                return (null, kind, error);
            }
            return (null, SyncFailureKind.Unauthorized, "Node token refused.");
        }
        catch (Exception ex) when (IsTransport(ex, ct))
        {
            return (null, SyncFailureKind.Offline, "Cloud unreachable: " + ex.Message);
        }
    }

    private async Task<(string? token, SyncFailureKind failure, string? error)> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            //JJQR - AI (Nov 21, 2026) - a token from another cloud URL is never reused
            if (!string.Equals(_tokenFor, _endpoint.BaseUrl, StringComparison.OrdinalIgnoreCase)) InvalidateToken();
            //JJQR - END
            if (!forceRefresh && _token is not null && DateTimeOffset.UtcNow < _tokenExpires)
                return (_token, SyncFailureKind.None, null);
            var cred = await _credentials.GetAsync(ct);
            if (cred is null)
                return (null, SyncFailureKind.NotRegistered, "This computer is not registered for cloud sync yet. Ask a cloud admin to register it.");
            using var req = new HttpRequestMessage(HttpMethod.Post, Url(_options.TokenPath))
            {
                Content = JsonContent.Create(new TokenRequestDto { NodeId = cred.NodeId, NodeSecret = cred.NodeSecret }, options: SyncWire.Json),
            };
            using var resp = await SendRawAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var error = await DescribeAsync(resp, ct);
                return (null, Classify(resp.StatusCode, error), error);
            }
            var dto = await resp.Content.ReadFromJsonAsync<TokenResponseDto>(SyncWire.Json, ct);
            if (dto is null || string.IsNullOrEmpty(dto.AccessToken))
                return (null, SyncFailureKind.Protocol, "Token response had no access_token.");
            _token = dto.AccessToken;
            _tokenFor = _endpoint.BaseUrl;
            var expires = dto.ExpiresAt ?? DateTimeOffset.UtcNow.AddSeconds(dto.ExpiresIn ?? 3600);
            _tokenExpires = expires - TimeSpan.FromSeconds(60);
            return (_token, SyncFailureKind.None, null);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private void InvalidateToken()
    {
        _token = null;
        _tokenExpires = DateTimeOffset.MinValue;
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage req, CancellationToken ct)
    {
        req.Headers.TryAddWithoutValidation(SyncWire.ProtocolHeader, SyncWire.ProtocolVersion.ToString());
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.RequestTimeout);
        return await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token);
    }

    //JJQR - AI (Nov 21, 2026)
    //JJQR - AI (Nov 21, 2026) - the cloud rejects verbs it does not know (invalid_operation): contract §3.1 lists these; anything else is a state snapshot = "update"
    private static readonly HashSet<string> WireVerbs = new(StringComparer.OrdinalIgnoreCase)
        { "create", "update", "upsert", "replace", "delete", "status", "cancel", "confirm", "confirm-waiver", "complete" };
    public static string WireOperationType(string? type) => type is not null && WireVerbs.Contains(type) ? type : "update";
    //JJQR - END

    private Uri Url(string path) => new(new Uri(_endpoint.BaseUrl.TrimEnd('/') + "/"), path.TrimStart('/'));
    //JJQR - END

    private static bool IsTransport(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException
        || (ex is OperationCanceledException && !ct.IsCancellationRequested) // our timeout
        || ex is IOException;

    internal static SyncFailureKind Classify(HttpStatusCode code, string? body = null)
    {
        var n = (int)code;
        if (n == 403 && body?.Contains("node_revoked", StringComparison.OrdinalIgnoreCase) == true) return SyncFailureKind.Revoked;
        return n switch
        {
            401 or 403 => SyncFailureKind.Unauthorized,
            426 => SyncFailureKind.VersionMismatch,
            404 or 408 or 429 or >= 500 => SyncFailureKind.Server, // 404: endpoint not deployed yet; retry later
            _ => SyncFailureKind.Protocol,
        };
    }

    internal static PushOperationStatus ParseStatus(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "applied" => PushOperationStatus.Applied,
        "merged" => PushOperationStatus.Merged,
        "duplicate" => PushOperationStatus.Duplicate,
        "conflict" => PushOperationStatus.Conflict,
        "retry" => PushOperationStatus.Retry,
        _ => PushOperationStatus.Rejected,
    };

    private static async Task<string> DescribeAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        string detail = "";
        try { detail = await resp.Content.ReadAsStringAsync(ct); } catch { /* ignore */ }
        if (detail.Length > 300) detail = detail[..300];
        return $"HTTP {(int)resp.StatusCode}{(detail.Length > 0 ? ": " + detail : "")}";
    }

    public void Dispose()
    {
        _http.Dispose();
        _tokenLock.Dispose();
    }
}
//JJQR - END
