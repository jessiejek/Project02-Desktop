//JJQR - AI (Nov 22, 2026) - owner: every save must show it is working ("make the button load"). While the work runs the button is disabled and shows its text with "..." (e.g. "Save profile..."), and it is restored afterwards.
namespace ClinicNode.App.Controls;

public static class BusyButton
{
    public static async Task RunBusyAsync(this Button b, Func<Task> work)
    {
        if (!b.IsEnabled) return;
        var original = b.Text;
        var busyText = string.IsNullOrEmpty(original) ? original : original.TrimEnd('…', '.', ' ') + "…";
        b.IsEnabled = false;
        if (!string.IsNullOrEmpty(original)) b.Text = busyText;
#if WINDOWS
        //JJQR - AI (Nov 23, 2026) - WINDOWS ONLY: start the work on the next UI turn (the click may redraw the screen), and never let an error in it close the app: log it and show it
        await Task.Yield();
        try { await work(); }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Button:" + original, ex);
            ClinicNode.App.Views.Shared.Controls.ClinicToast.Show(string.IsNullOrWhiteSpace(ex.Message) ? "Something went wrong. Try again." : ex.Message, ClinicNode.App.Views.Shared.Controls.ToastKind.Error);
        }
        finally
#else
        try { await work(); }
        finally
#endif
        {
            b.IsEnabled = true;
            if (b.Text == busyText) b.Text = original;   // the work may have changed the text itself (then leave it)
        }
    }
}
//JJQR - END
