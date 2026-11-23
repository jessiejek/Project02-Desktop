//JJQR - AI (Nov 22, 2026) - OTA: checks the signed update feed in the background, remembers the answer for the screens, and downloads + verifies an update on request.
// Nov 23: the Mac app is no longer sandboxed, so "Install now" downloads + verifies the signed zip and a small helper swaps the app in place and reopens it. The older ways (download and open, pasted installer) remain as fallbacks. On Windows the same service feeds the installer (see docs/DISTRIBUTION-AND-OTA-PLAN.md).
using System.Security.Cryptography;
using ClinicNode.Core.Update;

namespace ClinicNode.App.Services;

public sealed class AppUpdateService : IDisposable
{
    /// <summary>Where the signed feed lives: a small public GitHub repository (free). The signature is checked with the key below, so the host does not have to be trusted.</summary>
    public const string FeedUrl = "https://raw.githubusercontent.com/jessiejek/Project02-releases/main/latest.json";
    public const string InstallCommand = "curl -fsSL https://raw.githubusercontent.com/jessiejek/Project02-releases/main/install.sh | bash";
    // public half of the owner's update-signing key (the private half lives only in a GitHub secret and the owner's _secrets folder)
    static readonly byte[] PublicKey = Convert.FromBase64String(UpdateKeys.PublicKeyBase64);

    readonly HttpClient _http = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(10) }) { Timeout = TimeSpan.FromSeconds(60) };
    readonly UpdateChecker _checker;
    CancellationTokenSource? _cts;

    public Version Current { get; }
    public string CurrentText => Current.ToString(3);
    public UpdateInfo Last { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public event Action? Changed;

    public AppUpdateService()
    {
        Current = Version.TryParse(AppInfo.Current.VersionString, out var v) ? v : new Version(0, 0, 0);
#if WINDOWS
        //JJQR - AI (Nov 23, 2026) - owner: the Windows app said "0.1.0" after updating, so it offered the same update forever. The unpackaged Windows app takes its version label from a manifest that the installer never touches.
        // The installer now writes the version it installed (HKCU\Software\Clinic Node\Version); that is the truth, then the exe's file version, then the old label.
        if (WindowsInstalledVersion() is { } wv) Current = wv;
        //JJQR - END
#endif
#if MACCATALYST
        var key = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "mac_arm64" : "mac_x64";
#elif WINDOWS
        var key = "win_x64";
#else
        var key = "other";
#endif
        _checker = new UpdateChecker(() => _http, Environment.GetEnvironmentVariable("CLINIC_NODE_UPDATE_FEED") is { Length: > 0 } f ? f : FeedUrl, PublicKey, key, ComputerId); // the variable is for testing a feed on this computer
        Last = new UpdateInfo(UpdateStatus.NotChecked, Current);
    }

    static string ComputerId() { try { return Preferences.Default.Get("update-computer-id", "") is { Length: > 0 } id ? id : NewId(); } catch { return Environment.MachineName; } }
    static string NewId() { var id = Guid.NewGuid().ToString("N"); try { Preferences.Default.Set("update-computer-id", id); } catch { } return id; }

    /// <summary>First check 20 s after start (so start-up is never slowed), then every 6 hours.</summary>
    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { return; }
            while (!ct.IsCancellationRequested)
            {
                try { await CheckAsync(); } catch (Exception) { /* never disturb the app */ }
                try { await Task.Delay(TimeSpan.FromHours(6), ct); } catch (OperationCanceledException) { return; }
            }
        }, ct);
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        var info = await _checker.CheckAsync(Current, ct);
        // a failed check must not erase a good earlier answer
        if (info.Status is UpdateStatus.Unreachable or UpdateStatus.Error && Last.HasUpdate) { LastCheckedAt = DateTimeOffset.UtcNow; return Last; }
        Last = info; LastCheckedAt = DateTimeOffset.UtcNow;
        Changed?.Invoke();
        return info;
    }

    /// <summary>Downloads the installer into Downloads/Clinic Node, checks its SHA-256 against the signed feed, and returns the file path (null + message when it fails).</summary>
    public async Task<(string? Path, string? Error)> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default, bool preferZip = false, string? dirOverride = null)
    {
        var pkg = preferZip ? info.Package : (info.Installer ?? info.Package);
        if (pkg is null) return (null, "There is no download for this kind of computer yet.");
        try
        {
            var dir = dirOverride ?? System.IO.Path.Combine(Foundation_DownloadsDir(), "Clinic Node");
            Directory.CreateDirectory(dir);
            var file = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(new Uri(pkg.Url).AbsolutePath));
            using (var res = await _http.GetAsync(pkg.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                res.EnsureSuccessStatusCode();
                var total = res.Content.Headers.ContentLength ?? pkg.Size;
                await using var src = await res.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(file);
                var buf = new byte[81920]; long done = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0) { await dst.WriteAsync(buf.AsMemory(0, n), ct); done += n; if (total > 0) progress?.Report((double)done / total); }
            }
            await using (var fs = File.OpenRead(file))
            {
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
                if (!string.Equals(hash, pkg.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(file); } catch { }
                    return (null, "The download did not match its checksum, so it was deleted. Nothing was installed.");
                }
            }
            return (file, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UriFormatException)
        {
            return (null, "The download failed. Check the internet and try again.");
        }
    }


