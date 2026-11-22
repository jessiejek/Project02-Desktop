// JJQR - AI (Nov 21, 2026) - 260px sidebar matching Project02-fe Sidebar.tsx: light for doctor, inverse-surface for staff/admin (brand accent, not system dark mode).
using ClinicApp.Domain.Enums;

namespace ClinicNode.App.Controls;

public sealed record ClinicNavItem(string Title, string Route, bool Built, string Icon = "");

public sealed record ClinicNavGroup(string? Label, IReadOnlyList<ClinicNavItem> Items);

public sealed class ClinicSidebar : Grid
{
    private readonly AppShell _shell;
    private readonly IReadOnlyList<ClinicNavGroup> _groups;
    private readonly bool _light;
    private readonly Color _accent;
    private readonly Color _accentText;
    private string _activeRoute;

    public ClinicSidebar(AppShell shell, UserRole role, IReadOnlyList<ClinicNavGroup> groups, string activeRoute)
    {
        _shell = shell;
        _groups = groups;
        _activeRoute = activeRoute;
        _light = false; //JJQR - AI (Nov 21, 2026) desktop redesign: one dark teal sidebar for every role //JJQR - END
        //JJQR - AI (Nov 21, 2026) desktop redesign colours
        _accent = ClinicTheme.Color("SidebarAccent");
        _accentText = Colors.White;
        //JJQR - END

        RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Star),
            new(GridLength.Auto),
        };
        BackgroundColor = ClinicTheme.Color("SidebarBg"); //JJQR - AI (Nov 21, 2026) //JJQR - END
        Padding = new Thickness(0, 24, 0, 16);
        WidthRequest = ClinicTheme.SidebarWidth;
        //JJQR - AI (Nov 21, 2026) - fill the flyout column
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        //JJQR - END

        var titleColor = Colors.White; //JJQR - AI (Nov 21, 2026) //JJQR - END
        var subtitleColor = ClinicTheme.Color("SidebarMuted"); //JJQR - AI (Nov 21, 2026) //JJQR - END
        var header = new VerticalStackLayout
        {
            Padding = new Thickness(24, 0, 24, 16),
            Spacing = 4,
            Children =
            {
                new Label
                {
                    Text = "Dr. Grace Gavino",
                    FontFamily = ClinicTheme.FontSemiBold,
                    FontSize = 17,
                    TextColor = titleColor,
                    LineBreakMode = LineBreakMode.TailTruncation,
                },
                new Label
                {
                    Text = ClinicTheme.RoleSubtitle(role),
                    FontFamily = ClinicTheme.FontMedium,
                    FontSize = 12,
                    TextColor = subtitleColor,
                    CharacterSpacing = _light ? 0 : 1.2,
                },
            },
        };
        if (!_light)
            ((Label)header.Children[1]).Text = ClinicTheme.RoleSubtitle(role).ToUpperInvariant();

        var nav = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(8, 0) };
        foreach (var group in _groups)
        {
            var block = new VerticalStackLayout { Spacing = 4 };
            if (!string.IsNullOrWhiteSpace(group.Label))
            {
                block.Children.Add(new Label
                {
                    Text = _light ? group.Label : group.Label.ToUpperInvariant(),
                    FontFamily = ClinicTheme.FontMedium,
                    FontSize = 12,
                    TextColor = subtitleColor,
                    CharacterSpacing = _light ? 0 : 1.2,
                    Padding = new Thickness(16, 8, 16, 0),
                });
            }
            foreach (var item in group.Items)
                block.Children.Add(BuildItem(item));
            nav.Children.Add(block);
        }

        var logout = BuildRow("Logout", "logout", isActive: false, action: async () => await _shell.UserSignOutAsync()); //JJQR - AI (Nov 22, 2026) - step 3.5 //JJQR - END
        var footer = new VerticalStackLayout
        {
            Padding = new Thickness(8, 16, 8, 0),
            Children =
            {
                new BoxView
                {
                    HeightRequest = 1,
                    Color = ClinicTheme.Color("SidebarMuted").WithAlpha(0.25f), //JJQR - AI (Nov 21, 2026) //JJQR - END
                    Margin = new Thickness(8, 0, 8, 12),
                },
                logout,
            },
        };

        var scroll = new ScrollView { Content = nav, HorizontalScrollBarVisibility = ScrollBarVisibility.Never };
        Children.Add(header);
        Grid.SetRow(header, 0);
        Children.Add(scroll);
        Grid.SetRow(scroll, 1);
        Children.Add(footer);
        Grid.SetRow(footer, 2);
    }

    public void SetActiveRoute(string route)
    {
        _activeRoute = route.TrimStart('/');
        // Rebuild item visual state by reconstructing the nav column.
        if (Children.Count < 2 || Children[1] is not ScrollView scroll || scroll.Content is not VerticalStackLayout nav)
            return;
        nav.Children.Clear();
        foreach (var group in _groups)
        {
            var block = new VerticalStackLayout { Spacing = 4 };
            if (!string.IsNullOrWhiteSpace(group.Label))
            {
                var subtitleColor = ClinicTheme.Color("SidebarMuted"); //JJQR - AI (Nov 21, 2026) //JJQR - END
                block.Children.Add(new Label
                {
                    Text = _light ? group.Label : group.Label.ToUpperInvariant(),
                    FontFamily = ClinicTheme.FontMedium,
                    FontSize = 12,
                    TextColor = subtitleColor,
                    CharacterSpacing = _light ? 0 : 1.2,
                    Padding = new Thickness(16, 8, 16, 0),
                });
            }
            foreach (var item in group.Items)
                block.Children.Add(BuildItem(item));
            nav.Children.Add(block);
        }
    }

    View BuildItem(ClinicNavItem item)
    {
        var active = RoutesMatch(_activeRoute, item.Route);
        return BuildRow(item.Title, item.Icon, active, async () =>
        {
            try { await _shell.GoToAsync("//" + item.Route); }
            catch (Exception ex) { await _shell.DisplayAlertAsync("Navigation", ex.Message, "OK"); }
            SetActiveRoute(item.Route);
        });
    }

    View BuildRow(string title, string iconName, bool isActive, Func<Task> action)
    {
        Color text;
        Color fill;
        if (_light)
        {
            text = isActive ? ClinicTheme.Color("Primary") : ClinicTheme.Color("OnSurfaceVariant");
            fill = isActive ? ClinicTheme.Color("PrimarySubtle") : Colors.Transparent;
        }
        else
        {
            text = isActive ? Colors.White : ClinicTheme.Color("SidebarText"); //JJQR - AI (Nov 21, 2026) //JJQR - END
            fill = isActive ? ClinicTheme.Color("SidebarActive") : Colors.Transparent; //JJQR - AI (Nov 21, 2026) //JJQR - END
        }

        var indicator = new BoxView
        {
            WidthRequest = 3,
            Color = _accent,
            Opacity = isActive ? 1 : 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Fill,
        };
        var label = new Label
        {
            Text = title,
            FontFamily = isActive ? ClinicTheme.FontSemiBold : ClinicTheme.FontMedium,
            FontSize = 14,
            TextColor = text,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
        };
        var content = new HorizontalStackLayout
        {
            Spacing = 16,
            Padding = new Thickness(16, 0, 16, 0),
            VerticalOptions = LayoutOptions.Center,
            Children = { label },
        };
        if (!string.IsNullOrWhiteSpace(iconName))
        {
            content.Children.Insert(0, new ClinicIcon
            {
                Name = iconName,
                IconSize = ClinicIcons.SizeSm,
                IconColor = text,
                VerticalOptions = LayoutOptions.Center,
            });
        }
        var grid = new Grid
        {
            MinimumHeightRequest = ClinicTheme.MinTouch,
            BackgroundColor = fill,
            Children = { indicator, content },
        };
        var border = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = ClinicTheme.RadiusNav },
            Padding = 0,
            BackgroundColor = fill,
            Content = grid,
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await action();
        border.GestureRecognizers.Add(tap);
        return border;
    }

    static bool RoutesMatch(string current, string route)
    {
        current = current.TrimStart('/');
        route = route.TrimStart('/');
        if (string.Equals(current, route, StringComparison.OrdinalIgnoreCase)) return true;
        if (route == "today" && current.StartsWith("consult", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
// JJQR - END
