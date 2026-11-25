//JJQR - AI (Nov 25, 2026) - owner: fix the pop-ups that answered BEFORE they closed (Cancel booking, Record payment, Register a new patient, Prescription form dialogs).
// The caller used to get its answer while the pop-up was still on screen, so it redrew or navigated under it (on Windows that showed pages as full-window overlays).
// Same rule as StaffDialog.FinishAsync: close first, then answer. A second click while it is closing does nothing (it could otherwise close the page underneath).
namespace ClinicNode.App.Views.Staff;

public static class ModalClose
{
    private static readonly HashSet<Page> Closing = [];

    public static async Task ThenAnswerAsync<T>(Page page, TaskCompletionSource<T> done, T value)
    {
        if (done.Task.IsCompleted || !Closing.Add(page)) return;
        try { if (page.Navigation.ModalStack.Contains(page)) await page.Navigation.PopModalAsync(); }
        catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("ModalClose", ex); }
        finally { Closing.Remove(page); }
        done.TrySetResult(value);
    }
}
//JJQR - END
