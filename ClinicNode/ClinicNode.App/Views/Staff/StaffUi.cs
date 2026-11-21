//JJQR - AI (Nov 21, 2026) - plain shared building blocks for the staff screens (functionality first; shared style keys only, no hard-coded colours).
using ClinicNode.App.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Views.Staff;

public static class StaffUi
{
    /// <summary>Resolve a service; Core/Staff services fall back to ActivatorUtilities until AddClinicNodeStaff() is wired in MauiProgram.</summary>
    public static T Get<T>(IServiceProvider sp) where T : notnull => ActivatorUtilities.GetServiceOrCreateInstance<T>(sp);

    public static IServiceProvider Services =>
        IPlatformApplication.Current?.Services ?? throw new InvalidOperationException("App services are not ready.");

    /// <summary>Push a staff page built from DI (no Shell route needed). Deep-link routes are not needed (pages are pushed from DI). //JJQR - AI (21 Nov 2026) allen0 I-1 //JJQR - END</summary>
    public static Task PushAsync<TPage>(Page from, params object[] args) where TPage : Page =>
        from.Navigation.PushAsync((Page)ActivatorUtilities.CreateInstance(Services, typeof(TPage), args));

    public static VerticalStackLayout Column(int spacing = 16) => new() { Spacing = spacing };

    public static ScrollView Scroll(View content) => new() { Content = new ContentView { Padding = 24, Content = content } };

