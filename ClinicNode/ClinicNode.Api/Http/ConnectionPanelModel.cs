//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.5: the numbers the connection panel shows, computed from the monitor's samples so a test can prove they match.
namespace ClinicNode.Api.Http;

public sealed record ConnectionPanelNumbers(int? LastMs, int? MedianMs, int? WorstMs, DateTimeOffset? LastContactAt, string Database, int Checks, int Failed);

public static class ConnectionPanelModel
{
    /// <summary>Last = newest check; median and worst = over every kept check (up to 60), failed or not (a timeout counts its waiting time).
    /// Last contact = newest check where the server answered. Database = from the newest check.</summary>
    public static ConnectionPanelNumbers Compute(IReadOnlyList<HealthSample> samples)
    {
        if (samples.Count == 0) return new(null, null, null, null, "Not checked yet", 0, 0);
        var ms = samples.Select(s => s.Ms).OrderBy(m => m).ToList();
        var newest = samples[^1];
        var contact = samples.LastOrDefault(s => s.Ok);
        var db = newest.DatabaseOk switch
        {
            true => "Ready",
            false => "Not ready",
            null => newest.Ok ? "Unknown (older server)" : "Unknown (server not answering)",
        };
        return new(newest.Ms, ms[ms.Count / 2], ms[^1], contact?.At, db, samples.Count, samples.Count(s => !s.Ok));
    }
}
//JJQR - END
