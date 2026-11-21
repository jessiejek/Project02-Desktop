//JJQR - AI (Nov 21, 2026) - shared bits for the extra doctor screens (reuses the plain Staff building blocks read-only).
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra;

public static class DoctorExtraUi
{
    public static Guid? DoctorId(IServiceProvider sp) => StaffUi.Get<IDoctorProfileService>(sp).CurrentDoctorId;

    public static View NotLinked() => StaffUi.Card(
        ClinicTheme.Text("Doctor profile not linked", "HeadlineSm"),
        ClinicTheme.Text("Sign in with a doctor account to use this screen.", "MutedBody"));

    public static Task OpenConsultAsync(Guid bookingId) => Shell.Current.GoToAsync($"consult?bookingId={bookingId}");

    //JJQR - AI (21 Nov 2026) - web /doctor/consultation2/{id}?mode=complete|view|amend: the mode rides along on the existing "consult" route
    // (the consultation page owns what it does with it; an unknown query key is ignored by Shell).
    public static Task OpenConsultAsync(Guid bookingId, string mode) => Shell.Current.GoToAsync($"consult?bookingId={bookingId}&mode={Uri.EscapeDataString(mode)}");
    //JJQR - END

    public static string Peso(decimal v) => "₱" + v.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture);

    public static string Pht(DateTimeOffset t) => t.ToOffset(TimeSpan.FromHours(8)).ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    //JJQR - AI (Nov 21, 2026) - underline tabs: the selected tab has a teal label and a teal bar; the strip sits on one hairline
    public static VerticalStackLayout Tabs((string Id, string Label)[] tabs, Func<string> selected, Action<string> select)
    {
        var strip = new HorizontalStackLayout { Spacing = 2 };
        void Render()
        {
            strip.Children.Clear();
            foreach (var (id, label) in tabs)
            {
                var on = id == selected();
                var text = new Label
                {
                    Text = label, FontSize = 14, Padding = new Thickness(14, 10, 14, 9),
                    FontFamily = on ? ClinicTheme.FontSemiBold : ClinicTheme.FontMedium,
                    TextColor = on ? ClinicTheme.Color("Primary") : ClinicTheme.Color("OnSurfaceVariant"),
                };
                var bar = new BoxView { HeightRequest = 3, Color = on ? ClinicTheme.Color("Primary") : Colors.Transparent, CornerRadius = 2 };
                var tab = new VerticalStackLayout { Spacing = 0, Children = { text, bar } };
                tab.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { select(id); Render(); }) });
                if (!on)
                {
                    var hover = new PointerGestureRecognizer();
                    hover.PointerEntered += (_, _) => { text.TextColor = ClinicTheme.Color("OnSurface"); bar.Color = ClinicTheme.Color("OutlineVariant"); };
                    hover.PointerExited += (_, _) => { text.TextColor = ClinicTheme.Color("OnSurfaceVariant"); bar.Color = Colors.Transparent; };
                    tab.GestureRecognizers.Add(hover);
                }
                strip.Children.Add(tab);
            }
        }
        Render();
        var line = new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant"), Margin = new Thickness(0, -1, 0, 0) };
        return new VerticalStackLayout { Spacing = 0, Children = { strip, line } };
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - segmented control: one grey pill holding the options; the selected option is a white raised segment
    public static View Segmented(string[] labels, int selected, Action<int> onSelect)
    {
        var row = new HorizontalStackLayout { Spacing = 2 };
        for (var i = 0; i < labels.Length; i++)
        {
            var idx = i;
            var on = i == selected;
            var seg = new Border
            {
                StrokeThickness = on ? 1 : 0, Stroke = ClinicTheme.Color("OutlineVariant"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                Padding = new Thickness(14, 6),
                BackgroundColor = on ? ClinicTheme.Color("SurfaceContainerLowest") : Colors.Transparent,
                Content = new Label { Text = labels[i], FontSize = 13, FontFamily = on ? ClinicTheme.FontSemiBold : ClinicTheme.FontMedium, TextColor = on ? ClinicTheme.Color("Primary") : ClinicTheme.Color("OnSurfaceVariant") },
            };
            seg.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => onSelect(idx)) });
            row.Children.Add(seg);
        }
        return new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainer"), StrokeThickness = 0, Padding = new Thickness(3),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 9 },
            VerticalOptions = LayoutOptions.Center, Content = row,
        };
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - editing helpers for the overflow screens
    /// <summary>Modal confirm (web Modal with Cancel + action). Returns true when confirmed.</summary>
    public static Task<bool> ConfirmAsync(Page from, string title, string message, string confirmText, bool danger = false, string backText = "Cancel") =>
        StaffDialog.ShowAsync(from, title, message, null, null, backText, confirmText, _ => Task.FromResult<string?>(null), danger);

    /// <summary>Toggle chip (web FilterChip / pill buttons).</summary>
    public static Button Chip(string text, bool on, Func<Task> onClick)
    {
        var b = StaffUi.Btn(text, onClick, on ? "SegmentedButtonSelected" : "SegmentedButton");
        b.Margin = new Thickness(0, 0, 8, 8);
        return b;
    }

    public static Button Chip(string text, bool on, Action onClick) => Chip(text, on, () => { onClick(); return Task.CompletedTask; });

    /// <summary>Toast line: success style for good news, error style otherwise; empty text hides it.</summary>
    public static void Toast(Label success, Label error, string? text, bool ok)
    {
        StaffUi.Show(ok ? success : error, text);
        StaffUi.Show(ok ? error : success, null);
    }

    public static View Spaced(View v, double right = 8, double bottom = 8) { v.Margin = new Thickness(0, 0, right, bottom); return v; }

    /// <summary>A multi-line editor sized like the web textarea.</summary>
    public static Editor Area(string? text, double height = 96, string? placeholder = null) =>
        new() { Text = text ?? "", HeightRequest = height, AutoSize = EditorAutoSizeOption.Disabled, Placeholder = placeholder };
    //JJQR - END
}
//JJQR - END
