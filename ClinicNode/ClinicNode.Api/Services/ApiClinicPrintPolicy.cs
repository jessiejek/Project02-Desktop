//JJQR - AI (Nov 24, 2026) - the clinic-wide print settings, talking to api/print-config (see PrintConfigController in the backend):
//   GET api/print-config -> { mode, backgrounds: [{ kind, version, contentType, size }] } · GET api/print-config/backgrounds/{kind} -> the picture
//   PUT api/print-config/mode { mode } · PUT api/print-config/backgrounds/{kind} { dataBase64 } · DELETE api/print-config/backgrounds/{kind}   (the three writes are Admin only)
// Reading is for every signed-in user. This computer keeps its own copy (PrintPolicyCache) so printing works offline; the copy is refreshed
// when the app asks (after sign-in, after an Admin change) and quietly every 10 minutes while printing is used.
using System.Text.Json.Serialization;
using ClinicNode.Api.Http;
using ClinicNode.Core.Printing;

namespace ClinicNode.Api.Services;

public sealed class ApiClinicPrintPolicy : IClinicPrintPolicy, IPrintConfigAdmin
{
    sealed record BackgroundDto(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("version")] int Version);

    sealed record ConfigDto(
        [property: JsonPropertyName("mode")] string? Mode,
        [property: JsonPropertyName("backgrounds")] List<BackgroundDto>? Backgrounds,
        [property: JsonPropertyName("disabled")] List<string>? Disabled,
        [property: JsonPropertyName("labTests")] List<string>? LabTests);

    static readonly TimeSpan Stale = TimeSpan.FromMinutes(2); // an Admin change reaches a computer within about two minutes of it being used
    readonly IApiClient _api;
    readonly PrintPolicyCache _cache;
    readonly object _gate = new();
    DateTime _lastTry = DateTime.MinValue;
    bool _refreshing;

    public ApiClinicPrintPolicy(IApiClient api, string? dataDirectory)
    {
        _api = api;
        _cache = new PrintPolicyCache(dataDirectory);
    }

    public ClinicPrintMode Mode { get { RefreshIfStale(); return _cache.Mode; } }

    public string? BackgroundDataUri(ClinicPrintKind kind)
    {
        RefreshIfStale();
        var key = PrintPolicyKinds.KeyOf(kind);
        if (key is null) return null;
        var bytes = _cache.Bytes(key);
        var type = bytes is null ? null : PrintPolicyCache.ContentTypeOf(bytes);
        return bytes is null || type is null ? null : "data:" + type + ";base64," + Convert.ToBase64String(bytes);
    }

    public bool IsEnabled(ClinicPrintKind kind)
    {
        RefreshIfStale();
        return PrintPolicyKinds.KeyOf(kind) is not { } key || !_cache.IsDisabled(key);
    }

    public IReadOnlyList<string>? LabTests { get { RefreshIfStale(); return _cache.LabTests; } }

    public byte[]? BackgroundBytes(ClinicPrintKind kind) =>
        PrintPolicyKinds.KeyOf(kind) is { } key ? _cache.Bytes(key) : null;

    void RefreshIfStale()
    {
        lock (_gate)
        {
            if (_refreshing || DateTime.UtcNow - _lastTry < Stale) return;
            _lastTry = DateTime.UtcNow;
        }
        _ = Task.Run(async () =>
        {
            lock (_gate) _refreshing = true;
            try { await RefreshAsync(); }
            finally { lock (_gate) _refreshing = false; }
        });
    }

    /// <summary>Always fetches now (an Admin change must show on this computer at once).</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var cfg = await _api.GetAsync<ConfigDto>("/api/print-config", null, ct);
            if (cfg is null) return;
            var versions = new Dictionary<string, int>();
            var images = new Dictionary<string, byte[]>();
            foreach (var b in cfg.Backgrounds ?? [])
            {
                if (PrintPolicyKinds.KindOf(b.Kind) is null) continue;
                versions[b.Kind] = b.Version;
                if (_cache.Version(b.Kind) == b.Version && _cache.Bytes(b.Kind) is not null) continue; // already have this exact picture
                var file = await _api.DownloadAsync("/api/print-config/backgrounds/" + b.Kind, ct);
                if (file.Bytes.Length > 0 && PrintPolicyCache.ContentTypeOf(file.Bytes) is not null) images[b.Kind] = file.Bytes;
                else if (_cache.Bytes(b.Kind) is not null) versions[b.Kind] = _cache.Version(b.Kind); // a bad download never wipes the picture we already have
                else versions.Remove(b.Kind);
            }
            _cache.Save(PrintPolicyKinds.ParseMode(cfg.Mode), versions, images, cfg.Disabled?.Where(k => PrintPolicyKinds.KindOf(k) is not null), cfg.LabTests);
        }
        catch (Exception)
        {
            // offline, signed out or the server is busy: keep the copy this computer already has
        }
        finally { lock (_gate) _lastTry = DateTime.UtcNow; }
    }

    // ── Admin screen ──────────────────────────────────────────────────────
    public async Task<PrintConfigView?> GetAsync(CancellationToken ct = default)
    {
        try
        {
            var cfg = await _api.GetAsync<ConfigDto>("/api/print-config", null, ct);
            if (cfg is null) return null;
            var versions = new Dictionary<ClinicPrintKind, int>();
            foreach (var b in cfg.Backgrounds ?? []) if (PrintPolicyKinds.KindOf(b.Kind) is { } k) versions[k] = b.Version;
            var off = (cfg.Disabled ?? []).Select(PrintPolicyKinds.KindOf).Where(k => k is not null).Select(k => k!.Value).ToHashSet();
            return new PrintConfigView(PrintPolicyKinds.ParseMode(cfg.Mode), versions, off, cfg.LabTests is { Count: > 0 } ? cfg.LabTests : null);
        }
        catch (Exception) { return null; }
    }

    public async Task<string?> SetModeAsync(ClinicPrintMode mode, CancellationToken ct = default)
    {
        try
        {
            await _api.SendAsync(HttpMethod.Put, "/api/print-config/mode", new Dictionary<string, object?> { ["mode"] = PrintPolicyKinds.ModeKey(mode) }, null, ct);
            await RefreshAsync(ct);
            return null;
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return ex.Message; }
        catch (Exception) { return "The setting could not be saved. Check the connection and try again."; }
    }

    public async Task<string?> UploadBackgroundAsync(ClinicPrintKind kind, byte[] image, CancellationToken ct = default)
    {
        var key = PrintPolicyKinds.KeyOf(kind);
        if (key is null) return "Unknown document.";
        if (PrintPolicyCache.ContentTypeOf(image) is null) return "Only PNG or JPG pictures can be used.";
        if (image.Length > 6 * 1024 * 1024) return "The picture is too large. Use one under 6 MB.";
        try
        {
            await _api.SendAsync(HttpMethod.Put, "/api/print-config/backgrounds/" + key, new Dictionary<string, object?> { ["dataBase64"] = Convert.ToBase64String(image) }, null, ct);
            await RefreshAsync(ct);
            return null;
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return ex.Message; }
        catch (Exception) { return "The picture could not be saved. Check the connection and try again."; }
    }

    public async Task<string?> SetEnabledAsync(ClinicPrintKind kind, bool enabled, CancellationToken ct = default)
    {
        var key = PrintPolicyKinds.KeyOf(kind);
        if (key is null) return "Unknown document.";
        try
        {
            await _api.SendAsync(HttpMethod.Put, "/api/print-config/enabled/" + key, new Dictionary<string, object?> { ["enabled"] = enabled }, null, ct);
            await RefreshAsync(ct);
            return null;
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return ex.Message; }
        catch (Exception) { return "The setting could not be saved. Check the connection and try again."; }
    }

    public async Task<string?> SetLabTestsAsync(IReadOnlyList<string>? tests, CancellationToken ct = default)
    {
        try
        {
            if (tests is null) await _api.SendAsync(HttpMethod.Delete, "/api/print-config/lab-tests", null, null, ct);
            else await _api.SendAsync(HttpMethod.Put, "/api/print-config/lab-tests", new Dictionary<string, object?> { ["tests"] = tests.ToList() }, null, ct);
            await RefreshAsync(ct);
            return null;
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return ex.Message; }
        catch (Exception) { return "The list could not be saved. Check the connection and try again."; }
    }

    public async Task<string?> ClearBackgroundAsync(ClinicPrintKind kind, CancellationToken ct = default)
    {
        var key = PrintPolicyKinds.KeyOf(kind);
        if (key is null) return "Unknown document.";
        try
        {
            await _api.SendAsync(HttpMethod.Delete, "/api/print-config/backgrounds/" + key, null, null, ct);
            await RefreshAsync(ct);
            return null;
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return ex.Message; }
        catch (Exception) { return "The picture could not be removed. Check the connection and try again."; }
    }
}
//JJQR - END
