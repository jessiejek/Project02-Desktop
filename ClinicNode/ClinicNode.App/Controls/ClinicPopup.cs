//JJQR - AI (Nov 21, 2026) - shared floating popup layer: any page can open a panel (dropdown, calendar, time list) that floats OVER its content, anchored under a control.
using System.Globalization;

namespace ClinicNode.App.Controls;

public static class ClinicPopup
{
    const string RootId = "clinic-popup-root";
    const string LayerId = "clinic-popup-layer";

    static (Grid Root, AbsoluteLayout Layer)? Ensure(ContentPage page)
    {
        if (page.Content is Grid g && g.ClassId == RootId)
            return (g, g.Children.OfType<AbsoluteLayout>().First(l => l.ClassId == LayerId));
        var original = page.Content;
        if (original is null) return null;
        page.Content = null;
        var root = new Grid { ClassId = RootId };
        root.Children.Add(original);
        var layer = new AbsoluteLayout { ClassId = LayerId, IsVisible = false, BackgroundColor = Colors.Transparent, ZIndex = 100 };
        root.Children.Add(layer);
        page.Content = root;
        return (root, layer);
    }

    static Point PositionIn(Grid root, VisualElement v)
    {
        double x = 0, y = 0;
        Element? e = v;
        while (e is VisualElement ve && !ReferenceEquals(ve, root))
        {
            x += ve.X; y += ve.Y;
            if (ve.Parent is ScrollView sv) { x -= sv.ScrollX; y -= sv.ScrollY; }
            e = ve.Parent;
        }
        return new Point(x, y);
    }

