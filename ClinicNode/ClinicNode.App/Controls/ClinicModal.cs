//JJQR - AI (Nov 21, 2026) - one modal frame for every dialog: dimmed backdrop, floating card with shadow, tinted title bar with a close button, grey footer.
using ClinicNode.App.Controls;

namespace ClinicNode.App.Controls;

public static class ClinicModal
{
    public static readonly Color Backdrop = Color.FromArgb("#B3102A28");

    /// <summary>Backdrop grid holding the modal card. <paramref name="footer"/> may be null (no footer bar).</summary>
    public static Grid Build(string title, View body, View? footer, Func<Task>? onClose, double maxWidth = 560, double maxBodyHeight = 520)
    {
        var closeBtn = new Button
        {
            Text = "✕",
            FontSize = 15,
            BackgroundColor = Colors.Transparent,
            TextColor = ClinicTheme.Color("OnSurfaceVariant"),
            BorderWidth = 0,
            Padding = new Thickness(0),
            WidthRequest = 34,
            HeightRequest = 34,
            MinimumHeightRequest = 34,
            MinimumWidthRequest = 34,
            CornerRadius = 17,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
        };
        SemanticProperties.SetDescription(closeBtn, "Close");
        ToolTipProperties.SetText(closeBtn, "Close");
        if (onClose is not null) closeBtn.Clicked += async (_, _) => await onClose();
        else closeBtn.IsVisible = false;

        var titleLabel = new Label
        {
            Text = title,
            FontFamily = ClinicTheme.FontSemiBold,
            FontSize = 17,
            TextColor = ClinicTheme.Color("OnSurface"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        var header = new Grid
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"),
            Padding = new Thickness(22, 12, 14, 12),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        header.Add(titleLabel, 0, 0);
        header.Add(closeBtn, 1, 0);

        var rows = new VerticalStackLayout { Spacing = 0 };
        rows.Children.Add(header);
        rows.Children.Add(Line());
        rows.Children.Add(new ScrollView { MaximumHeightRequest = maxBodyHeight, Content = new ContentView { Padding = new Thickness(22, 18), Content = body } });
        if (footer is not null)
        {
            rows.Children.Add(Line());
            rows.Children.Add(new Grid { BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"), Padding = new Thickness(22, 12), Children = { footer } });
        }

        var card = new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
            Stroke = ClinicTheme.Color("Outline").WithAlpha(0.45f),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = 0,
            MaximumWidthRequest = maxWidth,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(28),
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.38f, Radius = 32, Offset = new Point(0, 12) },
            Content = rows,
        };
        var scrim = new Grid { BackgroundColor = Backdrop };
        scrim.Children.Add(card);
        //JJQR - AI (Nov 21, 2026) - dialogs take a proper width (up to maxWidth) instead of shrinking to their content; on narrow windows they leave a margin on each side
        scrim.SizeChanged += (_, _) => { if (scrim.Width > 0) card.WidthRequest = Math.Max(320, Math.Min(maxWidth, scrim.Width - 56)); };
        //JJQR - END
#if WINDOWS
        //JJQR - AI (Nov 23, 2026) - WINDOWS ONLY: a tall dialog (e.g. Register a new patient, Connection) ran off the bottom of a laptop screen and hid its buttons.
        // Cap the scrolling body to the room the window really has, so the body scrolls and the title and footer buttons stay visible. Also lets Esc / Enter find this dialog.
        var bodyScroll = rows.Children.OfType<ScrollView>().First();
        scrim.SizeChanged += (_, _) =>
        {
            if (scrim.Height <= 0) return;
            double others = 0;
            foreach (var c in rows.Children) if (!ReferenceEquals(c, bodyScroll) && c is View v && v.Height > 0) others += v.Height;
            if (others <= 0) others = 59 + (footer is not null ? 61 : 0); // title bar 58 + line, footer 60 + line, before the first layout
            bodyScroll.MaximumHeightRequest = Math.Max(120, Math.Min(maxBodyHeight, scrim.Height - 56 - others));
        };
        ClinicNode.App.WinUI.WindowsKeys.RegisterDialog(scrim, onClose, footer);
        //JJQR - END
#endif
        return scrim;
    }

    static BoxView Line() => new() { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") };
}
//JJQR - END
