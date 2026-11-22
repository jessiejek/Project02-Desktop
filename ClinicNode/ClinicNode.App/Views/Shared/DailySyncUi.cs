//JJQR - AI (Nov 22, 2026) - offline-mode plan step 3.3 (D5): the first sign-in of the day syncs this computer before the dashboard opens, with a Skip button. It never blocks the sign-in.
using ClinicNode.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Views.Shared;

public static class DailySyncUi
{
    private static DateOnly _registrationNoticeDay;

    public static async Task RunIfNeededAsync(IServiceProvider sp)
    {
        try
        {
            var sync = sp.GetRequiredService<ISyncService>();
            var shell = Shell.Current;
            switch (await sync.GetDailySyncDecisionAsync())
            {
                case DailySyncDecision.NotRegistered:
                    var today = ClinicApp.Domain.ClinicClock.Today;
                    if (_registrationNoticeDay != today)   // once a day is enough
                    {
                        _registrationNoticeDay = today;
                        await shell.DisplayAlertAsync("This computer is not registered", "Syncing is off until an admin registers this computer (Admin > Sync computers). Everything you do is saved on this computer and will be sent once it is registered.", "OK");
                    }
                    break;
                case DailySyncDecision.Needed:
                    var result = await SyncResultPage.ShowAsync(sp, "daily", skippable: true);
                    if (result is null || !result.Succeeded || result.StillWaiting > 0)
                    {
                        var why = result is null ? "You skipped it." : result.Succeeded ? $"{result.StillWaiting} change{(result.StillWaiting == 1 ? " is" : "s are")} still waiting." : (result.Error ?? "The clinic server did not answer.");
                        if (await shell.DisplayAlertAsync("Couldn't sync this morning", why + " Your work is safe on this computer.", "Sync now", "Dismiss"))
                            await SyncResultPage.ShowAsync(sp, "manual");
                    }
                    break;
            }
        }
        catch { /* the sign-in must go on whatever happens here */ }
    }
}
//JJQR - END
