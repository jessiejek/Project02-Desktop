//JJQR - AI (Nov 21, 2026) - JWT login / refresh / logout / me against the real /api/auth endpoints (camelCase wire, same as Project02-fe lib/auth/dotnet.ts).
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClinicNode.Api.Http;

public sealed record ApiUser(Guid Id, string FullName, string Email, string Role, string? AvatarUrl, bool IsFirstLogin, string? PhoneNumber);
public sealed record ApiSession(string AccessToken, string RefreshToken, ApiUser User);

public interface IApiAuthClient
{
    /// <summary>Signs in against the server and keeps the token pair. Wrong password → ApiUnauthorizedException; unreachable → ApiOfflineException.</summary>
    Task<ApiSession> LoginAsync(string email, string password, CancellationToken ct = default);
    /// <summary>Exchanges the refresh token for a new pair. false when there is none or the server refused it (the tokens are then cleared).</summary>
    Task<bool> TryRefreshAsync(CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task<ApiUser?> MeAsync(CancellationToken ct = default);
}

public sealed class ApiAuthClient : IApiAuthClient, IDisposable
{
    private readonly ApiOptions _options;
    private readonly IApiTokenStore _tokens;
    private readonly IApiConnectivity? _connectivity;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    public ApiAuthClient(ApiOptions options, IApiTokenStore tokens, IApiConnectivity? connectivity = null)
    {
        _options = options;
        _tokens = tokens;
        _connectivity = connectivity;
        _http = new HttpClient(options.HttpHandlerFactory?.Invoke() ?? new SocketsHttpHandler { ConnectTimeout = options.RequestTimeout }, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<ApiSession> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var text = await PostAsync("/api/auth/login", new { email, password }, bearer: false, ct);
        var session = Parse<ApiSession>(text) ?? throw new ApiException(200, "The server sent an answer this app could not read.", text);
        _tokens.Set(session.AccessToken, session.RefreshToken);
        _connectivity?.ReportSuccess();
        return session;
    }

    public async Task<bool> TryRefreshAsync(CancellationToken ct = default)
    {
        var before = _tokens.AccessToken;
        await _refreshGate.WaitAsync(ct);
        try
        {
            if (_tokens.AccessToken != before) return true; // another caller already refreshed
            var refresh = _tokens.RefreshToken;
            if (string.IsNullOrEmpty(refresh)) return false;
            try
            {
                var text = await PostAsync("/api/auth/refresh-token", new { refreshToken = refresh }, bearer: false, ct);
                var pair = Parse<RefreshPair>(text);
                if (pair is null || string.IsNullOrEmpty(pair.AccessToken)) { _tokens.Clear(); return false; }
                _tokens.Set(pair.AccessToken, pair.RefreshToken);
                return true;
            }
            catch (ApiOfflineException) { throw; }
            catch (ApiException) { _tokens.Clear(); return false; }
        }
        finally { _refreshGate.Release(); }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var refresh = _tokens.RefreshToken;
        _tokens.Clear();
        if (string.IsNullOrEmpty(refresh)) return;
        try { await PostAsync("/api/auth/logout", new { refreshToken = refresh }, bearer: false, ct); }
        catch (ApiException) { /* signing out locally always works; a stale server token just expires */ }
    }

    public async Task<ApiUser?> MeAsync(CancellationToken ct = default)
    {
        var text = await SendAsync(HttpMethod.Get, "/api/auth/me", null, bearer: true, ct);
        return Parse<ApiUser>(text);
    }

    private sealed record RefreshPair(string AccessToken, string? RefreshToken);

    private static T? Parse<T>(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return default;
        try { return JsonSerializer.Deserialize<T>(text, ApiJson.Auth); }
        catch (JsonException) { return default; }
    }

    private Task<string> PostAsync(string path, object body, bool bearer, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body, ApiJson.Auth), bearer, ct);

    private async Task<string> SendAsync(HttpMethod method, string path, string? json, bool bearer, CancellationToken ct)
    {
        var baseUrl = _options.ResolveBaseUrl() ?? throw new ApiOfflineException("No clinic server address is configured.");
        using var req = new HttpRequestMessage(method, baseUrl + path);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (bearer && _tokens.AccessToken is { Length: > 0 } t) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.RequestTimeout);
        HttpStatusCode status; string text;
        try
        {
            using var res = await _http.SendAsync(req, timeout.Token);
            status = res.StatusCode;
            text = await res.Content.ReadAsStringAsync(timeout.Token);
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
        if ((int)status is >= 200 and < 300) return text;
        throw ApiClient.Map(status, text);
    }

    public void Dispose() => _http.Dispose();
}
//JJQR - END
