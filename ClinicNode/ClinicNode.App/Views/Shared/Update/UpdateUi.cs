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
            //JJQR - AI (Nov 23, 2026) - owner: "for a second I thought nothing happens". Show a full-screen progress page the moment Install now is pressed: spinner, download percent, then "restarting".
            var status = new Label { Text = $"Downloading version {info.Latest!.ToString(3)}...", FontSize = 15, HorizontalTextAlignment = TextAlignment.Center, TextColor = Color.FromArgb("#5A6B66") };
            var bar = new ProgressBar { Progress = 0, ProgressColor = Color.FromArgb("#0F6B5C"), WidthRequest = 320 };
            var busyPage = new ContentPage
            {
                BackgroundColor = Color.FromArgb("#F4F7F6"),
                Content = new VerticalStackLayout
                {
                    Spacing = 16, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#0F6B5C"), HeightRequest = 40, WidthRequest = 40 },
                        new Label { Text = "Updating Clinic Node", FontSize = 22, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, TextColor = Color.FromArgb("#1B2A26") },
                        bar, status,
                        new Label { Text = "Please do not close the app. It will restart by itself.", FontSize = 13, HorizontalTextAlignment = TextAlignment.Center, TextColor = Color.FromArgb("#5A6B66") },
                    },
                },
            };
            await shell.Navigation.PushModalAsync(busyPage, false);
            string? err;
            try
            {
                var progress = new Progress<double>(pr => { bar.Progress = pr; status.Text = $"Downloading version {info.Latest!.ToString(3)}... {(int)(pr * 100)}%"; });
                err = await svc.PrepareInstallAsync(info, progress);
            }
            catch (Exception ex) { err = "The update failed: " + ex.Message; }
            if (err is not null)
            {
                try { await shell.Navigation.PopModalAsync(false); } catch { }
                await shell.DisplayAlertAsync("Update", err, "OK");
                return;
            }
            bar.Progress = 1;
            status.Text = "Installing... Clinic Node is restarting";
            await Task.Delay(1500);
            Environment.Exit(0);
            return;
            //JJQR - END
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
