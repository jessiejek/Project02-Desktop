//JJQR - AI (Nov 21, 2026)
// Background sync loop: probe connectivity, run a cycle, back off on failure (1s..5min, jitter),
// poll every SyncOptions.PollInterval while healthy, wake early on RequestSync().
// MAUI does not run IHostedService: the App calls ISyncLoop.Start() after LocalDatabase.InitializeAsync().
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClinicNode.Sync;

public interface ISyncLoop
{
    bool IsRunning { get; }
    void Start();
    Task StopAsync();
    void RequestSync();
}

public sealed class SyncLoop : ISyncLoop, IHostedService, IAsyncDisposable
{
    private readonly SyncService _sync;
    private readonly ICloudSyncClient _cloud;
    private readonly SyncOptions _options;
    private readonly ILogger<SyncLoop> _logger;
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _cts;
    private Task? _task;

    public SyncLoop(SyncService sync, ICloudSyncClient cloud, SyncOptions options, ILogger<SyncLoop> logger)
    {
        _sync = sync;
        _cloud = cloud;
        _options = options;
        _logger = logger;
        _sync.SyncRequested = RequestSync;
    }

    public bool IsRunning => _task is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
        _cts = new CancellationTokenSource();
        _task = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null || _task is null) return;
        _cts.Cancel();
        try { await _task; } catch (OperationCanceledException) { }
        _cts.Dispose();
        _cts = null;
        _task = null;
    }

    public void RequestSync()
    {
        if (_wake.CurrentCount == 0)
        {
            try { _wake.Release(); } catch (SemaphoreFullException) { }
        }
    }

    Task IHostedService.StartAsync(CancellationToken cancellationToken) { Start(); return Task.CompletedTask; }
    Task IHostedService.StopAsync(CancellationToken cancellationToken) => StopAsync();

    private async Task RunAsync(CancellationToken ct)
    {
        var failures = 0;
        while (!ct.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                if (!_cloud.IsConfigured)
                {
                    await _sync.RefreshStatusAsync(ct);
                    wait = _options.PollInterval;
                }
                else if (!await _cloud.PingAsync(ct))
                {
                    failures++;
                    _sync.ReportConnectivity(false);
                    await _sync.RefreshStatusAsync(ct);
                    wait = Min(Backoff.DelayAfter(failures), _options.OfflineProbeCap);
                }
                else
                {
                    _sync.ReportConnectivity(true);
                    var result = await _sync.RunOnceAsync(ct);
                    if (result.Succeeded)
                    {
                        failures = 0;
                        wait = result.RetryLater > 0 ? Min(_options.PollInterval, Backoff.DelayAfter(1)) : _options.PollInterval;
                    }
                    else if (result.Failure is SyncFailureKind.Revoked or SyncFailureKind.VersionMismatch or SyncFailureKind.NotRegistered)
                    {
                        wait = Backoff.Cap; // needs a person (admin / update); check again later
                    }
                    else
                    {
                        failures++;
                        wait = Backoff.DelayAfter(failures);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                _logger.LogError(ex, "Sync loop iteration failed.");
                wait = Backoff.DelayAfter(failures);
            }

            try { await _wake.WaitAsync(wait, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _wake.Dispose();
    }
}
//JJQR - END
