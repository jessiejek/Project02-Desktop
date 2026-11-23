// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core.Auth;
using ClinicNode.Data;
using ClinicNode.Sync;

namespace ClinicNode.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly LocalDatabase _database;
    private readonly Task _startup;

    public App(IServiceProvider services)
    {
        //JJQR - AI (Nov 22, 2026) - release-build diagnosis: log the real reason if the very first setup fails
        try { InitializeComponent(); }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(FileSystem.AppDataDirectory, "ClinicNode", "startup-crash.log"), $"{DateTimeOffset.Now:O} [App.InitializeComponent]\n{ex}\n\n"); } catch { }
            throw;
        }
        //JJQR - END
        // Force Light always. The web app has no dark-theme toggle;
        // ignore macOS / Windows dark mode so clinic screens stay on #f8f9ff.
        UserAppTheme = AppTheme.Light;
        _services = services;
        _database = services.GetRequiredService<LocalDatabase>();
        // Start schema + demo seed immediately, before the first window, so login cannot race an empty file.
        _startup = StartAsync();
    }

    async Task StartAsync()
    {
        try
        {
            await _database.InitializeAsync();
            if (_services.GetRequiredService<IAuthService>() is AuthService auth)
                await auth.LoadRememberedEmailAsync();
            //JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.1: load the saved work mode before the login page, so a restart keeps it
            await _services.GetRequiredService<ClinicNode.Core.Sync.IWorkModeService>().InitializeAsync();
            //JJQR - END
            // MAUI does not run hosted services; start the sync loop only after the local DB is ready.
            _services.GetRequiredService<ISyncLoop>().Start();
            _services.GetService<ClinicNode.App.Services.AppUpdateService>()?.Start(); //JJQR - AI (Nov 22, 2026) - OTA: look for a newer version in the background //JJQR - END
            _services.GetRequiredService<ClinicNode.Api.Http.IConnectionMonitor>().Start(); //JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.3: watches the connection in both modes //JJQR - END
        }
        catch
        {
            // InitializationError is set by LocalDatabase; LoginPage and Created show it.
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        //JJQR - AI (Nov 22, 2026) - the RELEASE build crashed while opening the first window with no readable reason (macOS only shows "quit unexpectedly"). Write the real reason next to the database, then let it fail as before.
        try { return CreateWindowCore(); }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(FileSystem.AppDataDirectory, "ClinicNode", "startup-crash.log"), $"{DateTimeOffset.Now:O}\n{ex}\n\n"); } catch { /* nothing more we can do */ }
            throw;
        }
    }

    Window CreateWindowCore()
    {
        //JJQR - END
        var shell = _services.GetRequiredService<AppShell>();
        var window = new Window(shell) { Title = "Clinic Node" };
        //JJQR - AI (Nov 21, 2026) - desktop window: a sensible first size and a minimum so the three-column workspace never gets squeezed
        window.Width = 1360; window.Height = 860; window.MinimumWidth = 1100; window.MinimumHeight = 700;
        //JJQR - END
#if MACCATALYST
        var zoomed = false;
        window.Activated += async (_, _) =>
        {
            if (zoomed) return;
            zoomed = true;
            await Task.Delay(400);
            MainThread.BeginInvokeOnMainThread(ClinicNode.App.Platforms.MacCatalyst.WindowZoom.ZoomFirstWindow);
        };
#endif
        window.Created += async (_, _) =>
        {
            try
            {
                await _startup;
                if (_database.InitializationError is { } err)
                    await shell.DisplayAlert("Database", "Could not open the local clinic database: " + err, "OK");
            }
            catch (Exception ex)
            {
                await shell.DisplayAlert("Database", "Could not open the local clinic database: " + ex.Message, "OK");
            }
        };
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.4: a save that was refused because this computer cannot sync (not registered / revoked / app too old) says why, once, whichever screen it came from
        var lastBlockedAt = DateTimeOffset.MinValue;
        ClinicNode.Api.OnlineFirst.OnlineRouteStatus.LocalWriteBlocked += reason =>
        {
            if (DateTimeOffset.UtcNow - lastBlockedAt < TimeSpan.FromSeconds(10)) return;
            lastBlockedAt = DateTimeOffset.UtcNow;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try { await shell.DisplayAlertAsync("Can't save here yet", reason, "OK"); }
                catch { /* the window may be closing */ }
            });
        };
        //JJQR - END
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.2 (rule R1): an Online-mode save that could not reach the clinic server was NOT saved. Say so, and let the doctor or an admin switch to Offline mode.
        var lastFailedAt = DateTimeOffset.MinValue;
        ClinicNode.Api.OnlineFirst.OnlineRouteStatus.OnlineWriteFailed += reason =>
        {
            if (DateTimeOffset.UtcNow - lastFailedAt < TimeSpan.FromSeconds(5)) return;
            lastFailedAt = DateTimeOffset.UtcNow;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    var mode = _services.GetRequiredService<ClinicNode.Core.Sync.IWorkModeService>();
                    if (mode.CanSwitch)
                    {
                        var go = await shell.DisplayAlertAsync("Not saved", reason, "Switch to Offline mode", "Cancel");
                        if (!go) return;
                        var r = await mode.SetAsync(ClinicNode.Core.Sync.WorkMode.Offline);
                        await shell.DisplayAlertAsync(r.Succeeded ? "Offline mode is on" : "Can't switch", r.Succeeded ? "Press the save again. It will be kept on this computer and sent to the clinic server when you sync." : r.Error ?? "", "OK");
                    }
                    else await shell.DisplayAlertAsync("Not saved", "Could not save: the clinic server cannot be reached. Nothing was saved. Ask the doctor to switch this computer to Offline mode, or try again.", "OK");
                }
                catch { /* the window may be closing */ }
            });
        };
        //JJQR - END
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.4: a SUGGESTION to switch mode. Nothing changes unless the doctor or admin presses the button.
        var showingSuggestion = false;
        _services.GetRequiredService<ClinicNode.Api.Http.IConnectionAdvisor>().Suggested += suggestion =>
        {
            var mode = _services.GetRequiredService<ClinicNode.Core.Sync.IWorkModeService>();
            if (!mode.CanSwitch || showingSuggestion) return; // staff cannot switch, so they are not asked
            showingSuggestion = true;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    if (suggestion.Kind == ClinicNode.Api.Http.SuggestionKind.SwitchToOffline)
                    {
                        if (await shell.DisplayAlertAsync("Connection", suggestion.Message, "Switch to Offline mode", "Not now"))
                        {
                            var r = await mode.SetAsync(ClinicNode.Core.Sync.WorkMode.Offline);
                            if (!r.Succeeded) await shell.DisplayAlertAsync("Can't switch", r.Error ?? "", "OK");
                        }
                    }
                    else if (await shell.DisplayAlertAsync("Connection", suggestion.Message, "Sync and go Online", "Not now"))
                    {
                        var run = await _services.GetRequiredService<ISyncService>().RunManualAsync("switch");
                        if (run.Succeeded) await mode.SetAsync(ClinicNode.Core.Sync.WorkMode.Online);
                        else await shell.DisplayAlertAsync("Sync did not finish", (run.Error ?? "The clinic server did not accept everything.") + " You are still in Offline mode.", "OK");
                    }
                }
                catch { /* the window may be closing */ }
                finally { showingSuggestion = false; }
            });
        };
        //JJQR - END
        //JJQR - AI (Nov 22, 2026) - owner approved 22 Nov: remembered visit versions are forgotten after a manual / first-login sync (not after the background one)
        if (_services.GetService<ClinicNode.Sync.SyncService>() is { } syncService)
            syncService.ManualRunCompleted += r => { if (r.Succeeded) ClinicNode.Api.Http.VisitVersions.ClearAll(); };
        //JJQR - END
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 3.4: quitting (menu or Cmd-Q) first syncs what is waiting; if something is still waiting, ask. Not intercepted: the red window button and Dock > Quit (see the plan, spike V7).
#if MACCATALYST
        ClinicNode.App.AppDelegate.ConfirmQuitAsync = () => MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var status = _services.GetRequiredService<ISyncStatus>();
            if (status.PendingCount == 0) return true;
            await ClinicNode.App.Views.Shared.SyncResultPage.ShowAsync(_services, "close", skippable: true);
            await status.RefreshAsync();
            var left = status.PendingCount;
            if (left == 0) return true;
            return await shell.DisplayAlertAsync("Changes not synced", $"{left} change{(left == 1 ? " is" : "s are")} not on the clinic server yet. They are safe on this computer and will go next time you sync. Quit anyway?", "Quit anyway", "Stay");
        });
#endif
        //JJQR - END
        window.Destroying += (_, _) =>
        {
            //JJQR - AI (Nov 23, 2026) - owner: "closing the app on Windows takes a long time". The window waited on the main thread for the sync loop to finish (.GetResult()), so a sync in flight, or one that needed the main thread, held the window open until a timeout.
            // Ask the loop to stop OFF the main thread, wait at most about a second, and never block the window. On Windows, make sure the process really ends a few seconds later even if some background connection lingers (everything saved is in SQLite, which is crash-safe).
            try
            {
                var stop = Task.Run(() => _services.GetRequiredService<ISyncLoop>().StopAsync());
                stop.Wait(TimeSpan.FromMilliseconds(1000));
            }
            catch
            {
                // shutting down
            }
#if WINDOWS
            _ = Task.Run(async () => { await Task.Delay(TimeSpan.FromSeconds(3)); Environment.Exit(0); });
#endif
            //JJQR - END
        };
        return window;
    }
}
// JJQR - END
