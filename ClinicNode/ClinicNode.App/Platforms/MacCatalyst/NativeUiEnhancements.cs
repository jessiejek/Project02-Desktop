//JJQR - AI (Nov 22, 2026) - owner requests: (1) a pointing-hand style on everything clickable, (2) a second quick click on the same button must do nothing.
// Done ONCE for the whole app here, not per page.
using Microsoft.Maui.Handlers;
using UIKit;

namespace ClinicNode.App.Platforms.MacCatalyst;

public static class NativeUiEnhancements
{
    /// <summary>The button stays disabled this long after a click, so a double click (or a nervous second click) cannot start the same work twice.</summary>
    const int DebounceMs = 800;

    //JJQR - AI (Nov 22, 2026) - owner: the cursor did not become a hand over clickable things (Apple's UIPointerInteraction only draws a highlight on Mac). The real hand is AppKit's NSCursor.pointingHandCursor, which a Mac Catalyst app can reach through the Objective-C runtime.
    // A hover recognizer on every clickable view sets the hand while the mouse is over it and the arrow when it leaves. Disabled controls keep the arrow.
    [System.Runtime.InteropServices.DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr Msg(IntPtr receiver, IntPtr selector);

    static readonly IntPtr NSCursorClass = ObjCRuntime.Class.GetHandle("NSCursor");
    static readonly IntPtr SelPointingHand = ObjCRuntime.Selector.GetHandle("pointingHandCursor");
    static readonly IntPtr SelArrow = ObjCRuntime.Selector.GetHandle("arrowCursor");
    static readonly IntPtr SelSet = ObjCRuntime.Selector.GetHandle("set");

    static void SetCursor(bool hand)
    {
        try
        {
            if (NSCursorClass == IntPtr.Zero) return;
            var cursor = Msg(NSCursorClass, hand ? SelPointingHand : SelArrow);
            if (cursor != IntPtr.Zero) Msg(cursor, SelSet);
        }
        catch (Exception) { /* the cursor shape is cosmetic; never break the app for it */ }
    }

    static void AddPointer(UIView view)
    {
        if (view.GestureRecognizers?.OfType<UIHoverGestureRecognizer>().Any(h => h.Name == "clinic-hand") == true) return;
        var hover = new UIHoverGestureRecognizer(g =>
        {
            switch (g.State)
            {
                case UIGestureRecognizerState.Began:
                case UIGestureRecognizerState.Changed:
                    SetCursor(!(g.View is UIControl { Enabled: false }));
                    break;
                case UIGestureRecognizerState.Ended:
                case UIGestureRecognizerState.Cancelled:
                    SetCursor(false);
                    break;
            }
        })
        { Name = "clinic-hand", CancelsTouchesInView = false };
        view.AddGestureRecognizer(hover);
    }
    //JJQR - END

    public static void Register()
    {
        ButtonHandler.Mapper.AppendToMapping("ClinicClickGuardAndPointer", (handler, view) =>
        {
            if (handler.PlatformView is not UIButton ui) return;
            AddPointer(ui);
            ui.AddTarget((_, _) =>
            {
                ui.Enabled = false;
                _ = Task.Delay(DebounceMs).ContinueWith(__ => MainThread.BeginInvokeOnMainThread(() => ui.Enabled = view.IsEnabled));
            }, UIControlEvent.TouchUpInside);
        });

        // anything with a tap (rows, cards, pills, sidebar items, tabs, chips) gets the same pointer style
        ViewHandler.ViewMapper.AppendToMapping("ClinicTapPointer", (handler, view) =>
        {
            if (view is not Microsoft.Maui.Controls.View v) return;
            if (!v.GestureRecognizers.OfType<TapGestureRecognizer>().Any()) return;
            if (handler.PlatformView is UIView ui) AddPointer(ui);
        });
    }
}
//JJQR - END
