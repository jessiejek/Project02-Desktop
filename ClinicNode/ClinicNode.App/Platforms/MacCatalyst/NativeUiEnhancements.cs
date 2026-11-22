//JJQR - AI (Nov 22, 2026) - owner requests: (1) a pointing-hand style on everything clickable, (2) a second quick click on the same button must do nothing.
// Done ONCE for the whole app here, not per page.
using Microsoft.Maui.Handlers;
using UIKit;

namespace ClinicNode.App.Platforms.MacCatalyst;

public static class NativeUiEnhancements
{
    /// <summary>The button stays disabled this long after a click, so a double click (or a nervous second click) cannot start the same work twice.</summary>
    const int DebounceMs = 800;

    sealed class HoverPointer : UIPointerInteractionDelegate
    {
        public override UIPointerStyle? GetStyleForRegion(UIPointerInteraction interaction, UIPointerRegion region)
        {
            var view = interaction.View;
            if (view is null) return null;
            if (view is UIControl { Enabled: false }) return null; // disabled things do not get the hand
            return UIPointerStyle.Create(UIPointerHoverEffect.Create(new UITargetedPreview(view)), (UIPointerShape?)null);
        }
    }

    static readonly HoverPointer Pointer = new();

    static void AddPointer(UIView view)
    {
        if (view.Interactions.OfType<UIPointerInteraction>().Any()) return;
        view.AddInteraction(new UIPointerInteraction(Pointer));
    }

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
