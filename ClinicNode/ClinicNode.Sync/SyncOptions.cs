// JJQR - AI (Nov 21, 2026) - allen2: cloud sync settings (contract docs/SYNC-API-CONTRACT.md). Rebuilt after an accidental truncation; same members as before.
namespace ClinicNode.Sync;

public sealed class SyncOptions
{
    /// <summary>The backend's normal local address; the node syncs here until settings say otherwise.</summary>
    public const string DefaultLocalBackendUrl = "http://localhost:5055";

    /// <summary>Start value for the cloud URL (the live value is <see cref="ISyncEndpoint.BaseUrl"/>). "" = local-only.</summary>
    public string BaseUrl { get; set; } = DefaultLocalBackendUrl;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl);

    public string PushPath { get; set; } = "/api/sync/push";
    public string PullPath { get; set; } = "/api/sync/pull";
    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.2: probe /health/ready (API AND database); an older server without it (404) is probed on LegacyHealthPath
    public string HealthPath { get; set; } = "/health/ready";
    public string LegacyHealthPath { get; set; } = "/health";
    //JJQR - END
    public string TokenPath { get; set; } = "/api/sync/token";

    /// <summary>Operations per push call (contract max 200).</summary>
    public int PushBatchSize { get; set; } = 100;
    public int MaxPushBatchesPerCycle { get; set; } = 20;
    /// <summary>Changes per pull page. A first pull is ~12.7k changes (icd10 ~12.2k), so 1000 per page.</summary>
    public int PullPageSize { get; set; } = 1000;
    public int MaxPullPagesPerCycle { get; set; } = 50;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan OfflineProbeCap { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Test hook: supplies the HTTP handler (a fake cloud) instead of a real socket handler.</summary>
    public Func<HttpMessageHandler>? HttpHandlerFactory { get; set; }
}
// JJQR - END
