//JJQR - AI (Nov 24, 2026) - owner: "after pressing Print prescription there should be a loading while waiting for the print dialog to show".
// A small "Preparing the print…" message with a spinner appears as soon as Print is pressed and goes away when the print window is on screen (or the print fails).
// It is safe to call from anywhere: Begin does nothing if one is already showing, End does nothing if none is.
using ClinicNode.App.Views.Shared.Controls;

namespace ClinicNode.App.Controls;

public static class PrintBusy
{
    static readonly object Gate = new();
    static IDisposable? _current;

    public static void Begin(string message = "Preparing the print…")
    {
        lock (Gate)
        {
            if (_current is not null) return;
            _current = ClinicToast.ShowBusy(message);
        }
    }

    public static void End()
    {
        IDisposable? c;
        lock (Gate) { c = _current; _current = null; }
        c?.Dispose();
    }
}
//JJQR - END
