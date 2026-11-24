//JJQR - AI (Nov 24, 2026) - owner wishlist: the Admin chooses how the WHOLE clinic prints (PDF / native / off) and uploads a background picture (PNG/JPG) for each printed document.
// The server keeps the settings (api/print-config). Every computer keeps its OWN COPY in {DataDirectory}/print-policy so printing still works with no internet.
// "Computer" (the default) = nothing changes: each computer keeps using its own printing.json, exactly as before.
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicNode.Core.Printing;

public enum ClinicPrintMode { Computer, Pdf, Native, Off }

/// <summary>What the printer asks before every job. Never throws and never waits for the network.</summary>
public interface IClinicPrintPolicy
{
    ClinicPrintMode Mode { get; }
    /// <summary>The background picture for a document as a data: URI, or null when none is set.</summary>
    string? BackgroundDataUri(ClinicPrintKind kind);
    /// <summary>False when the Admin switched printing off for this one document.</summary>
    bool IsEnabled(ClinicPrintKind kind);
    /// <summary>Fetch the latest settings from the server into this computer's copy. Best effort: offline or any error keeps the copy we have.</summary>
    Task RefreshAsync(CancellationToken ct = default);
}

/// <summary>What the Admin screen needs: read and change the clinic-wide settings (online only).</summary>
public interface IPrintConfigAdmin
{
    Task<PrintConfigView?> GetAsync(CancellationToken ct = default);
    Task<string?> SetModeAsync(ClinicPrintMode mode, CancellationToken ct = default);
    /// <summary>Null = saved; otherwise the message to show.</summary>
    Task<string?> UploadBackgroundAsync(ClinicPrintKind kind, byte[] image, CancellationToken ct = default);
    Task<string?> ClearBackgroundAsync(ClinicPrintKind kind, CancellationToken ct = default);
    Task<string?> SetEnabledAsync(ClinicPrintKind kind, bool enabled, CancellationToken ct = default);
    /// <summary>The picture currently saved on this computer for a document (for the preview), or null.</summary>
    byte[]? BackgroundBytes(ClinicPrintKind kind);
}

public sealed record PrintConfigView(ClinicPrintMode Mode, IReadOnlyDictionary<ClinicPrintKind, int> BackgroundVersions, IReadOnlySet<ClinicPrintKind>? DisabledKinds = null)
{
    public bool IsEnabled(ClinicPrintKind kind) => DisabledKinds is null || !DisabledKinds.Contains(kind);
}

/// <summary>Default when nothing is registered (tests, first start): each computer decides, no backgrounds.</summary>
public sealed class NoPrintPolicy : IClinicPrintPolicy
{
    public ClinicPrintMode Mode => ClinicPrintMode.Computer;
    public string? BackgroundDataUri(ClinicPrintKind kind) => null;
    public bool IsEnabled(ClinicPrintKind kind) => true;
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public static class PrintPolicyKinds
{
    /// <summary>The five documents, in the order they are shown, with the server's name for each.</summary>
    public static readonly IReadOnlyList<(ClinicPrintKind Kind, string Key, string Label)> All =
    [
        (ClinicPrintKind.Prescription, "prescription", "Prescription"),
        (ClinicPrintKind.LabRequest, "lab_request", "Lab request"),
        (ClinicPrintKind.MedicalCertificate, "medical_certificate", "Medical certificate"),
        (ClinicPrintKind.QueueSlip, "queue_slip", "Queue slip"),
        (ClinicPrintKind.Receipt, "receipt", "Official receipt"),
    ];

    public static string? KeyOf(ClinicPrintKind kind) => All.FirstOrDefault(k => k.Kind == kind).Key;

    public static ClinicPrintKind? KindOf(string key) => All.Where(k => k.Key == key).Select(k => (ClinicPrintKind?)k.Kind).FirstOrDefault();

    public static ClinicPrintMode ParseMode(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "pdf" => ClinicPrintMode.Pdf,
        "native" => ClinicPrintMode.Native,
        "off" => ClinicPrintMode.Off,
        _ => ClinicPrintMode.Computer,
    };

    public static string ModeKey(ClinicPrintMode m) => m switch
    {
        ClinicPrintMode.Pdf => "pdf",
        ClinicPrintMode.Native => "native",
        ClinicPrintMode.Off => "off",
        _ => "computer",
    };
}

/// <summary>This computer's copy of the clinic print settings: policy.json plus one picture file per document.</summary>
public sealed class PrintPolicyCache
{
    sealed class State
    {
        [JsonPropertyName("mode")] public string Mode { get; set; } = "computer";
        [JsonPropertyName("versions")] public Dictionary<string, int> Versions { get; set; } = new();
        [JsonPropertyName("disabled")] public List<string> Disabled { get; set; } = new();
    }

