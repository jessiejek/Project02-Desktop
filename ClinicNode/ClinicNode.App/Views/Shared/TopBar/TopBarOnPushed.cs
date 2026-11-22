//JJQR - AI (Nov 22, 2026) - owner: pages opened over another page (a visit, a chart, a consultation) must show the top bar with the connection ping.
// HISTORY: (1) setting Shell.TitleView from the shell's Navigated event crashed MAUI navigation on Mac Catalyst; (2) setting it in the page constructor did not show anything on the Mac.
// FINAL: the bar is placed INSIDE the page, as the first row of its content. No native title view is involved, so there is nothing to crash and nothing to fail to render.
using ClinicNode.App.Controls;

namespace ClinicNode.App.Views.Shared;

public static class TopBarOnPushed
{
    const string Marker = "topbar-in-page";

    /// <summary>For pages whose constructor has no service provider: takes it from the running app.</summary>
    public static void Attach(Page page)
    {
        var sp = IPlatformApplication.Current?.Services;
        if (sp is not null) Attach(page, sp);
    }

    public static void Attach(Page page, IServiceProvider sp)
    {
        if (page is not ContentPage cp) return;
        //JJQR - AI (Nov 22, 2026) - owner: "ping is at the VERY TOP". The native bar (empty white strip with only a back arrow) is hidden on these pages; our own first row holds the back arrow AND the top bar, in the same place as on Today.
        try { Shell.SetNavBarIsVisible(cp, false); } catch { /* keep the native bar if it cannot be hidden */ }
        //JJQR - END
        void Wrap()
        {
            try
            {
                if (cp.Content is null || cp.ClassId == Marker) return;
                var inner = cp.Content;
                cp.Content = null;
                var bar = new TopBarView(sp) { HeightRequest = 52, VerticalOptions = LayoutOptions.Center };
                var back = new Button { Text = "←  Back", HeightRequest = 36, Padding = new Thickness(12, 0), Margin = new Thickness(16, 0, 8, 0), VerticalOptions = LayoutOptions.Center };
                back.Apply("GhostButton");
                back.Clicked += async (_, _) =>
                {
                    // the page's own back logic (e.g. the consultation's "discard changes?") wins; otherwise a plain Back
                    if (Shell.GetBackButtonBehavior(cp)?.Command is { } cmd && cmd.CanExecute(null)) { cmd.Execute(null); return; }
                    try { await Shell.Current.GoToAsync(".."); } catch { /* already leaving */ }
                };
                var line = new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant"), VerticalOptions = LayoutOptions.End };
                var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 0 };
                row.Add(back, 0, 0);
                row.Add(bar, 1, 0);
                var header = new Grid { BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"), Children = { row, line } };
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
