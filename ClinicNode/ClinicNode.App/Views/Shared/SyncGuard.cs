//JJQR - AI (Nov 22, 2026) - offline-mode plan step 3.5 (owner approved 22 Nov): before the doctor leaves a state where unsynced work could be forgotten (sign out, switch Offline to Online), the computer syncs by itself first.
// If everything went up, nothing is asked. If something is still waiting (the server did not answer, or a change was not accepted), the doctor chooses: sync again, leave anyway (the changes stay safely queued), or cancel.
using ClinicNode.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Views.Shared;

public static class SyncGuard
{
    /// <returns>true = go ahead and leave; false = the user chose to stay.</returns>
    public static async Task<bool> ConfirmLeavingAsync(IServiceProvider sp, string trigger, string leaveLabel)
    {
        var status = sp.GetRequiredService<ISyncStatus>();
        try
        {
            await status.RefreshAsync();
            if (status.PendingCount == 0) return true;
            await SyncResultPage.ShowAsync(sp, trigger, skippable: true);
            while (true)
            {
                await status.RefreshAsync();
                var left = status.PendingCount;
                if (left == 0) return true;
                var choice = await Shell.Current.DisplayActionSheetAsync(
                    $"{left} change{(left == 1 ? " is" : "s are")} not on the clinic server yet. They are safe on this computer.", "Cancel", null, "Sync now", leaveLabel);
                if (choice == "Sync now") { await SyncResultPage.ShowAsync(sp, trigger, skippable: true); continue; }
                return choice == leaveLabel;
            }
        }
        catch { return true; }   // never trap the user because of a problem in this check
    }
}
//JJQR - END
