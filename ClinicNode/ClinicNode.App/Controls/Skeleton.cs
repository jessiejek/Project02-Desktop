//JJQR - AI (Nov 22, 2026) - owner request: skeleton (pulsing grey placeholder) instead of plain "Loading..." text, because the live server can take 1-2 s per call.
namespace ClinicNode.App.Controls;

/// <summary>A pulsing grey bar. Stack several with <see cref="Rows"/> / <see cref="Card"/> to stand in for a list or a panel while data loads.</summary>
public sealed class SkeletonBar : BoxView
{
    public SkeletonBar(double height = 14, double width = -1, double radius = 6)
    {
        HeightRequest = height;
        if (width > 0) { WidthRequest = width; HorizontalOptions = LayoutOptions.Start; }
        CornerRadius = radius;
        Color = ClinicTheme.Color("SurfaceContainerHigh");
        SemanticProperties.SetDescription(this, "Loading");
        Loaded += (_, _) => new Animation(v => Opacity = v, 0.45, 1).Commit(this, "skeleton-pulse", 16, 900, Easing.SinInOut, repeat: () => true);
        Unloaded += (_, _) => this.AbortAnimation("skeleton-pulse");
    }
}

public static class Skeleton
{
    /// <summary>Covers the page with a skeleton and returns the action that removes it (also removed by itself after 15 s). For pages that load by their own code, not through a base class.</summary>
    public static Action CoverPage(ContentPage page)
    {
        var inner = page.Content;
        if (inner is null) return () => { };
        var stack = new VerticalStackLayout { Spacing = 24, Padding = new Thickness(24), MaximumWidthRequest = 900, HorizontalOptions = LayoutOptions.Center };
        stack.Children.Add(new SkeletonBar(24, 240));
        stack.Children.Add(Card(3));
        stack.Children.Add(Rows(5));
        var overlay = new Grid { BackgroundColor = ClinicTheme.Color("Background"), Children = { stack } };
        page.Content = null;
        page.Content = new Grid { Children = { inner, overlay } };
        void Hide() => overlay.IsVisible = false;
        page.Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () => { Hide(); return false; });
        return Hide;
    }

    /// <summary>Admin pages: wraps the page content ONCE with a skeleton that shows until the page's view-model finishes its first load (IsFirstLoad), or after 15 s at the latest.</summary>
    public static void WrapAdminPage(ContentPage page)
    {
        if (page.Content is null || page.ClassId == "skeleton-wrapped") return;
        page.ClassId = "skeleton-wrapped";
        var wrapped = Overlay(page.Content, "IsFirstLoad", 3);
        page.Content = null;
        page.Content = wrapped;
        if (wrapped is Grid g && g.Children.Count > 1 && g.Children[1] is View overlay)
            page.Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () => { overlay.IsVisible = false; return false; });
    }

    /// <summary>Puts a skeleton panel over <paramref name="content"/> while the bound bool (e.g. IsFirstLoad) is true, so a form never shows empty boxes while it loads.</summary>
    public static View Overlay(View content, string isLoadingPath, int cards = 3)
    {
        var host = new VerticalStackLayout { Spacing = 24, Padding = new Thickness(24, 24), MaximumWidthRequest = 720, HorizontalOptions = LayoutOptions.Center };
        for (var i = 0; i < cards; i++) host.Children.Add(Card(3));
        var overlay = new Grid { BackgroundColor = ClinicTheme.Color("Background"), Children = { host } };
        overlay.SetBinding(VisualElement.IsVisibleProperty, isLoadingPath);
        return new Grid { Children = { content, overlay } };
    }

    /// <summary>n list rows: a wide bar and a shorter one under it, like a name and a detail line.</summary>
    public static View Rows(int n = 5) 
    {
        var col = new VerticalStackLayout { Spacing = 14, Padding = new Thickness(24, 16) };
        for (var i = 0; i < n; i++)
        {
            col.Children.Add(new VerticalStackLayout
            {
                Spacing = 6,
                Children = { new SkeletonBar(14) { HorizontalOptions = LayoutOptions.Fill }, new SkeletonBar(10, 180 + (i % 3) * 40) },
            });
        }
        return col;
    }

    /// <summary>A panel: a title bar and a few text lines.</summary>
    public static View Card(int lines = 3)
    {
        var col = new VerticalStackLayout { Spacing = 10, Padding = new Thickness(0, 4) };
        col.Children.Add(new SkeletonBar(18, 160));
        for (var i = 0; i < lines; i++) col.Children.Add(new SkeletonBar(12) { HorizontalOptions = LayoutOptions.Fill });
        return col;
    }
}
//JJQR - END