    public static void Show(ContentPage page, VisualElement anchor, View panel, double width, double estimatedHeight = 340)
    {
        if (Ensure(page) is not { } host) return;
        var (root, layer) = host;
        layer.Children.Clear();
        var scrim = new BoxView { Color = Colors.Transparent };
        scrim.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Hide(page)) });
        AbsoluteLayout.SetLayoutBounds(scrim, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(scrim, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
        layer.Children.Add(scrim);
        var pos = PositionIn(root, anchor);
        var x = Math.Max(8, Math.Min(pos.X, Math.Max(8, root.Width - width - 12)));
        var below = pos.Y + anchor.Height + 4;
        var y = below + estimatedHeight > root.Height ? Math.Max(8, pos.Y - estimatedHeight - 4) : below;
        AbsoluteLayout.SetLayoutBounds(panel, new Rect(x, y, width, AbsoluteLayout.AutoSize));
        AbsoluteLayout.SetLayoutFlags(panel, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        layer.Children.Add(panel);
        layer.IsVisible = true;
    }

    public static void Hide(ContentPage page)
    {
        if (page.Content is Grid g && g.ClassId == RootId && g.Children.OfType<AbsoluteLayout>().FirstOrDefault(l => l.ClassId == LayerId) is { } layer)
        {
            layer.IsVisible = false;
            layer.Children.Clear();
        }
    }
}

public static class ClinicPickers
{
    /// <summary>The ContentPage (e.g. the open dialog) that a control currently lives on, so its popup floats over the right page.</summary>
    static ContentPage? HostPage(Element e) { for (Element? x = e; x is not null; x = x.Parent) if (x is ContentPage cp) return cp; return null; }

    public static View DateBox(ContentPage? page, DatePicker dp, DateTime? startView = null, bool fill = false)
    {
        dp.IsVisible = false;
        var icon = new ClinicNode.App.Controls.ClinicIcon { Name = "calendar_today", IconSize = ClinicNode.App.Controls.ClinicIcons.SizeSm, IconColor = ClinicTheme.Color("Primary"), VerticalOptions = LayoutOptions.Center };
        var text = new Label { FontSize = 14, FontFamily = ClinicTheme.FontMedium, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.NoWrap };
        var arrow = new Label { Text = "▾", FontSize = 14, TextColor = ClinicTheme.Color("Primary"), VerticalOptions = LayoutOptions.Center };
        void Refresh()
        {
            if (dp.Date is { } d) { text.Text = d.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture); text.TextColor = ClinicTheme.Color("OnSurface"); }
            else { text.Text = "Choose a date"; text.TextColor = ClinicTheme.Color("OnSurfaceVariant"); }
        }
        Refresh();
        dp.DateSelected += (_, _) => Refresh();
        var grid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(icon, 0, 0);
        grid.Add(text, 1, 0);
        grid.Add(arrow, 2, 0);
        var box = new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
            Stroke = ClinicTheme.Color("Primary").WithAlpha(0.55f),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12, 9),
            MinimumWidthRequest = fill ? 0 : 220,
            HorizontalOptions = fill ? LayoutOptions.Fill : LayoutOptions.Start,
            Content = grid,
        };
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (_, _) => box.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
        hover.PointerExited += (_, _) => box.BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest");
        box.GestureRecognizers.Add(hover);
        box.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => { var pg = page ?? HostPage(box); if (pg is null) return; ClinicPopup.Show(pg, box, BuildCalendar(dp.Date, dp.MinimumDate ?? new DateTime(1900, 1, 1), dp.MaximumDate ?? new DateTime(2200, 12, 31), startView, d => { ClinicPopup.Hide(pg); dp.Date = d; }), 316); })
        });
        return new VerticalStackLayout { Children = { box, dp } };
    }

    static View BuildCalendar(DateTime? selected, DateTime min, DateTime max, DateTime? startView, Action<DateTime> onPick)
    {
        var basis = selected ?? startView ?? DateTime.Today;
        var shown = new DateTime(basis.Year, basis.Month, 1);
        var yearMode = false;
        var yearPage = shown.Year - 5;
        var holder = new VerticalStackLayout { Spacing = 8 };

        static Border Hit(View content, Action tap, bool hover = true)
        {
            var b = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 }, Padding = new Thickness(10, 6), BackgroundColor = Colors.Transparent, Content = content };
            b.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(tap) });
            if (hover)
            {
                var h = new PointerGestureRecognizer();
                h.PointerEntered += (_, _) => b.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
                h.PointerExited += (_, _) => b.BackgroundColor = Colors.Transparent;
                b.GestureRecognizers.Add(h);
            }
            return b;
        }
        Label Glyph(string t) => new() { Text = t, FontSize = 16, TextColor = ClinicTheme.Color("Primary") };

        void Render()
        {
            holder.Children.Clear();
            var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            if (yearMode)
            {
                head.Add(Hit(Glyph("◀"), () => { yearPage -= 12; Render(); }), 0, 0);
                head.Add(new Label { Text = $"{yearPage} – {yearPage + 11}", FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center }, 1, 0);
                head.Add(Hit(Glyph("▶"), () => { yearPage += 12; Render(); }), 2, 0);
                holder.Children.Add(head);
                var yg = new Grid { ColumnSpacing = 4, RowSpacing = 4 };
                for (var c = 0; c < 4; c++) yg.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                for (var i = 0; i < 12; i++)
                {
                    var year = yearPage + i;
                    var off = year < min.Year || year > max.Year;
                    var isSel = year == shown.Year;
                    var lbl = new Label { Text = year.ToString(CultureInfo.InvariantCulture), FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center, FontFamily = isSel ? ClinicTheme.FontSemiBold : ClinicTheme.Font, TextColor = isSel ? Colors.White : off ? ClinicTheme.Color("OutlineVariant") : ClinicTheme.Color("OnSurface") };
                    var cell = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, HeightRequest = 40, BackgroundColor = isSel ? ClinicTheme.Color("Primary") : Colors.Transparent, Content = lbl };
                    if (!off)
                    {
                        cell.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { shown = new DateTime(year, shown.Month, 1); yearMode = false; Render(); }) });
                        if (!isSel)
                        {
                            var h = new PointerGestureRecognizer();
                            h.PointerEntered += (_, _) => cell.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
                            h.PointerExited += (_, _) => cell.BackgroundColor = Colors.Transparent;
                            cell.GestureRecognizers.Add(h);
                        }
                    }
                    yg.Add(cell, i % 4, i / 4);
                }
                holder.Children.Add(yg);
                holder.Children.Add(Hit(new Label { Text = "Back to days", FontFamily = ClinicTheme.FontSemiBold, FontSize = 13, TextColor = ClinicTheme.Color("Primary") }, () => { yearMode = false; Render(); }));
                return;
            }

            head.Add(Hit(Glyph("◀"), () => { shown = shown.AddMonths(-1); Render(); }), 0, 0);
            var title = Hit(new Label { Text = shown.ToString("MMMM yyyy", CultureInfo.InvariantCulture) + "  ▾", FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center }, () => { yearMode = true; yearPage = shown.Year - 5; Render(); });
            title.HorizontalOptions = LayoutOptions.Center;
            head.Add(title, 1, 0);
            head.Add(Hit(Glyph("▶"), () => { shown = shown.AddMonths(1); Render(); }), 2, 0);
            holder.Children.Add(head);

            var grid = new Grid { ColumnSpacing = 2, RowSpacing = 2 };
            for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var names = new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };
            for (var i = 0; i < 7; i++) grid.Add(new Label { Text = names[i], FontSize = 11, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("OnSurfaceVariant"), HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2) }, i, 0);
            var first = (int)shown.DayOfWeek;
            var days = DateTime.DaysInMonth(shown.Year, shown.Month);
            for (var d = 1; d <= days; d++)
            {
                var date = new DateTime(shown.Year, shown.Month, d);
                var idx = first + d - 1;
                var isSel = selected is { } sd && sd.Date == date;
                var isToday = date == DateTime.Today;
                var disabled = date < min.Date || date > max.Date;
                var lbl = new Label { Text = d.ToString(CultureInfo.InvariantCulture), FontSize = 13, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center, FontFamily = isSel ? ClinicTheme.FontSemiBold : ClinicTheme.Font, TextColor = isSel ? Colors.White : disabled ? ClinicTheme.Color("OutlineVariant") : ClinicTheme.Color("OnSurface") };
                var cell = new Border
                {
                    StrokeThickness = isToday && !isSel ? 1.5 : 0, Stroke = ClinicTheme.Color("Primary"),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, HeightRequest = 34,
                    BackgroundColor = isSel ? ClinicTheme.Color("Primary") : Colors.Transparent, Content = lbl,
                };
                if (!disabled)
                {
                    cell.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => onPick(date)) });
                    if (!isSel)
                    {
                        var h = new PointerGestureRecognizer();
                        h.PointerEntered += (_, _) => cell.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
                        h.PointerExited += (_, _) => cell.BackgroundColor = Colors.Transparent;
                        cell.GestureRecognizers.Add(h);
                    }
                }
                grid.Add(cell, idx % 7, 1 + idx / 7);
            }
            holder.Children.Add(grid);
            if (DateTime.Today >= min.Date && DateTime.Today <= max.Date)
                holder.Children.Add(Hit(new Label { Text = "Today", FontFamily = ClinicTheme.FontSemiBold, FontSize = 13, TextColor = ClinicTheme.Color("Primary") }, () => onPick(DateTime.Today)));
        }
        Render();
        return new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
            Stroke = ClinicTheme.Color("Outline").WithAlpha(0.5f), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12),
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.28f, Radius = 22, Offset = new Point(0, 8) },
            Content = holder,
        };
    }

    /// <summary>One checkbox row where the WHOLE row (box + title + hint) toggles it, with a hover highlight.</summary>
    public static View CheckRow(CheckBox box, string title, string? hint = null)
    {
        box.InputTransparent = true;
        box.VerticalOptions = LayoutOptions.Start;
        var text = new VerticalStackLayout { Spacing = 2, Children = { new Label { Text = title, FontSize = 14 } } };
        if (!string.IsNullOrWhiteSpace(hint)) text.Children.Add(new Label { Text = hint, FontSize = 12, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
        var g = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        g.Add(box, 0, 0); g.Add(text, 1, 0);
        var row = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Padding = new Thickness(8, 6), BackgroundColor = Colors.Transparent, Content = g };
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { if (box.IsEnabled) box.IsChecked = !box.IsChecked; }) });
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (_, _) => row.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
        hover.PointerExited += (_, _) => row.BackgroundColor = Colors.Transparent;
        row.GestureRecognizers.Add(hover);
        return row;
    }

    /// <summary>A time field: bordered box with a clock icon and ▾; clicking it floats a list of half-hour slots. The TimePicker stays as the value holder.</summary>
    public static View TimeBox(ContentPage? page, TimePicker tp, int fromHour = 5, int toHour = 23)
    {
        tp.IsVisible = false;
        var icon = new ClinicIcon { Name = "schedule", IconSize = ClinicIcons.SizeSm, IconColor = ClinicTheme.Color("Primary"), VerticalOptions = LayoutOptions.Center };
        var text = new Label { FontSize = 14, FontFamily = ClinicTheme.FontMedium, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.NoWrap };
        var arrow = new Label { Text = "▾", FontSize = 14, TextColor = ClinicTheme.Color("Primary"), VerticalOptions = LayoutOptions.Center };
        static string Fmt(TimeSpan t) => DateTime.Today.Add(t).ToString("h:mm tt", CultureInfo.InvariantCulture);
        var grid = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(icon, 0, 0); grid.Add(text, 1, 0); grid.Add(arrow, 2, 0);
        var box = new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
            Stroke = ClinicTheme.Color("Primary").WithAlpha(0.55f), StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 7), WidthRequest = 128, HorizontalOptions = LayoutOptions.Start, Content = grid,
        };
        void Refresh()
        {
            text.Text = Fmt(tp.Time ?? TimeSpan.Zero);
            box.Opacity = tp.IsEnabled ? 1 : 0.45;
        }
        Refresh();
        tp.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(TimePicker.Time) or nameof(TimePicker.IsEnabled)) Refresh(); };
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (_, _) => { if (tp.IsEnabled) box.BackgroundColor = ClinicTheme.Color("PrimarySubtle"); };
        hover.PointerExited += (_, _) => box.BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest");
        box.GestureRecognizers.Add(hover);
        box.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (!tp.IsEnabled) return;
                var pg = page ?? HostPage(box); if (pg is null) return;
                var list = new VerticalStackLayout { Spacing = 0 };
                for (var m = fromHour * 60; m <= toHour * 60 + 30; m += 30)
                {
                    var t = TimeSpan.FromMinutes(m);
                    var sel = tp.Time == t;
                    var lbl = new Label { Text = Fmt(t), FontSize = 14, FontFamily = sel ? ClinicTheme.FontSemiBold : ClinicTheme.Font, TextColor = sel ? Colors.White : ClinicTheme.Color("OnSurface") };
                    var row = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 }, Padding = new Thickness(12, 7), BackgroundColor = sel ? ClinicTheme.Color("Primary") : Colors.Transparent, Content = lbl };
                    if (!sel)
                    {
                        var h = new PointerGestureRecognizer();
                        h.PointerEntered += (_, _) => row.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
                        h.PointerExited += (_, _) => row.BackgroundColor = Colors.Transparent;
                        row.GestureRecognizers.Add(h);
                    }
                    row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { ClinicPopup.Hide(pg); tp.Time = t; }) });
                    list.Children.Add(row);
                }
                var panel = new Border
                {
                    BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"), Stroke = ClinicTheme.Color("Outline").WithAlpha(0.5f), StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, Padding = new Thickness(6),
                    Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.28f, Radius = 22, Offset = new Point(0, 8) },
                    Content = new ScrollView { MaximumHeightRequest = 280, Content = list },
                };
                ClinicPopup.Show(pg, box, panel, 150, 300);
            })
        });
        return new VerticalStackLayout { Children = { box, tp } };
    }

    /// <summary>A dropdown field replacing a native Picker: bordered box showing the current choice with ▾; clicking floats the list. The Picker stays as the hidden value holder.</summary>
    public static View PickerBox(ContentPage? page, Picker picker, double width = 0)
    {
        picker.IsVisible = false;
        var text = new Label { FontSize = 14, FontFamily = ClinicTheme.FontMedium, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        var arrow = new Label { Text = "▾", FontSize = 14, TextColor = ClinicTheme.Color("Primary"), VerticalOptions = LayoutOptions.Center };
        var grid = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(text, 0, 0); grid.Add(arrow, 1, 0);
        var box = new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
            Stroke = ClinicTheme.Color("Primary").WithAlpha(0.55f), StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12, 7), HorizontalOptions = width > 0 ? LayoutOptions.Start : LayoutOptions.Fill, Content = grid,
        };
        if (width > 0) box.WidthRequest = width;
        List<string> Items() => (picker.ItemsSource ?? new List<string>()).Cast<object>().Select(o => o?.ToString() ?? "").ToList();
        void Refresh()
        {
            var items = Items();
            var i = picker.SelectedIndex;
            var has = i >= 0 && i < items.Count;
            text.Text = has ? items[i] : (string.IsNullOrWhiteSpace(picker.Title) ? "Select…" : picker.Title);
            text.TextColor = has ? ClinicTheme.Color("OnSurface") : ClinicTheme.Color("OnSurfaceVariant");
            box.Opacity = picker.IsEnabled ? 1 : 0.45;
        }
        Refresh();
        picker.SelectedIndexChanged += (_, _) => Refresh();
        picker.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(Picker.ItemsSource) or nameof(Picker.SelectedItem) or nameof(Picker.IsEnabled)) Refresh(); };
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (_, _) => { if (picker.IsEnabled) box.BackgroundColor = ClinicTheme.Color("PrimarySubtle"); };
        hover.PointerExited += (_, _) => box.BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest");
        box.GestureRecognizers.Add(hover);
        box.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (!picker.IsEnabled) return;
                var pg = page ?? HostPage(box); if (pg is null) return;
                var items = Items();
                var list = new VerticalStackLayout { Spacing = 0 };
                for (var k = 0; k < items.Count; k++)
                {
                    var idx = k;
                    var sel = picker.SelectedIndex == idx;
                    var lbl = new Label { Text = items[idx], FontSize = 14, FontFamily = sel ? ClinicTheme.FontSemiBold : ClinicTheme.Font, TextColor = sel ? Colors.White : ClinicTheme.Color("OnSurface") };
                    var row = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 }, Padding = new Thickness(12, 8), BackgroundColor = sel ? ClinicTheme.Color("Primary") : Colors.Transparent, Content = lbl };
                    if (!sel)
                    {
                        var h = new PointerGestureRecognizer();
                        h.PointerEntered += (_, _) => row.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
                        h.PointerExited += (_, _) => row.BackgroundColor = Colors.Transparent;
                        row.GestureRecognizers.Add(h);
                    }
                    row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { ClinicPopup.Hide(pg); picker.SelectedIndex = idx; }) });
                    list.Children.Add(row);
                }
                var panel = new Border
                {
                    BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"), Stroke = ClinicTheme.Color("Outline").WithAlpha(0.5f), StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, Padding = new Thickness(6),
                    Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.28f, Radius = 22, Offset = new Point(0, 8) },
                    Content = new ScrollView { MaximumHeightRequest = 300, Content = list },
                };
                var w = Math.Max(box.Width, 180);
                ClinicPopup.Show(pg, box, panel, w, Math.Min(300, items.Count * 36 + 16));
            })
        });
        return new VerticalStackLayout { Children = { box, picker } };
    }
}
//JJQR - END
