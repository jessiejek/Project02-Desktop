//JJQR - AI (Nov 21, 2026) - typed HTTP client for the clinic API (the C# twin of Project02-fe lib/api/client.ts): Bearer token, one transparent refresh on 401,
// ProblemDetails / { message } → typed exceptions, network / timeout / 502-504 → ApiOfflineException (and the connectivity monitor is told).
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClinicNode.Api.Http;

public interface IApiClient
{
    /// <summary>GET; null body (204 / empty) → default.</summary>
    Task<T?> GetAsync<T>(string path, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default);
    Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default);
    Task SendAsync(HttpMethod method, string path, object? body = null, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default);
}

public sealed class ApiClient : IApiClient, IDisposable
{
    private readonly ApiOptions _options;
    private readonly IApiTokenStore _tokens;
    private readonly IApiAuthClient _auth;
    private readonly IApiConnectivity? _connectivity;
    private readonly HttpClient _http;

    public ApiClient(ApiOptions options, IApiTokenStore tokens, IApiAuthClient auth, IApiConnectivity? connectivity = null)
    {
        _options = options;
        _tokens = tokens;
        _auth = auth;
        _connectivity = connectivity;
        _http = new HttpClient(options.HttpHandlerFactory?.Invoke() ?? new SocketsHttpHandler { ConnectTimeout = options.RequestTimeout }, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public Task<T?> GetAsync<T>(string path, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default) =>
        SendAsync<T>(HttpMethod.Get, path, null, query, ct);

    public async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        var text = await SendCoreAsync(method, path, body, query, ct);
        if (string.IsNullOrWhiteSpace(text)) return default;
        try { return JsonSerializer.Deserialize<T>(text, ApiJson.Data); }
        catch (JsonException ex) { throw new ApiException(200, "The server sent an answer this app could not read.", text, ex); }
    }

    public async Task SendAsync(HttpMethod method, string path, object? body = null, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default) =>
        _ = await SendCoreAsync(method, path, body, query, ct);

    private async Task<string> SendCoreAsync(HttpMethod method, string path, object? body, IReadOnlyDictionary<string, string?>? query, CancellationToken ct)
    {
        var baseUrl = _options.ResolveBaseUrl();
        if (baseUrl is null) throw new ApiOfflineException("No clinic server address is configured.");
        var uri = Build(baseUrl, path, query);
        var json = body is null ? null : JsonSerializer.Serialize(body, ApiJson.Data);

        //JJQR - AI (Nov 22, 2026) - stale-form protection: saves of an open visit carry the version we last saw and run one at a time (see VisitVersions)
        var visit = VisitVersions.For(path, query);
        var isWrite = method == HttpMethod.Put || method == HttpMethod.Delete;
        if (visit is not null && isWrite) await visit.Value.Session.Gate.WaitAsync(ct);
        try
        {
            var sentVersion = isWrite ? visit?.Session.Version : null;
            var res = await AttemptAsync(method, uri, json, ct, sentVersion);
            if (res.Status == HttpStatusCode.Unauthorized && _tokens.RefreshToken is not null && await _auth.TryRefreshAsync(ct))
                res = await AttemptAsync(method, uri, json, ct, sentVersion); // one retry with the fresh token
            VisitVersions.Remember(visit?.BookingId, visit?.Session, method, (int)res.Status is >= 200 and < 300, (int)res.Status, res.Version, res.BookingHeader, res.ConsultationHeader);
            if ((int)res.Status is >= 200 and < 300)
            {
                _connectivity?.ReportSuccess();
                return res.Text;
            }
            throw Map(res.Status, res.Text);
        }
        finally
        {
            if (visit is not null && isWrite) visit.Value.Session.Gate.Release();
        }
        //JJQR - END
    }

    private sealed record Raw(HttpStatusCode Status, string Text, string? Version = null, string? BookingHeader = null, string? ConsultationHeader = null);

    private async Task<Raw> AttemptAsync(HttpMethod method, Uri uri, string? json, CancellationToken ct, string? visitVersion = null)
    {
        using var req = new HttpRequestMessage(method, uri);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(visitVersion)) req.Headers.TryAddWithoutValidation(VisitVersions.VersionHeader, visitVersion); //JJQR - AI (Nov 22, 2026) //JJQR - END
        if (_tokens.AccessToken is { Length: > 0 } t) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.RequestTimeout);
        try
        {
            using var res = await _http.SendAsync(req, HttpCompletionOption.ResponseContentRead, timeout.Token);
            var text = await res.Content.ReadAsStringAsync(timeout.Token);
            static string? H(HttpResponseMessage r, string n) => r.Headers.TryGetValues(n, out var v) ? v.FirstOrDefault() : null;
            return new Raw(res.StatusCode, text, H(res, VisitVersions.VersionHeader), H(res, VisitVersions.BookingHeader), H(res, VisitVersions.ConsultationHeader));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _connectivity?.ReportNetworkFailure();
            throw new ApiOfflineException("The clinic server took too long to answer.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Net.Sockets.SocketException)
        {
            _connectivity?.ReportNetworkFailure();
            throw new ApiOfflineException("The clinic server could not be reached.", ex);
        }
    }

    internal static Uri Build(string baseUrl, string path, IReadOnlyDictionary<string, string?>? query)
    {
        var sb = new StringBuilder(path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : baseUrl + (path.StartsWith('/') ? path : "/" + path));
        var first = !sb.ToString().Contains('?');
        foreach (var (k, v) in query ?? new Dictionary<string, string?>())
        {
            if (v is null) continue;
            sb.Append(first ? '?' : '&').Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v));
            first = false;
        }
        return new Uri(sb.ToString());
    }

    /// <summary>ProblemDetails / validation / { message } / { error } → a typed exception.</summary>
    internal static ApiException Map(HttpStatusCode status, string text)
    {
        var code = (int)status;
        var (message, errors) = Describe(text);
        message ??= code switch
        {
            401 => "Your session has expired. Sign in again.",
            403 => "You do not have permission to do that.",
            404 => "That could not be found.",
            409 => "That conflicts with a change that was already made.",
            _ => $"The server answered with an error (HTTP {code}).",
        };
        return code switch
        {
            401 => new ApiUnauthorizedException(message, text),
            403 => new ApiForbiddenException(message, text),
            404 => new ApiNotFoundException(message, text),
            409 => new ApiConflictException(message, text),
            400 or 422 => new ApiValidationException(code, message, errors, text),
            502 or 503 or 504 => new ApiOfflineException($"The clinic server is not available (HTTP {code})."),
            _ => new ApiServerException(code, message, text),
        };
    }

    private static (string? Message, IReadOnlyDictionary<string, string[]> Errors) Describe(string text)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(text)) return (null, errors);
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return (null, errors);
            var root = doc.RootElement;
            if (root.TryGetProperty("errors", out var e) && e.ValueKind == JsonValueKind.Object)
                foreach (var p in e.EnumerateObject())
                    errors[p.Name] = p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Select(x => x.ToString()).ToArray() : [p.Value.ToString()];
            string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;
            var first = errors.Values.SelectMany(x => x).FirstOrDefault(s => !string.IsNullOrEmpty(s));
            return (Str("message") ?? first ?? Str("detail") ?? Str("title") ?? Str("error"), errors);
        }
        catch (JsonException) { return (null, errors); }
    }

    public void Dispose() => _http.Dispose();
}
//JJQR - END
