//JJQR - AI (Nov 21, 2026) - optional hints on a Core service interface method for the online-first router.
namespace ClinicNode.Api.OnlineFirst;

public enum OnlineRoute
{
    /// <summary>Default: Api when online, Local when offline, when the call fails with a network error, or when the Api does not implement the method (ApiNotSupportedException).</summary>
    Auto,
    /// <summary>Needs the server (e.g. change password, send email). Offline → ApiOfflineException to the caller; never falls back.</summary>
    ApiOnly,
    /// <summary>Always the local database (device-only things: printing, backup, local settings).</summary>
    LocalOnly,
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnlineRouteAttribute(OnlineRoute route) : Attribute
{
    public OnlineRoute Route { get; } = route;
}

/// <summary>Force the read/write classification when the method name does not say it (reads never trigger a sync request; writes do after an Api success).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class OnlineReadAttribute : Attribute;

[AttributeUsage(AttributeTargets.Method)]
public sealed class OnlineWriteAttribute : Attribute;
//JJQR - END
