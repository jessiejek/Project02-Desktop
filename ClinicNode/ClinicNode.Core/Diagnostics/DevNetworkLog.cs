//JJQR - AI (Nov 22, 2026) - owner request: a developer log of every call the app makes to the clinic server (the native app has no browser "Network" tab).
// ON in Debug builds, or when CLINIC_NODE_DEV_LOG=1; OFF in a normal release build. Memory only (the last 400 calls), never written to disk.
// It records the time, the area (api / sync), method, address, status, how long it took and, for a failed call, a short error text.
// It never records passwords, tokens, headers or request bodies. A failed call's response text is cut to 300 characters.
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace ClinicNode.Core.Diagnostics;

public sealed record DevNetworkEntry(DateTimeOffset At, string Area, string Method, string Url, int Status, long Ms, string? Error);

/// <summary>How many calls to the clinic server are waiting for an answer right now (background health checks and sync are not counted). The top bar shows a thin progress line while this is above 0.</summary>
public static class ApiActivity
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, long> Started = new();
    private static long _next;
    public static int InFlight => Started.Count;
    public static event Action? Changed;

    /// <summary>How long the OLDEST waiting call has been waiting. The top bar shows its line only when ONE call is slow (more than 1.5 s), so quick background checks and refreshes never light it up.</summary>
    public static TimeSpan OldestWait()
    {
        long oldest = long.MaxValue;
        foreach (var v in Started.Values) if (v < oldest) oldest = v;
        return oldest == long.MaxValue ? TimeSpan.Zero : Stopwatch.GetElapsedTime(oldest);
    }

    internal static long Begin() { var id = Interlocked.Increment(ref _next); Started[id] = Stopwatch.GetTimestamp(); Changed?.Invoke(); return id; }
    internal static void End(long id) { Started.TryRemove(id, out _); Changed?.Invoke(); }
}

public static class DevNetworkLog
{
    public const int Max = 400;
    private static readonly ConcurrentQueue<DevNetworkEntry> Queue = new();

    public static bool Enabled { get; set; } =
#if DEBUG
        true;
#else
        string.Equals(Environment.GetEnvironmentVariable("CLINIC_NODE_DEV_LOG"), "1", StringComparison.Ordinal);
#endif

    public static event Action? Changed;

    public static void Add(DevNetworkEntry e)
    {
        if (!Enabled) return;
        Queue.Enqueue(e);
        while (Queue.Count > Max && Queue.TryDequeue(out _)) { }
        Changed?.Invoke();
    }

    /// <summary>Newest first.</summary>
    public static IReadOnlyList<DevNetworkEntry> Snapshot() => Queue.Reverse().ToList();

    public static void Clear() { Queue.Clear(); Changed?.Invoke(); }

    public static string Line(DevNetworkEntry e, TimeSpan manilaOffset)
    {
        var t = e.At.ToOffset(manilaOffset).ToString("HH:mm:ss");
        var status = e.Status == 0 ? "NO ANSWER" : e.Status.ToString();
        return $"{t}  {e.Area,-4}  {e.Method,-6}  {status,-9}  {e.Ms,5} ms  {e.Url}" + (string.IsNullOrEmpty(e.Error) ? "" : "  | " + e.Error);
    }

    public static string AsText(TimeSpan manilaOffset)
    {
        var sb = new StringBuilder();
        foreach (var e in Snapshot()) sb.AppendLine(Line(e, manilaOffset));
        return sb.ToString();
    }
}

/// <summary>Lets several HttpClients share ONE connection pool. Disposing an HttpClient normally disposes its handler, which closed the shared pool for everybody ("Cannot access a disposed object: SocketsHttpHandler" at sign-in). This wrapper swallows the dispose; the shared pool lives as long as the app.</summary>
public sealed class SharedHandlerLease : DelegatingHandler
{
    public SharedHandlerLease(HttpMessageHandler shared) : base(shared) { }
    protected override void Dispose(bool disposing) { /* never dispose the shared handler */ }
}

/// <summary>Wraps the real handler and records each call. Health checks that succeed are not recorded (they run every few seconds and would bury everything else).</summary>
public sealed class DevLogHandler : DelegatingHandler
{
    private readonly string _area;
    public DevLogHandler(string area, HttpMessageHandler inner) : base(inner) => _area = area;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var uri = request.RequestUri;
        var url = uri is null ? "" : uri.PathAndQuery.Length > 160 ? uri.PathAndQuery[..160] + "..." : uri.PathAndQuery;
        var host = uri?.Host ?? "";
        var isHealth = url.StartsWith("/health", StringComparison.OrdinalIgnoreCase);
        var counted = _area == "api" && !isHealth && !url.StartsWith("/hubs", StringComparison.OrdinalIgnoreCase);
        var activityId = counted ? ApiActivity.Begin() : 0;
        try
        {
            var res = await base.SendAsync(request, ct).ConfigureAwait(false);
            string? error = null;
            if ((int)res.StatusCode >= 400)
            {
                try
                {
                    var body = res.Content is null ? "" : await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    error = body.Length > 300 ? body[..300] + "..." : body;
                }
                catch { /* the text is only a help */ }
            }
            if (!(isHealth && (int)res.StatusCode < 400))
                DevNetworkLog.Add(new DevNetworkEntry(DateTimeOffset.UtcNow, _area, request.Method.Method, host + url, (int)res.StatusCode, sw.ElapsedMilliseconds, error));
            return res;
        }
        catch (Exception ex)
        {
            DevNetworkLog.Add(new DevNetworkEntry(DateTimeOffset.UtcNow, _area, request.Method.Method, host + url, 0, sw.ElapsedMilliseconds, ex.GetType().Name + ": " + ex.Message));
            throw;
        }
        finally { if (counted) ApiActivity.End(activityId); }
    }
}
//JJQR - END
