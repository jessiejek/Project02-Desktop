//JJQR - AI (Nov 21, 2026)
// Runtime-configurable cloud URL. There is no hosted backend any more: the local API is the cloud, so the default is
// the backend's normal local address. Settings (App) read/write it through ISyncEndpoint; no restart needed.
using System.Text.Json;
using ClinicNode.Data;

namespace ClinicNode.Sync;

public sealed record SyncEndpointResult(bool Succeeded, string? Error, string? BaseUrl);

public interface ISyncEndpoint
{
    /// <summary>The URL sync uses right now ("" = local-only, nothing is sent).</summary>
    string BaseUrl { get; }
    /// <summary>What <see cref="ResetAsync"/> goes back to (NodeOptions.CloudBaseUrl, else <see cref="SyncOptions.DefaultLocalBackendUrl"/>).</summary>
    string DefaultBaseUrl { get; }
    /// <summary>True when the user saved their own value (including "" for local-only).</summary>
    bool IsCustom { get; }
    /// <summary>Raised after the URL changed (new value as argument). Fired on the caller's thread.</summary>
    event Action<string>? Changed;

    /// <summary>
    /// Validates (absolute http/https URL, or "" for local-only), persists, and applies the URL immediately.
    /// Sync drops its cached node token and restarts pulling from the beginning on the new cloud.
    /// </summary>
    Task<SyncEndpointResult> SetBaseUrlAsync(string? url, CancellationToken ct = default);
    /// <summary>Forget the saved value and use the default again.</summary>
    Task<SyncEndpointResult> ResetAsync(CancellationToken ct = default);
    /// <summary>Reachability check of <paramref name="url"/> (or the current URL when null) via GET /health. Never throws, changes nothing.</summary>
    Task<bool> TestConnectionAsync(string? url = null, CancellationToken ct = default);
}

public sealed class FileSyncEndpoint : ISyncEndpoint
{
    private readonly string _path;
    private readonly SyncOptions _options;
    private readonly object _gate = new();
    private string? _custom;

    /// <summary>The file lives next to clinic-node.db so it follows the node's data folder.</summary>
    public FileSyncEndpoint(SyncOptions options, NodeOptions node)
        : this(options, Path.Combine(node.DataDirectory, "cloud-endpoint.json")) { }

    public FileSyncEndpoint(SyncOptions options, string path)
    {
        _options = options;
        _path = path;
        DefaultBaseUrl = Normalize(options.BaseUrl) ?? "";
        _custom = Load();
    }

    public string DefaultBaseUrl { get; }
    public string BaseUrl { get { lock (_gate) return _custom ?? DefaultBaseUrl; } }
    public bool IsCustom { get { lock (_gate) return _custom is not null; } }
    public event Action<string>? Changed;

    public async Task<SyncEndpointResult> SetBaseUrlAsync(string? url, CancellationToken ct = default)
    {
        if (url is null) return await ResetAsync(ct);
        var trimmed = url.Trim();
        string value;
        if (trimmed.Length == 0) value = "";
        else
        {
            var normalized = Normalize(trimmed);
            if (normalized is null)
                return new SyncEndpointResult(false, "Enter a full address such as http://localhost:5055 (http:// or https://).", BaseUrl);
            value = normalized;
        }
        await File.WriteAllTextAsync(_path, JsonSerializer.Serialize(new { base_url = value }), ct);
        return Apply(value);
    }

    public Task<SyncEndpointResult> ResetAsync(CancellationToken ct = default)
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.FromResult(Apply(null));
    }

    public async Task<bool> TestConnectionAsync(string? url = null, CancellationToken ct = default)
    {
        var target = url is null ? BaseUrl : Normalize(url);
        if (string.IsNullOrEmpty(target)) return false;
        try
        {
            using var handler = _options.HttpHandlerFactory?.Invoke();
            using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_options.PingTimeout);
            using var resp = await http.GetAsync(new Uri(new Uri(target + "/"), _options.HealthPath.TrimStart('/')), cts.Token);
            //JJQR - AI (Nov 22, 2026) - step 0.2: older server without /health/ready (404) → plain /health
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                using var legacy = await http.GetAsync(new Uri(new Uri(target + "/"), _options.LegacyHealthPath.TrimStart('/')), cts.Token);
                return (int)legacy.StatusCode < 500;
            }
            //JJQR - END
            return (int)resp.StatusCode < 500;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private SyncEndpointResult Apply(string? custom)
    {
        string before, after;
        lock (_gate)
        {
            before = _custom ?? DefaultBaseUrl;
            _custom = custom;
            after = _custom ?? DefaultBaseUrl;
        }
        if (!string.Equals(before, after, StringComparison.OrdinalIgnoreCase)) Changed?.Invoke(after);
        return new SyncEndpointResult(true, null, after);
    }

    private string? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            if (!doc.RootElement.TryGetProperty("base_url", out var v) || v.ValueKind != JsonValueKind.String) return null;
            var s = v.GetString() ?? "";
            return s.Length == 0 ? "" : Normalize(s);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Absolute http(s) URL without trailing slash, or null.</summary>
    public static string? Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps)) return null;
        return u.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
//JJQR - END
