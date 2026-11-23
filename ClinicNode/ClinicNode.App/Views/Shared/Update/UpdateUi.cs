//JJQR - AI (Nov 22, 2026) - OTA: the dialog behind the "Update available" chip and the "Check for updates" button.
using ClinicNode.App.Services;
using ClinicNode.Core.Update;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Views.Shared;

public static class UpdateUi
{
    public static string StatusLine(UpdateInfo i) => i.Status switch
    {
        UpdateStatus.Available => $"Version {i.Latest!.ToString(3)} is available",
        UpdateStatus.Required => $"Version {i.Latest!.ToString(3)} is required",
        UpdateStatus.UpToDate => "You have the latest version",
        UpdateStatus.Unreachable => "Could not check (no connection)",
        UpdateStatus.BadSignature => "The update information was refused (not signed correctly)",
        UpdateStatus.Error => i.Message ?? "Could not check for updates",
        _ => "Not checked yet",
    };

    public static async Task ShowAsync(IServiceProvider sp)
    {
        var svc = sp.GetRequiredService<AppUpdateService>();
        var shell = Shell.Current;
        if (shell is null) return;
        var info = svc.Last;
        if (!info.HasUpdate) info = await svc.CheckAsync();
        if (!info.HasUpdate) { await shell.DisplayAlertAsync("Clinic Node " + svc.CurrentText, StatusLine(info), "OK"); return; }

        var notes = info.Notes is { Count: > 0 } n ? "\n\nWhat is new:\n• " + string.Join("\n• ", n) : "";
        var required = info.Status == UpdateStatus.Required ? "\n\nThis update is required." : "";
#if MACCATALYST
        var canInstall = AppUpdateService.CanInstallInPlace();
#else
        var canInstall = false;
#endif
        var buttons = canInstall ? new[] { "Install now (the app restarts)", "Download and open", "Copy the install command" } : new[] { "Download and open", "Copy the install command" };
        var choice = await shell.DisplayActionSheetAsync($"Update to {info.Latest!.ToString(3)} (you have {svc.CurrentText}){notes}{required}", "Later", null, buttons);
#if MACCATALYST
        if (choice == "Install now (the app restarts)") //JJQR - AI (Nov 23, 2026) - one-click update //JJQR - END
        {
            var err = await svc.PrepareInstallAsync(info);
            if (err is not null) { await shell.DisplayAlertAsync("Update", err, "OK"); return; }
            await shell.DisplayAlertAsync("Updating", "Clinic Node will close, update itself and reopen in a few seconds.", "OK");
            Environment.Exit(0);
            return;
        }
#endif
        if (choice == "Copy the install command")
        {
            await Clipboard.Default.SetTextAsync(AppUpdateService.InstallCommand);
            await shell.DisplayAlertAsync("Copied", "Open Terminal, paste (Cmd+V) and press Enter. It installs the new version without any warning and opens it.", "OK");
            return;
        }
        if (choice != "Download and open") return;
        var (path, error) = await svc.DownloadAsync(info);
        if (path is null) { await shell.DisplayAlertAsync("Update", error ?? "The download failed.", "OK"); return; }
#if MACCATALYST
        UIKit.UIApplication.SharedApplication.OpenUrl(Foundation.NSUrl.FromFilename(path), new UIKit.UIApplicationOpenUrlOptions(), null);
#endif
        await shell.DisplayAlertAsync("Update downloaded and checked", "The installer is open. Drag Clinic Node onto Applications and choose Replace, then open it again. (macOS may ask you to right-click > Open the first time.)", "OK");
    }
}
//JJQR - END
