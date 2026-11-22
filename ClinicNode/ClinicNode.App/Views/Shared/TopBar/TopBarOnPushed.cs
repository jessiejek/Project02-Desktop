//JJQR - AI (Nov 22, 2026) - owner: pages opened over another page (a visit, a chart, a consultation) must show the top bar with the connection ping.
// HISTORY: (1) setting Shell.TitleView from the shell's Navigated event crashed MAUI navigation on Mac Catalyst; (2) setting it in the page constructor did not show anything on the Mac.
// FINAL: the bar is placed INSIDE the page, as the first row of its content. No native title view is involved, so there is nothing to crash and nothing to fail to render.
using ClinicNode.App.Controls;

namespace ClinicNode.App.Views.Shared;

public static class TopBarOnPushed
{
    const string Marker = "topbar-in-page";

    public static void Attach(Page page, IServiceProvider sp)
    {
        if (page is not ContentPage cp) return;
        void Wrap()
        {
            try
            {
                if (cp.Content is null || cp.ClassId == Marker) return;
                var inner = cp.Content;
                cp.Content = null;
                var bar = new TopBarView(sp) { HeightRequest = 52, BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"), VerticalOptions = LayoutOptions.Center };
                var line = new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant"), VerticalOptions = LayoutOptions.End };
                var header = new Grid { BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"), Children = { bar, line } };
                var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 0 };
                root.Add(header, 0, 0);
                root.Add(inner, 0, 1);
                cp.Content = root;
                cp.ClassId = Marker;
            }
            catch { /* the bar must never stop a page from opening */ }
        }
        void OnAppearing(object? s, EventArgs e) { cp.Appearing -= OnAppearing; Wrap(); }
        cp.Appearing += OnAppearing;
    }
}
//JJQR - END