    public static View Header(string title, string description, params View[] actions)
    {
        var col = new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text(title, "HeadlineMd"), ClinicTheme.Text(description, "MutedBody") } };
        if (actions.Length == 0) return col;
        var row = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var a in actions) { a.Margin = new Thickness(0, 8, 8, 0); row.Children.Add(a); }
        col.Children.Add(row);
        return col;
    }

    public static Button Btn(string text, Func<Task> onClick, string style = "PrimaryButton")
    {
        var b = new Button { Text = text, Style = ClinicTheme.Style(style) };
        b.Clicked += async (_, _) =>
        {
            if (!b.IsEnabled) return;
            b.IsEnabled = false;
            try { await onClick(); }
            finally { b.IsEnabled = true; }
        };
        return b;
    }

    //JJQR - AI (21 Nov 2026) - web <Button variant="ghost" className="text-error">: ghost button with error-coloured text (Cancel / No-show).
    public static Button DangerGhost(string text, Func<Task> onClick)
    {
        var b = Btn(text, onClick, "GhostButton");
        b.TextColor = ClinicTheme.Color("Error");
        return b;
    }

    /// <summary>Web <c>isVoidBooking</c>: a cancelled / no-show / expired visit owes nothing, so its payment shows "—", never "Unpaid".</summary>
    public static bool IsVoidBooking(ClinicApp.Domain.Enums.BookingStatus s) =>
        s is ClinicApp.Domain.Enums.BookingStatus.Cancelled or ClinicApp.Domain.Enums.BookingStatus.NoShow or ClinicApp.Domain.Enums.BookingStatus.Expired;
    //JJQR - END

    public static View Stat(string label, string value, Func<Task>? onTap = null)
    {
        var card = new Border
        {
            Style = ClinicTheme.Style("Card"),
            MinimumWidthRequest = 150,
            Margin = new Thickness(0, 0, 12, 12),
            Content = new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text(value, "HeadlineLg"), ClinicTheme.Text(label, "MutedSm") } },
        };
        if (onTap is not null)
        {
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await onTap();
            card.GestureRecognizers.Add(tap);
        }
        return card;
    }

    public static FlexLayout Wrap(params View[] children)
    {
        var f = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var c in children) f.Children.Add(c);
        return f;
    }

    public static Label Error() => new Label { Style = ClinicTheme.Style("ErrorText"), IsVisible = false };
    public static Label Success() => new Label { Style = ClinicTheme.Style("PillLabelSuccess"), IsVisible = false };

    public static void Show(Label label, string? text)
    {
        label.Text = text ?? "";
        label.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    public static View Card(params View[] children)
    {
        var col = new VerticalStackLayout { Spacing = 12 };
        foreach (var c in children) col.Children.Add(c);
        return new Border { Style = ClinicTheme.Style("Card"), Content = col };
    }

    public static View Row(params View[] children)
    {
        var col = new VerticalStackLayout { Spacing = 8 };
        foreach (var c in children) col.Children.Add(c);
        return new Border { Style = ClinicTheme.Style("ListCard"), Content = col };
    }

    public static View Field(string label, View input, string? hint = null)
    {
        var col = new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text(label, "FieldLabel"), input } };
        if (hint is not null) col.Children.Add(ClinicTheme.Text(hint, "MutedSm"));
        return col;
    }

    public static Label Link(string text, Func<Task> onTap)
    {
        var l = ClinicTheme.Text(text, "BrandLink");
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await onTap();
        l.GestureRecognizers.Add(tap);
        return l;
    }

    public static string Time(TimeOnly t) => t.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
    public static string Date(DateOnly d) => d.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture);
    public static string DateTimeText(DateTimeOffset t) =>
        t.ToOffset(TimeSpan.FromHours(8)).ToString("MMM d, yyyy, h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Modal with a title, message, optional text field, inline error, and Back / confirm buttons. The confirm callback returns an error to keep the dialog open, or null to close.</summary>
public sealed class StaffDialog : ContentPage
{
    private readonly TaskCompletionSource<bool> _done = new();

    StaffDialog(string title, string message, string? fieldLabel, string? placeholder, string? hint, string backText, string confirmText, bool danger, Func<string, Task<string?>> confirm, bool multiline,
        string? initialText = null, int? maxLength = null) //JJQR - AI (21 Nov 2026) - prefilled value + max length (typed-OR retry) //JJQR - END
    {
        Title = title;
        //JJQR - AI (Nov 21, 2026) - real modal look: backdrop + floating card (shared ClinicModal chrome) instead of a full page
        BackgroundColor = Colors.Transparent;
        ControlTemplate = null; //JJQR - AI (Nov 21, 2026) - full-window backdrop //JJQR - END
        Shell.SetPresentationMode(this, PresentationMode.ModalNotAnimated);
        var error = StaffUi.Error();
        InputView? input = null;
        var col = new VerticalStackLayout { Spacing = 12, Children = { error, ClinicTheme.Text(message, "BodyMd") } };
        if (fieldLabel is not null)
        {
            input = multiline ? new Editor { Placeholder = placeholder, HeightRequest = 96 } : new Entry { Placeholder = placeholder };
            if (initialText is not null) input.Text = initialText;
            if (maxLength is { } max) input.MaxLength = max;
            col.Children.Add(StaffUi.Field(fieldLabel, input, hint));
        }
        var back = StaffUi.Btn(backText, async () => { _done.TrySetResult(false); await Navigation.PopModalAsync(); }, "SecondaryButton");
        var ok = StaffUi.Btn(confirmText, async () =>
        {
            StaffUi.Show(error, null);
            var problem = await confirm(input?.Text ?? "");
            if (problem is not null) { StaffUi.Show(error, problem); return; }
            _done.TrySetResult(true);
            await Navigation.PopModalAsync();
        }, danger ? "DangerButton" : "PrimaryButton");
        var footer = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { back, ok } };
        Content = ClinicNode.App.Controls.ClinicModal.Build(title, col, footer, async () => { _done.TrySetResult(false); await Navigation.PopModalAsync(); });
        //JJQR - END
    }

    public static async Task<bool> ShowAsync(Page from, string title, string message, string? fieldLabel, string? placeholder, string backText, string confirmText,
        Func<string, Task<string?>> confirm, bool danger = false, string? hint = null, bool multiline = true,
        string? initialText = null, int? maxLength = null) //JJQR - AI (21 Nov 2026) //JJQR - END
    {
        var d = new StaffDialog(title, message, fieldLabel, placeholder, hint, backText, confirmText, danger, confirm, multiline, initialText, maxLength); //JJQR - AI (21 Nov 2026) - + initialText, maxLength //JJQR - END
        await from.Navigation.PushModalAsync(d);
        return await d._done.Task;
    }

    protected override bool OnBackButtonPressed()
    {
        _done.TrySetResult(false);
        return base.OnBackButtonPressed();
    }
}
//JJQR - END
