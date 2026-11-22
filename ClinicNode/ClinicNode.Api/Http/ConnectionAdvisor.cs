//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.4: suggestions, never an automatic switch (decision D2). This class can only READ the work mode; it has no way to change it.
//   Online and Down for 60 s, or Slow for 3 min  -> "Connection is poor. Switch to Offline mode?"
//   Offline and Good for 5 min                   -> "Connection looks good. Sync, then go Online?"
//   The same suggestion is not repeated for 15 min.
using ClinicNode.Core.Sync;

namespace ClinicNode.Api.Http;

public enum SuggestionKind { SwitchToOffline, SyncThenGoOnline }

public sealed record ConnectionSuggestion(SuggestionKind Kind, string Message);

public interface IConnectionAdvisor
{
    /// <summary>Raised (any thread) when a suggestion should be shown. The mode has NOT changed.</summary>
    event Action<ConnectionSuggestion>? Suggested;
}

public sealed class ConnectionAdvisor : IConnectionAdvisor, IDisposable
{
    public static readonly TimeSpan DownFor = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan SlowFor = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan GoodFor = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(15);

    public const string PoorMessage = "Connection is poor. Switch to Offline mode?";
    public const string GoodMessage = "Connection looks good. Sync, then go Online?";

    private readonly IConnectionMonitor _monitor;
    private readonly IWorkModeService _mode; // read only: Mode is the only thing used
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private string _key = "";
    private DateTimeOffset _since;
    private readonly Dictionary<SuggestionKind, DateTimeOffset> _lastShown = [];

    public ConnectionAdvisor(IConnectionMonitor monitor, IWorkModeService mode, Func<DateTimeOffset>? now = null)
    {
        _monitor = monitor; _mode = mode; _now = now ?? (() => DateTimeOffset.UtcNow);
        _since = _now();
        _monitor.Changed += Evaluate;
        _mode.Changed += (_, _) => Evaluate();
    }

    public event Action<ConnectionSuggestion>? Suggested;

    /// <summary>Looks at the monitor and the mode now. Called after every new check and every mode change.</summary>
    public void Evaluate()
    {
        ConnectionSuggestion? raise = null;
        lock (_gate)
        {
            var now = _now();
            var offline = _mode.Mode == WorkMode.Offline;
            var key = (offline ? "off:" : "on:") + _monitor.Status.Quality switch
            {
                ConnectionQuality.Down => "down",
                ConnectionQuality.Slow => "slow",
                ConnectionQuality.Good => "good",
                _ => "other",
            };
            if (key != _key) { _key = key; _since = now; }   // the state changed: the clock starts again
            var held = now - _since;

            SuggestionKind? kind = key switch
            {
                "on:down" when held >= DownFor => SuggestionKind.SwitchToOffline,
                "on:slow" when held >= SlowFor => SuggestionKind.SwitchToOffline,
                "off:good" when held >= GoodFor => SuggestionKind.SyncThenGoOnline,
                _ => null,
            };
            if (kind is { } k && (!_lastShown.TryGetValue(k, out var last) || now - last >= Cooldown))
            {
                _lastShown[k] = now;
                raise = new(k, k == SuggestionKind.SwitchToOffline ? PoorMessage : GoodMessage);
            }
        }
        if (raise is not null) { try { Suggested?.Invoke(raise); } catch { /* a listener must never break the advisor */ } }
    }

    public void Dispose() => _monitor.Changed -= Evaluate;
}
//JJQR - END
