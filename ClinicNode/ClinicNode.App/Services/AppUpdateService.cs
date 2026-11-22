//JJQR - AI (Nov 22, 2026) - OTA: checks the signed update feed in the background, remembers the answer for the screens, and downloads + verifies an update on request.
// The app is sandboxed (Apple's safety box): it can download and CHECK an update but cannot overwrite itself, so after the download it opens the file and the person drags it to Applications
// (or pastes the one-line installer, which needs no warning). On Windows the same service feeds the installer (see docs/DISTRIBUTION-AND-OTA-PLAN.md).
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
    public async Task<(string? Path, string? Error)> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var pkg = info.Installer ?? info.Package;
        if (pkg is null) return (null, "There is no download for this kind of computer yet.");
        try
        {
            var dir = System.IO.Path.Combine(Foundation_DownloadsDir(), "Clinic Node");
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
