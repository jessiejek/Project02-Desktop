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
        try { await work(); }
        finally
        {
            b.IsEnabled = true;
            if (b.Text == busyText) b.Text = original;   // the work may have changed the text itself (then leave it)
        }
    }
}
//JJQR - END
