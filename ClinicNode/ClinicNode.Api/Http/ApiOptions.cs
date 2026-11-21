//JJQR - AI (Nov 21, 2026) - settings of the online-first API layer.
namespace ClinicNode.Api.Http;

public sealed class ApiOptions
{
    /// <summary>Default for development; the App replaces it with <c>() =&gt; ISyncEndpoint.BaseUrl</c> so the same address (cloud-endpoint.json) serves sync and the API.</summary>
    public const string DefaultBaseUrl = "http://localhost:5055";

    /// <summary>Resolved on every request, so a changed endpoint takes effect immediately. null / blank = no API configured (always offline).</summary>
    public Func<string?> BaseUrl { get; set; } = () => DefaultBaseUrl;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Health ping timeout: short, so an unreachable server never stalls the UI.</summary>
    public TimeSpan PingTimeout { get; set; } = TimeSpan.FromSeconds(3);
    /// <summary>Re-check period while online.</summary>
    public TimeSpan OnlineInterval { get; set; } = TimeSpan.FromSeconds(20);
    /// <summary>Re-check delays while offline (the last one repeats): 2 s, 5 s, 10 s, 30 s.</summary>
    public TimeSpan[] OfflineBackoff { get; set; } = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    /// <summary>Start the background health loop when the monitor is created (default). Tests turn it off for deterministic timing.</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>Tests plug a fake handler here.</summary>
    public Func<HttpMessageHandler>? HttpHandlerFactory { get; set; }

    public string? ResolveBaseUrl()
    {
        var u = BaseUrl()?.Trim();
        return string.IsNullOrEmpty(u) ? null : u.TrimEnd('/');
    }
}
//JJQR - END
