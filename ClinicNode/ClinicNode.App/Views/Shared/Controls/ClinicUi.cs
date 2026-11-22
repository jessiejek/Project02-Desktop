//JJQR - AI (21 Nov 2026) - item 3: shared controls for every lane, 1:1 with Project02-fe components/ui (Button, fieldStyles inputs, DataTable,
// StatusPill, Modal, Toast/ToastHost, FormAlert, PageHeader). Styles live in Resources/Styles/Styles.xaml; colours in Colors.xaml (web tokens). Light only.
// Usage: ClinicUi.Button("Save", onClick) · ClinicUi.Field("Email", entry, error: "…") · ClinicUi.Table(columns, rows) · ClinicUi.Badge("Waiting")
//        await ClinicUi.ConfirmAsync(page, "Cancel visit?", "…", "Cancel visit", danger: true) · ClinicToast.Show("Saved", ToastKind.Success)
using ClinicNode.App.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace ClinicNode.App.Views.Shared.Controls;

public enum ButtonKind { Primary, Secondary, Ghost, Danger }
public enum ToastKind { Info, Success, Warning, Error }

public sealed record TableColumn(string Header, GridLength Width, Func<object, View> Cell);

public static class ClinicUi
{
    // ── buttons (buttonStyles.ts: primary / secondary / ghost / danger, 44 px) ──
    public static Button Button(string text, Func<Task> onClick, ButtonKind kind = ButtonKind.Primary, bool fill = false)
    {
        var b = new Button
        {
            Text = text,
            Style = ClinicTheme.Style(kind switch
            {
                ButtonKind.Secondary => "SecondaryButton",
                ButtonKind.Ghost => "GhostButton",
                ButtonKind.Danger => "DangerButton",
                _ => "PrimaryButton",
            }),
            HorizontalOptions = fill ? LayoutOptions.Fill : LayoutOptions.Start,
        };
        b.Clicked += async (_, _) =>
        {
            //JJQR - AI (Nov 22, 2026) - no double submit AND a visible "Text..." while it works
            await ClinicNode.App.Controls.BusyButton.RunBusyAsync(b, async () =>
            {
                try { await onClick(); }
                catch (Exception ex) { ClinicToast.Show(ex.Message, ToastKind.Error); }
            });
            //JJQR - END
        };
        return b;
    }

    // ── inputs (fieldStyles.ts: label text-label-md, 44 px input, hint / error below) ──
    public static View Field(string label, View input, string? hint = null, string? error = null, bool required = false)
    {
        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Children.Add(ClinicTheme.Text(required ? label + " *" : label, "FieldLabel"));
        stack.Children.Add(input);
        if (!string.IsNullOrWhiteSpace(error)) stack.Children.Add(ClinicTheme.Text(error!, "ErrorText"));
        else if (!string.IsNullOrWhiteSpace(hint)) stack.Children.Add(ClinicTheme.Text(hint!, "FieldHint"));
        return stack;
    }

    public static Entry Input(string? placeholder = null, Keyboard? keyboard = null) =>
        new() { Placeholder = placeholder ?? "", Keyboard = keyboard ?? Keyboard.Default };

    // ── badges (StatusPill.tsx) ──
    public static View Badge(string status) => ClinicTheme.MakeStatusPill(status);

    public static View Badge(string text, string tone) => new Border
    {
        Style = ClinicTheme.Style("Pill" + tone) ?? ClinicTheme.Style("PillNeutral"),
        Content = ClinicTheme.Text(text, "PillLabel" + tone),
    };

    // ── form alert (FormAlert.tsx) ──
    public static View Alert(string text, bool success) => new Border
    {
        Style = ClinicTheme.Style(success ? "FormAlertSuccess" : "FormAlertError"),
        Content = ClinicTheme.Text(text, success ? "FormAlertSuccessText" : "FormAlertErrorText"),
    };

