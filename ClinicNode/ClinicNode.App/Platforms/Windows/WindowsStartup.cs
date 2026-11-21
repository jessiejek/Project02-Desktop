//JJQR - AI (Nov 21, 2026) - MSIX auto-start toggle (StartupTask "ClinicNodeStartup" in Package.appxmanifest). Only works in the packaged app.
using Windows.ApplicationModel;

namespace ClinicNode.App.WinUI;

public static class WindowsStartup
{
    public const string TaskId = "ClinicNodeStartup";

    public static async Task<bool> IsEnabledAsync()
    {
        var task = await StartupTask.GetAsync(TaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    /// <summary>Asks Windows to start Clinic Node at sign-in. Returns false if the user/policy disabled it (then only Settings can re-enable).</summary>
    public static async Task<bool> EnableAsync()
    {
        var task = await StartupTask.GetAsync(TaskId);
        var state = task.State == StartupTaskState.Disabled ? await task.RequestEnableAsync() : task.State;
        return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    public static async Task DisableAsync() => (await StartupTask.GetAsync(TaskId)).Disable();
}
//JJQR - END
