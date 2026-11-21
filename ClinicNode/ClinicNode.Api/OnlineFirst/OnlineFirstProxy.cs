//JJQR - AI (Nov 21, 2026) - the generic online-first decorator. One proxy class serves ANY Core service interface whose members return Task / Task<T>:
//   online  → the Api implementation          (a write that succeeds asks the sync loop for a pull, so the local copy follows)
//   offline → the Local implementation        (writes go to the existing outbox, exactly as before)
//   Api call fails with a network error / timeout / gateway error → the Local implementation (and the connectivity monitor flips offline)
//   Api throws ApiNotSupportedException      → the Local implementation (partially converted service)
//   no API session (offline-only sign-in) or the API session expired (401 after refresh) → the Local implementation
// Real server answers (validation, 401/403/404/409, 5xx) are NOT swallowed: they reach the caller as typed ApiExceptions.
using System.Reflection;
using ClinicNode.Api.Http;
using ClinicNode.Core.Sync;

namespace ClinicNode.Api.OnlineFirst;

public class OnlineFirstProxy<T> : DispatchProxy where T : class
{
    private T _api = null!;
    private T _local = null!;
    private IApiConnectivity _connectivity = null!;
    private ISyncBridge? _sync;
    private Func<bool> _hasApiSession = () => true;
    private Action? _dropApiSession;
    //JJQR - AI (21 Nov 2026) - owner rule: every successful API read is mirrored into the local SQLite copy
    private IOnlineReadMirror? _mirror;
    //JJQR - END

    public static T Create(T api, T local, IApiConnectivity connectivity, ISyncBridge? sync = null, Func<bool>? hasApiSession = null, Action? dropApiSession = null,
        //JJQR - AI (21 Nov 2026)
        IOnlineReadMirror? mirror = null)
        //JJQR - END
    {
        var proxy = Create<T, OnlineFirstProxy<T>>();
        var p = (OnlineFirstProxy<T>)(object)proxy;
        p._api = api; p._local = local; p._connectivity = connectivity; p._sync = sync;
        if (hasApiSession is not null) p._hasApiSession = hasApiSession;
        p._dropApiSession = dropApiSession;
        p._mirror = mirror; //JJQR - AI (21 Nov 2026) //JJQR - END
        return proxy;
    }

    /// <summary>Which side served the last call (for diagnostics and tests).</summary>
    public string LastRoute { get; private set; } = "";

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method is null) return null;
        var rt = method.ReturnType;
        if (rt == typeof(Task)) return RunVoidAsync(method, args);
        if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
            return Call(this, typeof(OnlineFirstProxy<T>).GetMethod(nameof(RunAsync), BindingFlags.Instance | BindingFlags.NonPublic)!
                .MakeGenericMethod(rt.GetGenericArguments()[0]), [method, args]);
        // properties and other synchronous members are device-local
        return Call(_local, method, args);
    }

    private static OnlineRoute RouteOf(MethodInfo m) => m.GetCustomAttribute<OnlineRouteAttribute>()?.Route ?? OnlineRoute.Auto;

    private static readonly string[] ReadPrefixes = ["Get", "List", "Search", "Find", "Count", "Is", "Has", "Load", "Query", "Read", "Preview", "Build", "Can", "Lookup", "Fetch"];

    internal static bool IsWrite(MethodInfo m)
    {
        if (m.GetCustomAttribute<OnlineWriteAttribute>() is not null) return true;
        if (m.GetCustomAttribute<OnlineReadAttribute>() is not null) return false;
        return !ReadPrefixes.Any(p => m.Name.StartsWith(p, StringComparison.Ordinal));
    }

    private static object? Call(object target, MethodInfo m, object?[]? args)
    {
        try { return m.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private async Task<TResult> RunAsync<TResult>(MethodInfo m, object?[]? args)
    {
        var route = RouteOf(m);
        if (route == OnlineRoute.LocalOnly) { LastRoute = "local"; return await (Task<TResult>)Call(_local, m, args)!; }
        if (!_hasApiSession() || !await _connectivity.EnsureCheckedAsync())
        {
            if (route == OnlineRoute.ApiOnly) throw new ApiOfflineException("This needs a connection to the clinic server and an online sign-in.");
            LastRoute = "local"; return await (Task<TResult>)Call(_local, m, args)!;
        }
        try
        {
            var result = await (Task<TResult>)Call(_api, m, args)!;
            LastRoute = "api";
            if (IsWrite(m)) _sync?.RequestSync();
            //JJQR - AI (21 Nov 2026) - read served by the API → write it into the local DB (lane hook, else a catch-up pull). Never blocks or throws.
            else _mirror?.OnApiRead(typeof(T), m, args, result);
            //JJQR - END
            return result;
        }
        catch (ApiOfflineException) when (route != OnlineRoute.ApiOnly) { _connectivity.ReportNetworkFailure(); }
        catch (ApiNotSupportedException) when (route != OnlineRoute.ApiOnly) { }
        catch (ApiUnauthorizedException) when (route != OnlineRoute.ApiOnly) { _dropApiSession?.Invoke(); } // the API session is gone (refresh failed): carry on locally until the next online sign-in
        LastRoute = "local";
        //JJQR - AI (21 Nov 2026) - offline write: the Local service saved it and queued it in the outbox; wake the sync loop so it is sent when back online
        var localResult = await (Task<TResult>)Call(_local, m, args)!;
        if (IsWrite(m)) _sync?.RequestSync();
        return localResult;
        //JJQR - END
    }

    private async Task RunVoidAsync(MethodInfo m, object?[]? args)
    {
        var route = RouteOf(m);
        if (route == OnlineRoute.LocalOnly) { LastRoute = "local"; await (Task)Call(_local, m, args)!; return; }
        if (!_hasApiSession() || !await _connectivity.EnsureCheckedAsync())
        {
            if (route == OnlineRoute.ApiOnly) throw new ApiOfflineException("This needs a connection to the clinic server and an online sign-in.");
            LastRoute = "local"; await (Task)Call(_local, m, args)!; return;
        }
        try
        {
            await (Task)Call(_api, m, args)!;
            LastRoute = "api";
            if (IsWrite(m)) _sync?.RequestSync();
            return;
        }
        catch (ApiOfflineException) when (route != OnlineRoute.ApiOnly) { _connectivity.ReportNetworkFailure(); }
        catch (ApiNotSupportedException) when (route != OnlineRoute.ApiOnly) { }
        catch (ApiUnauthorizedException) when (route != OnlineRoute.ApiOnly) { _dropApiSession?.Invoke(); } // the API session is gone (refresh failed): carry on locally until the next online sign-in
        LastRoute = "local";
        await (Task)Call(_local, m, args)!;
        if (IsWrite(m)) _sync?.RequestSync(); //JJQR - AI (21 Nov 2026) - offline write queued locally //JJQR - END
    }
}
//JJQR - END