    // ── page header (PageHeader.tsx) ──
    public static View PageHeader(string title, string? subtitle = null, params View[] actions)
    {
        var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
        var left = new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text(title, "HeadlineLg") } }; //JJQR - AI (Nov 21, 2026) page title size //JJQR - END
        if (!string.IsNullOrWhiteSpace(subtitle)) left.Children.Add(ClinicTheme.Text(subtitle!, "MutedBody"));
        grid.Add(left, 0, 0);
        if (actions.Length > 0)
        {
            var right = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
            foreach (var a in actions) right.Children.Add(a);
            grid.Add(right, 1, 0);
        }
        return grid;
    }

    // ── table (DataTable.tsx: rounded-xl frame, header row bg-surface-container-low, row dividers, empty text) ──
    public static View Table(IReadOnlyList<TableColumn> columns, IEnumerable<object> rows, string empty = "Nothing to show yet.", Action<object>? onTap = null)
    {
        //JJQR - AI (Nov 21, 2026) - ONE grid for header + every row so each column has the same width on every row (Auto columns used to
        // resize per row and push the headers out of line); the row background spans all columns and lights up on hover.
        var grid = new Grid { ColumnSpacing = 0, RowSpacing = 0 };
        for (var i = 0; i < columns.Count; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(columns[i].Width));
        var lastCol = Math.Max(0, columns.Count - 1);
        var surface = ClinicTheme.Color("SurfaceContainerLowest");
        var gridRow = 0;

        void AddRow(Func<TableColumn, View> make, Color background, double minHeight, double vPad, object? item, bool interactive)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var bg = new BoxView { Color = background, MinimumHeightRequest = minHeight };
            grid.Add(bg, 0, gridRow); Grid.SetColumnSpan(bg, columns.Count);
            var touched = new List<View> { bg };
            for (var c = 0; c < columns.Count; c++)
            {
                var cell = make(columns[c]);
                cell.VerticalOptions = LayoutOptions.Center;
                if (string.IsNullOrEmpty(columns[c].Header) && c == lastCol) cell.HorizontalOptions = LayoutOptions.End;
                var holder = new ContentView { Padding = new Thickness(c == 0 ? 16 : 8, vPad, c == lastCol ? 16 : 8, vPad), VerticalOptions = LayoutOptions.Fill, Content = cell, MinimumHeightRequest = minHeight };
                grid.Add(holder, c, gridRow);
                touched.Add(holder);
            }
            if (interactive)
            {
                foreach (var t in touched)
                {
                    var hv = new PointerGestureRecognizer();
                    hv.PointerEntered += (_, _) => bg.Color = ClinicTheme.Color("PrimarySubtle");
                    hv.PointerExited += (_, _) => bg.Color = surface;
                    t.GestureRecognizers.Add(hv);
                    if (onTap is not null && item is not null)
                    {
                        var captured = item;
                        t.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => onTap(captured)) });
                    }
                }
            }
            gridRow++;
        }

        AddRow(c => ClinicTheme.Text(c.Header, "TableHeader"), ClinicTheme.Color("SurfaceContainerLow"), 36, 9, null, false);
        var any = false;
        foreach (var item in rows)
        {
            any = true;
            grid.RowDefinitions.Add(new RowDefinition(1));
            var sep = new BoxView { Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.3f), HeightRequest = 1 };
            grid.Add(sep, 0, gridRow); Grid.SetColumnSpan(sep, columns.Count);
            gridRow++;
            AddRow(c => c.Cell(item), surface, 42, 10, item, true);
        }
        var body = new VerticalStackLayout { Spacing = 0, Children = { grid } };
        if (!any) body.Children.Add(ClinicTheme.Text(empty, "TableEmpty"));
        return new Border { Style = ClinicTheme.Style("TableFrame"), Content = body };
        //JJQR - END
    }

    public static View Cell(string? text) => ClinicTheme.Text(string.IsNullOrWhiteSpace(text) ? "—" : text!, "TableCell");

    private static Grid Row(IReadOnlyList<TableColumn> columns, Func<TableColumn, View> make, string style)
    {
        var g = new Grid { Style = ClinicTheme.Style(style), ColumnSpacing = 16 };
        for (var i = 0; i < columns.Count; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(columns[i].Width));
            g.Add(make(columns[i]), i, 0);
        }
        return g;
    }

    // ── dialogs (Modal.tsx) ──
    public static Task<bool> ConfirmAsync(Page page, string title, string message, string confirm = "OK", string cancel = "Cancel", bool danger = false) =>
        ClinicDialog.ShowAsync(page, title, ClinicTheme.Text(message, "BodyMd"), confirm, cancel, danger);
}

/// <summary>Modal card over a scrim, header / body / footer like the web Modal. Returns true on confirm.</summary>
public sealed class ClinicDialog : ContentPage
{
    private readonly TaskCompletionSource<bool> _result = new();