    readonly string _dir;
    readonly object _gate = new();
    State _state = new();
    readonly Dictionary<string, byte[]?> _bytes = new();
    bool _loaded;

    public PrintPolicyCache(string? dataDirectory) =>
        _dir = string.IsNullOrWhiteSpace(dataDirectory) ? "" : Path.Combine(dataDirectory, "print-policy");

    void Load()
    {
        if (_loaded) return;
        _loaded = true;
        if (_dir.Length == 0) return;
        try
        {
            var path = Path.Combine(_dir, "policy.json");
            if (File.Exists(path)) _state = JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State();
        }
        catch { _state = new State(); }
    }

    public ClinicPrintMode Mode { get { lock (_gate) { Load(); return PrintPolicyKinds.ParseMode(_state.Mode); } } }

    public bool IsDisabled(string key) { lock (_gate) { Load(); return _state.Disabled.Contains(key); } }

    public int Version(string key) { lock (_gate) { Load(); return _state.Versions.TryGetValue(key, out var v) ? v : 0; } }

    public byte[]? Bytes(string key)
    {
        lock (_gate)
        {
            Load();
            if (!_state.Versions.ContainsKey(key) || _dir.Length == 0) return null;
            if (_bytes.TryGetValue(key, out var cached)) return cached;
            try
            {
                var f = Path.Combine(_dir, "bg-" + key + ".bin");
                return _bytes[key] = File.Exists(f) ? File.ReadAllBytes(f) : null;
            }
            catch { return _bytes[key] = null; }
        }
    }

    /// <summary>Replace the saved copy with what the server just said. <paramref name="images"/> holds only the pictures that changed.</summary>
    public void Save(ClinicPrintMode mode, IReadOnlyDictionary<string, int> serverVersions, IReadOnlyDictionary<string, byte[]> images, IEnumerable<string>? disabled = null)
    {
        lock (_gate)
        {
            Load();
            var next = new State { Mode = PrintPolicyKinds.ModeKey(mode), Versions = new Dictionary<string, int>(serverVersions), Disabled = (disabled ?? []).ToList() };
            if (_dir.Length > 0)
            {
                try
                {
                    Directory.CreateDirectory(_dir);
                    foreach (var (key, data) in images) File.WriteAllBytes(Path.Combine(_dir, "bg-" + key + ".bin"), data);
                    foreach (var k in PrintPolicyKinds.All)
                        if (!serverVersions.ContainsKey(k.Key)) { var f = Path.Combine(_dir, "bg-" + k.Key + ".bin"); if (File.Exists(f)) File.Delete(f); }
                    File.WriteAllText(Path.Combine(_dir, "policy.json"), JsonSerializer.Serialize(next));
                }
                catch { /* the copy is a convenience: in memory is still right for this run */ }
            }
            _state = next;
            _bytes.Clear();
            foreach (var (key, data) in images) _bytes[key] = data;
        }
    }

    public static string? ContentTypeOf(byte[] d) =>
        d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 ? "image/png"
        : d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF ? "image/jpeg" : null;
}

/// <summary>Puts the clinic's background picture behind a printed document.</summary>
public static class PrintBackground
{
    public static PrintDocument Apply(PrintDocument doc, string dataUri)
    {
        // the clinic forms (prescription, lab request, medical certificate) draw their paper as a ".form" sheet: the picture goes ON that sheet.
        // Everything else (slip, receipt) gets the picture on a full-page layer behind the content.
        var style = "<style>.form{background:#fff url(" + dataUri + ") no-repeat 0 0 / 100% 100% !important;-webkit-print-color-adjust:exact;print-color-adjust:exact}" +
                    ".write-lbl{background:transparent !important}</style>";
        var layer = doc.Html.Contains("class=\"form", StringComparison.Ordinal) ? ""
            : "<div style=\"position:fixed;left:0;top:0;width:100%;height:100%;z-index:-1;background:url(" + dataUri + ") no-repeat 0 0 / 100% 100%;-webkit-print-color-adjust:exact;print-color-adjust:exact\"></div>";
        var html = doc.Html;
        var head = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        html = head >= 0 ? html.Insert(head, style) : style + html;
        var body = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (layer.Length > 0 && body >= 0)
        {
            var end = html.IndexOf('>', body);
            if (end >= 0) html = html.Insert(end + 1, layer);
        }
        return (doc with { Html = html }).WithInfo(doc.Info());
    }
}
//JJQR - END
