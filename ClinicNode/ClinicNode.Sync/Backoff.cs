// JJQR - AI (Nov 21, 2026) - exponential backoff with jitter for the sync loop.
namespace ClinicNode.Sync;

public static class Backoff
{
    public static readonly TimeSpan Initial = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan Cap = TimeSpan.FromMinutes(5);

    /// <summary>Delay after <paramref name="failedAttempts"/> consecutive failures (1-based).</summary>
    public static TimeSpan DelayAfter(int failedAttempts, Random? rng = null)
    {
        var attempt = Math.Max(1, failedAttempts);
        var ticks = Initial.Ticks * (1L << Math.Min(attempt - 1, 16));
        var raw = TimeSpan.FromTicks(Math.Min(ticks, Cap.Ticks));
        var jitter = (rng ?? Random.Shared).NextDouble() * 0.2; // ±0..20%
        var factor = 0.9 + jitter;
        var jittered = TimeSpan.FromTicks((long)(raw.Ticks * factor));
        return jittered > Cap ? Cap : jittered;
    }
}
// JJQR - END
