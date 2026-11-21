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

    /// <summary>Plain tab strip: one button per tab, the selected one uses the primary style.</summary>
    public static FlexLayout Tabs((string Id, string Label)[] tabs, Func<string> selected, Action<string> select)
    {
        var row = StaffUi.Wrap();
        void Render()
        {
            row.Children.Clear();
            foreach (var (id, label) in tabs)
            {
                var b = StaffUi.Btn(label, () => { select(id); Render(); return Task.CompletedTask; }, id == selected() ? "SegmentedButtonSelected" : "SegmentedButton");
                b.Margin = new Thickness(0, 0, 8, 8);
                row.Children.Add(b);
            }
        }
        Render();
        return row;
    }

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