    private ClinicDialog(string title, View body, string confirm, string? cancel, bool danger)
    {
        ControlTemplate = null; //JJQR - AI (Nov 21, 2026) - dialogs keep the full-window backdrop, no centred column //JJQR - END
        BackgroundColor = Colors.Transparent;
        Shell.SetPresentationMode(this, PresentationMode.ModalNotAnimated);
        //JJQR - AI (Nov 21, 2026) - shared modal chrome (backdrop, floating card, title bar + close, grey footer)
        var ok = new Button { Text = confirm, Style = ClinicTheme.Style(danger ? "DangerButton" : "PrimaryButton") };
        ok.Clicked += async (_, _) => await ok.RunBusyAsync(() => DoneAsync(true));
        var footer = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End };
        if (cancel is not null)
        {
            var no = new Button { Text = cancel, Style = ClinicTheme.Style("SecondaryButton") };
            no.Clicked += async (_, _) => await DoneAsync(false);
            footer.Children.Add(no);
        }
        footer.Children.Add(ok);
        Content = ClinicModal.Build(title, body, footer, () => DoneAsync(false));
        //JJQR - END
    }

    private static void Divider(out BoxView b) => b = new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") };

    protected override bool OnBackButtonPressed() { _ = DoneAsync(false); return true; }

    private async Task DoneAsync(bool value)
    {
        if (_result.Task.IsCompleted) return;
        _result.TrySetResult(value);
        try { await Navigation.PopModalAsync(false); } catch { }
    }

    public static async Task<bool> ShowAsync(Page page, string title, View body, string confirm = "OK", string? cancel = "Cancel", bool danger = false)
    {
        var dialog = new ClinicDialog(title, body, confirm, cancel, danger);
        await page.Navigation.PushModalAsync(dialog, false);
        return await dialog._result.Task;
    }
}

/// <summary>Toast host (ToastHost.tsx): bottom-centre, auto-dismiss after 4 s, newest on top, max 3. Safe from any thread.</summary>
public static class ClinicToast
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(4);
    private static readonly List<Border> Live = [];

    public static void Show(string message, ToastKind kind = ToastKind.Info) =>
        MainThread.BeginInvokeOnMainThread(() => _ = ShowOnUiAsync(message, kind));

    private static async Task ShowOnUiAsync(string message, ToastKind kind)
    {
        var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is not ContentPage cp || cp.Content is null) return;
        var host = EnsureHost(cp);
        if (host is null) return;
        var (style, label) = kind switch
        {
            ToastKind.Success => ("ToastSuccess", "FormAlertSuccessText"),
            ToastKind.Warning => ("ToastWarning", "PillLabelWarning"),
            ToastKind.Error => ("ToastError", "FormAlertErrorText"),
            _ => ("ToastInfo", "PillLabelInfo"),
        };
        var text = ClinicTheme.Text(message, label);
        text.TextTransform = TextTransform.None;
        text.MaxLines = 2;
        var toast = new Border { Style = ClinicTheme.Style(style), Content = text, Opacity = 0 };
        SemanticProperties.SetDescription(toast, message);
        toast.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Remove(host, toast)) });
        host.Children.Insert(0, toast);
        Live.Add(toast);
        while (host.Children.Count > 3) Remove(host, (Border)host.Children[^1]);
        await toast.FadeToAsync(1, 150);
        await Task.Delay(Duration);
        Remove(host, toast);
    }

    private static void Remove(VerticalStackLayout host, Border toast)
    {
        Live.Remove(toast);
        host.Children.Remove(toast);
    }

    /// <summary>Wraps the page content once in a Grid with a bottom-centre toast stack (no page needs to opt in).</summary>
    private static VerticalStackLayout? EnsureHost(ContentPage page)
    {
        if (page.Content is Grid g && g.ClassId == "clinic-toast-root")
            return g.Children.OfType<VerticalStackLayout>().FirstOrDefault(v => v.ClassId == "clinic-toast-host");
        var original = page.Content;
        var host = new VerticalStackLayout { ClassId = "clinic-toast-host", Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.End, Margin = new Thickness(0, 0, 0, 24), InputTransparent = false, CascadeInputTransparent = false };
        var root = new Grid { ClassId = "clinic-toast-root" };
        page.Content = null;
        root.Children.Add(original);
        root.Children.Add(host);
        page.Content = root;
        return host;
    }
}
//JJQR - END