#if MACCATALYST
    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")] static extern int system(string command);

    /// <summary>True when this app sits in a folder the person can write to (normally /Applications), so it can replace itself.</summary>
    public static bool CanInstallInPlace()
    {
        try
        {
            var app = Foundation.NSBundle.MainBundle.BundlePath;
            var parent = System.IO.Path.GetDirectoryName(app);
            if (!app.EndsWith(".app") || string.IsNullOrEmpty(parent)) return false;
            var t = System.IO.Path.Combine(parent, ".cn-write-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(t, ""); File.Delete(t);
            return true;
        }
        catch { return false; }
    }

    /// <summary>One-click update: downloads the signed zip, checks its SHA-256, then starts a tiny helper that waits for this app to quit, swaps the app in place, and opens the new one. Returns an error message, or null when the helper has started (the caller then quits the app).</summary>
    public async Task<string?> PrepareInstallAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClinicNodeUpdate-" + Guid.NewGuid().ToString("N"));
        var (zip, error) = await DownloadAsync(info, progress, ct, preferZip: true, dirOverride: dir);
        if (zip is null) return error ?? "The download failed.";
        try
        {
            var app = Foundation.NSBundle.MainBundle.BundlePath;
            var script = System.IO.Path.Combine(dir, "swap.sh");
            File.WriteAllText(script, """
                APP="$1"; ZIP="$2"; PID="$3"; DIR="$(dirname "$ZIP")"; STAGE="$DIR/new"
                while kill -0 "$PID" 2>/dev/null; do sleep 0.5; done
                mkdir -p "$STAGE"
                /usr/bin/ditto -x -k "$ZIP" "$STAGE" || { /usr/bin/open "$APP"; exit 1; }
                NEW="$(find "$STAGE" -maxdepth 1 -name '*.app' | head -1)"
                [ -d "$NEW" ] || { /usr/bin/open "$APP"; exit 1; }
                /usr/bin/xattr -cr "$NEW"
                mv "$APP" "$APP.old" || { /usr/bin/open "$APP"; exit 1; }
                if mv "$NEW" "$APP"; then rm -rf "$APP.old"; else mv "$APP.old" "$APP"; fi
                /usr/bin/open "$APP"
                rm -rf "$DIR"
                """.Replace("\r", ""));
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            static string Q(string v) => "'" + v.Replace("'", "'\\''") + "'";
            var pid = Environment.ProcessId;
            system($"nohup /bin/bash {Q(script)} {Q(app)} {Q(zip)} {pid} >/dev/null 2>&1 &");
            return null;
        }
        catch (Exception ex) { return "Could not start the installer: " + ex.Message; }
    }
#endif

#if WINDOWS
    /// <summary>True when this copy runs from the folder the installer uses (per user, no administrator), so the installer can replace it in place.</summary>
    public static bool CanInstallInPlace()
    {
        try
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Clinic Node");
            return AppContext.BaseDirectory.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>One-click update on Windows: downloads the signed Setup.exe, checks its SHA-256, then starts it silently (no questions, no administrator prompt). The caller quits the app; the installer replaces the files and reopens Clinic Node.</summary>
    public async Task<string?> PrepareInstallAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClinicNodeUpdate-" + Guid.NewGuid().ToString("N"));
        var (setup, error) = await DownloadAsync(info, progress, ct, preferZip: false, dirOverride: dir);
        if (setup is null) return error ?? "The download failed.";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(setup, "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS") { UseShellExecute = false });
            return null;
        }
        catch (Exception ex) { return "Could not start the installer: " + ex.Message; }
    }
#endif

#if WINDOWS
    static Version? WindowsInstalledVersion()
    {
        static Version? Parse(string? text) => Version.TryParse(text, out var v) && v > new Version(0, 1, 0, 0) ? new Version(v.Major, v.Minor, Math.Max(v.Build, 0)) : null;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Clinic Node");
            if (Parse(key?.GetValue("Version") as string) is { } fromRegistry) return fromRegistry;
        }
        catch { /* fall through */ }
        try
        {
            if (Environment.ProcessPath is { Length: > 0 } exe && Parse(System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileVersion) is { } fromFile) return fromFile;
        }
        catch { /* fall through */ }
        return null;
    }
#endif

    static string Foundation_DownloadsDir()
    {
#if MACCATALYST
        var dl = Foundation.NSSearchPath.GetDirectories(Foundation.NSSearchPathDirectory.DownloadsDirectory, Foundation.NSSearchPathDomain.User, true).FirstOrDefault();
        return string.IsNullOrEmpty(dl) ? FileSystem.CacheDirectory : dl;
#else
        return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
#endif
    }

    public void Dispose() { _cts?.Cancel(); _http.Dispose(); }
}
//JJQR - END
