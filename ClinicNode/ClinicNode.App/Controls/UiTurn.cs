//JJQR - AI (Nov 23, 2026) - WINDOWS safety helper. WinUI can crash when a click/tap redraws the screen (removing the very control that was clicked) while the click is still being handled.
// On Windows this runs the action on the NEXT UI turn and logs (instead of crashing on) any error; on the Mac it just runs the action right away, exactly as before.
using ClinicNode.Core.Diagnostics;

namespace ClinicNode.App.Controls;

public static class UiTurn
{
    public static void Next(Action action)
    {
#if WINDOWS
        var d = Application.Current?.Dispatcher;
        if (d is null) { action(); return; }
        d.Dispatch(() =>
        {
            try { action(); }
            catch (Exception ex) { ClinicErrorLog.Write("UiTurn.Next", ex); }
        });
#else
        action();
#endif
    }
}
//JJQR - END
