//JJQR - AI (21 Nov 2026) - owner rule 1: while online, every successful API READ also lands in the local SQLite copy. Done centrally in OnlineFirstProxy:
//   a) if the lane registered an IApiReadCacheWriter<TService>, it gets (method, args, result) and upserts the rows straight away;
//   b) ALWAYS: a catch-up pull (ISyncBridge.RequestSync → /api/sync/pull from the saved cursor) is requested, debounced, so the local rows
//      match the server even for services without a writer. Lanes need NOTHING beyond AddOnlineFirst; a writer is an optional speed-up.
// Never blocks the caller, never throws, never logs data.
using System.Reflection;
using ClinicNode.Core.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClinicNode.Api.OnlineFirst;

public interface IOnlineReadMirror
{
    void OnApiRead(Type service, MethodInfo method, object?[]? args, object? result);
}

/// <summary>Optional lane hook: write an API read result into the local DB (upsert by primary key; never touch outbox rows).</summary>
public interface IApiReadCacheWriter<TService> where TService : class
{
    Task WriteAsync(MethodInfo method, object?[]? args, object? result, CancellationToken ct = default);
}

public sealed class OnlineReadMirror : IOnlineReadMirror
{
    public static readonly TimeSpan PullDebounce = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _sp;
    private readonly ISyncBridge _sync;
    private readonly ILogger<OnlineReadMirror> _logger;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private DateTimeOffset _lastPull = DateTimeOffset.MinValue;

    public OnlineReadMirror(IServiceProvider sp, ISyncBridge sync, ILogger<OnlineReadMirror> logger, TimeProvider? clock = null)
    {
        _sp = sp; _sync = sync; _logger = logger; _clock = clock ?? TimeProvider.System;
    }

    public void OnApiRead(Type service, MethodInfo method, object?[]? args, object? result)
    {
        var writerType = typeof(IApiReadCacheWriter<>).MakeGenericType(service);
        var writer = _sp.GetService(writerType);
        if (writer is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var write = writerType.GetMethod(nameof(IApiReadCacheWriter<object>.WriteAsync))!;
                    await (Task)write.Invoke(writer, [method, args, result, CancellationToken.None])!;
                }
                catch (Exception ex)
                {
                    // the API answer already reached the screen; the pull below still repairs the local copy
                    _logger.LogWarning("Local cache write failed for {Service}.{Method}: {Error}", service.Name, method.Name, ex.GetType().Name);
                }
            });
        }

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (now - _lastPull < PullDebounce) return;
            _lastPull = now;
        }
        _sync.RequestSync(); // push pending + pull changes since the cursor
    }
}
//JJQR - END
