//JJQR - AI (Nov 23, 2026) - owner: "the Windows version is too big". The Mac draws the whole app at 77% (Platforms/MacCatalyst/Info.plist UIDeviceFamily 2 = "Scaled to match iPad"); Windows drew it at 100% plus the display scaling (125% / 150%).
// WINDOWS ONLY: this file is under Platforms/Windows, so the Mac build never compiles it.
// It shrinks the PAGE AREA of every window (the main screen and every dialog) by ClinicTheme.WindowsUiScale, like a zoom: the page is laid out larger
// (window size / scale) and drawn scaled down, so every font, padding, fixed width and table shrinks by the same amount and keeps its proportions.
// The Windows title bar (minimise / maximise / close) and native Windows alerts are not scaled.
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WElement = Microsoft.UI.Xaml.FrameworkElement;
using WThickness = Microsoft.UI.Xaml.Thickness;

namespace ClinicNode.App.WinUI;

public static class WindowsUiScale
{
    public static void Attach(Microsoft.UI.Xaml.Window window)
    {
        var attached = false;
        void Hook()
        {
            if (attached || window.Content is not Panel container) return;
            attached = true;
            // LayoutUpdated also catches dialogs (each one is a new child of this panel) and window resizes / DPI changes.
            container.LayoutUpdated += (_, _) => ApplyAll(container);
            ApplyAll(container);
        }
        window.Activated += (_, _) => Hook();
        Hook();
    }

    static void ApplyAll(Panel container)
    {
        try
        {
            foreach (var child in container.Children)
                if (child is Microsoft.Maui.Platform.WindowRootView root && root.Content is WElement page)
                    Apply(page);
        }
        catch { /* a cosmetic size must never break the window */ }
    }

    static void Apply(WElement page)
    {
        if (VisualTreeHelper.GetParent(page) is not WElement slot) return;
        var w = slot.ActualWidth;
        var h = slot.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var s = ClinicNode.App.Controls.ClinicTheme.WindowsUiScale(page.XamlRoot?.RasterizationScale ?? 1.0);
        // MAUI keeps the page clear of the title bar with a top content margin; that margin is inside the scaled page, so move the page down by the part the scale took away.
        var titleBar = Microsoft.UI.Xaml.Application.Current?.Resources.TryGetValue("NavigationViewContentMargin", out var r) == true && r is WThickness t ? t.Top : 0;
        var top = Math.Round(titleBar * (1 - s), 2);
        var layoutW = w / s;
        var layoutH = (h - top) / s;
        // negative right / bottom margins let the page be laid out bigger than its slot; the scale then draws it back inside the slot
        var margin = new WThickness(0, top, Math.Round(w - layoutW, 2), Math.Round(h - top - layoutH, 2));
        if (page.RenderTransform is not ScaleTransform st || Math.Abs(st.ScaleX - s) > 0.0001)
            page.RenderTransform = new ScaleTransform { ScaleX = s, ScaleY = s };
        if (page.RenderTransformOrigin != default)
            page.RenderTransformOrigin = default;
        var m = page.Margin;
        if (Math.Abs(m.Left - margin.Left) > 0.5 || Math.Abs(m.Top - margin.Top) > 0.5 || Math.Abs(m.Right - margin.Right) > 0.5 || Math.Abs(m.Bottom - margin.Bottom) > 0.5)
            page.Margin = margin;
    }
}
//JJQR - END
