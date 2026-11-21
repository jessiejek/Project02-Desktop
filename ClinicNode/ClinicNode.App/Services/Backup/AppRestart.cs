//JJQR - AI (Nov 21, 2026) - after a restore every service still holds the old DB open, so the app must start again.
// Windows: AppInstance.Restart relaunches. Mac Catalyst (App Sandbox): no self-relaunch; the page tells the user, then the app quits.
namespace ClinicNode.App.Services.Backup;

public static class AppRestart
{
    /// <summary>True when the platform relaunches by itself; false when the user must open the app again.</summary>
    public static bool CanRelaunch =>
#if WINDOWS
        true;
#else
        false;
#endif

    public static void RestartOrQuit()
    {
#if WINDOWS
        var r = Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
        // Restart only returns on failure
        Application.Current?.Quit();
#else
        Application.Current?.Quit();
#endif
    }
}
//JJQR - END
