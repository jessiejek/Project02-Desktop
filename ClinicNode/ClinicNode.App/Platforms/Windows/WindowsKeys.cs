//JJQR - AI (Nov 23, 2026) - Windows keyboard conventions. WINDOWS ONLY: this file is under Platforms/Windows, so the Mac build never compiles it.
// Esc: closes the open pop-up (calendar, list, name menu) first; otherwise the top dialog, through that dialog's OWN close action
//      (so "no closing while saving" and the cancel result of each dialog are kept). Dialogs without a close button are left alone.
// Enter: in a one-line field of a dialog, presses that dialog's single blue (PrimaryButton) button. Never in a multi-line box,
//      never a red (DangerButton) button, never when there is more than one blue button or it is disabled. Owner approved 23 Nov 2026.
using ClinicNode.App.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace ClinicNode.App.WinUI;

public static class WindowsKeys
{
    sealed record Dialog(WeakReference<Grid> Scrim, Func<Task>? OnClose, WeakReference<View>? Footer);

    static readonly List<Dialog> Dialogs = new();
    static DateTime _lastEnter = DateTime.MinValue;

    /// <summary>Called by ClinicModal.Build (inside #if WINDOWS) for every dialog frame.</summary>
    public static void RegisterDialog(Grid scrim, Func<Task>? onClose, View? footer)
    {
        lock (Dialogs)
        {
            Dialogs.RemoveAll(d => !d.Scrim.TryGetTarget(out _));
            Dialogs.Add(new Dialog(new WeakReference<Grid>(scrim), onClose, footer is null ? null : new WeakReference<View>(footer)));
        }
    }

    public static void Attach(Microsoft.UI.Xaml.Window window)
    {
        var attached = false;
        void Hook()
        {
            if (attached || window.Content is not Microsoft.UI.Xaml.UIElement root) return;
            attached = true;
            root.PreviewKeyDown += OnPreviewKeyDown;
        }
        window.Activated += (_, _) => Hook();
        Hook();
    }

    static void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            if (e.Key == VirtualKey.Escape)
            {
                if (TryEscape()) e.Handled = true;
            }
            else if (e.Key == VirtualKey.Enter && IsOneLineField(e.OriginalSource))
            {
                if (TryEnter()) e.Handled = true;
            }
        }
        catch { /* a key press must never crash the app */ }
    }

    static bool IsOneLineField(object source) => source switch
    {
        Microsoft.UI.Xaml.Controls.TextBox tb => !tb.AcceptsReturn,
        Microsoft.UI.Xaml.Controls.PasswordBox => true,
        _ => false,
    };

    static ContentPage? TopPage()
    {
        var nav = Shell.Current?.Navigation ?? Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (nav?.ModalStack.LastOrDefault() is { } modal)
            return modal as ContentPage ?? (modal as NavigationPage)?.CurrentPage as ContentPage;
        return Shell.Current?.CurrentPage as ContentPage;
    }

    static ContentPage? HostPage(Element e) { for (Element? x = e; x is not null; x = x.Parent) if (x is ContentPage cp) return cp; return null; }

    static Dialog? FindDialog(ContentPage page)
    {
        lock (Dialogs)
            return Dialogs.LastOrDefault(d => d.Scrim.TryGetTarget(out var s) && ReferenceEquals(HostPage(s), page));
    }

    // tree changes run on the next UI turn, never inside the key event that caused them (WinUI can crash otherwise)
    static void Later(Action a) => Application.Current?.Dispatcher.Dispatch(a);

    static bool TryEscape()
    {
        if (TopPage() is not { } page) return false;
        if (ClinicPopup.IsOpen(page))
        {
            Later(() => ClinicPopup.Hide(page));
            return true;
        }
        if (FindDialog(page)?.OnClose is { } close)
        {
            Later(() => _ = close());
            return true;
        }
        return false;
    }

    static bool IsShown(Element e) { for (Element? x = e; x is not null; x = x.Parent) if (x is VisualElement { IsVisible: false }) return false; return true; }

    static bool TryEnter()
    {
        if (TopPage() is not { } page || ClinicPopup.IsOpen(page)) return false;
        if (FindDialog(page)?.Footer is not { } fref || !fref.TryGetTarget(out var footer)) return false;
        var primary = ClinicTheme.Style("PrimaryButton");
        if (primary is null) return false;
        var buttons = footer.GetVisualTreeDescendants().OfType<Button>()
            .Where(b => ReferenceEquals(b.Style, primary) && b.IsEnabled && IsShown(b))
            .ToList();
        if (buttons.Count != 1) return false;
        if ((DateTime.UtcNow - _lastEnter).TotalMilliseconds < 800) return true; // same double-press guard as the buttons
        _lastEnter = DateTime.UtcNow;
        var button = buttons[0];
        Later(() => { if (button.IsEnabled) ((IButtonController)button).SendClicked(); });
        return true;
    }
}
//JJQR - END
