//JJQR - AI (Nov 21, 2026) - Mac Catalyst ignores Window.Width/Height, so the first window is zoomed (the green button's "Zoom") to fill the screen on start-up.
using System.Runtime.InteropServices;
using ObjCRuntime;

namespace ClinicNode.App.Platforms.MacCatalyst;

internal static class WindowZoom
{
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr MsgNint(IntPtr receiver, IntPtr selector, nint index);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern void MsgVoid(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern byte MsgBool(IntPtr receiver, IntPtr selector);

    public static void ZoomFirstWindow()
    {
        try
        {
            var app = Msg(Class.GetHandle("NSApplication"), Selector.GetHandle("sharedApplication"));
            if (app == IntPtr.Zero) return;
            var windows = Msg(app, Selector.GetHandle("windows"));
            if (windows == IntPtr.Zero) return;
            var first = MsgNint(windows, Selector.GetHandle("objectAtIndex:"), 0);
            if (first == IntPtr.Zero) return;
            if (MsgBool(first, Selector.GetHandle("isZoomed")) != 0) return;
            MsgVoid(first, Selector.GetHandle("zoom:"), IntPtr.Zero);
        }
        catch { /* best effort: the user can still zoom from the Window menu */ }
    }
}
//JJQR - END
