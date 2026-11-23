//JJQR - AI (Nov 23, 2026) - Windows twin of Platforms/MacCatalyst/NativeUiEnhancements.cs. WINDOWS ONLY: this file is under Platforms/Windows, so the Mac build never compiles it.
// (1) a second quick click on the same button does nothing (same 800 ms as the Mac), (2) a pointing hand over buttons and anything with a tap, (3) a light hover tint on buttons.
// Also hooks the Windows-only UI scale (WindowsUiScale.cs) and the Esc / Enter keys (WindowsKeys.cs) to every window. Registered once from MauiProgram inside #if WINDOWS.
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.LifecycleEvents;
using Microsoft.UI.Input;

namespace ClinicNode.App.WinUI;

public static class NativeUiEnhancements
{
    const int DebounceMs = 800;
    static readonly ConditionalWeakTable<Microsoft.UI.Xaml.Controls.Button, object> Done = new();
    // UIElement.ProtectedCursor is the only way to set the mouse cursor on a WinUI element; it is protected, so it is set by reflection.
    static readonly PropertyInfo? CursorProperty = typeof(Microsoft.UI.Xaml.UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    static void SetHand(Microsoft.UI.Xaml.UIElement element)
    {
        try { CursorProperty?.SetValue(element, InputSystemCursor.Create(InputSystemCursorShape.Hand)); }
        catch (Exception) { /* the cursor shape is cosmetic; never break the app for it */ }
    }

    public static void Register(MauiAppBuilder builder)
    {
        ButtonHandler.Mapper.AppendToMapping("ClinicClickGuardAndPointer", (handler, view) =>
        {
            if (handler.PlatformView is not Microsoft.UI.Xaml.Controls.Button b) return;
            if (Done.TryGetValue(b, out _)) return;
            Done.Add(b, new object());
            SetHand(b);
            // MAUI's own Click handler was added first, so this runs after the click was delivered: ignore further clicks for 800 ms.
            // IsHitTestVisible (not IsEnabled) so the button does not flash grey.
            b.Click += (_, _) =>
            {
                b.IsHitTestVisible = false;
                _ = Task.Delay(DebounceMs).ContinueWith(_ => b.DispatcherQueue?.TryEnqueue(() => b.IsHitTestVisible = !view.InputTransparent));
            };
            b.PointerEntered += (_, _) => { if (b.IsEnabled) b.Opacity = view.Opacity * 0.88; };
            b.PointerExited += (_, _) => b.Opacity = view.Opacity;
            b.PointerCaptureLost += (_, _) => b.Opacity = view.Opacity;
        });

        // anything with a tap (rows, cards, pills, sidebar items, tabs, chips) gets the same pointing hand, as on the Mac
        ViewHandler.ViewMapper.AppendToMapping("ClinicTapPointer", (handler, view) =>
        {
            if (view is not Microsoft.Maui.Controls.View v) return;
            if (!v.GestureRecognizers.OfType<TapGestureRecognizer>().Any()) return;
            if (handler.PlatformView is Microsoft.UI.Xaml.UIElement ui) SetHand(ui);
            if (handler.ContainerView is Microsoft.UI.Xaml.UIElement container) SetHand(container);
        });

        builder.ConfigureLifecycleEvents(events => events.AddWindows(w => w.OnWindowCreated(window =>
        {
            WindowsUiScale.Attach(window);
            WindowsKeys.Attach(window);
        })));
    }
}
//JJQR - END
