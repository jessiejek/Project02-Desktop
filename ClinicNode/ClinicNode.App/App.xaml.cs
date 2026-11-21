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
        InitializeComponent();
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
            // MAUI does not run hosted services; start the sync loop only after the local DB is ready.
            _services.GetRequiredService<ISyncLoop>().Start();
        }
        catch
        {
            // InitializationError is set by LocalDatabase; LoginPage and Created show it.
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
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
        window.Destroying += (_, _) =>
        {
            try
            {
                _services.GetRequiredService<ISyncLoop>().StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // shutting down
            }
        };
        return window;
    }
}
// JJQR - END
