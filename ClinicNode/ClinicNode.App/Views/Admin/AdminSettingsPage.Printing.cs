//JJQR - AI (Nov 24, 2026) - owner wishlist: Admin > Settings > Printing. The Admin chooses how the WHOLE clinic prints (each computer decides / PDF / native / off)
// and uploads a PNG or JPG background for each of the five printed documents. Online only (the server keeps the settings; each computer keeps a copy for offline printing).
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Printing;

namespace ClinicNode.App.Views.Admin;

public partial class AdminSettingsPage
{
    PrintConfigView? _print;
    bool _printLoaded, _printLoading;
    string _printMessage = "";
    bool _printMessageOk;
    ClinicPrintKind? _printBusyKind;

    IPrintConfigAdmin? PrintAdmin => Handler?.MauiContext?.Services.GetService<IPrintConfigAdmin>();

    static readonly (ClinicPrintMode Mode, string Title, string Hint)[] PrintModes =
    [
        (ClinicPrintMode.Computer, "Each computer decides", "Nothing changes: every computer keeps using its own printing settings (the way it works today)."),
        (ClinicPrintMode.Pdf, "PDF printing", "Print saves a PDF and opens it. You print or save it from the PDF viewer."),
        (ClinicPrintMode.Native, "Native printing", "Print opens the computer's own print window, straight to the printer."),
        (ClinicPrintMode.Off, "Printing off", "Print is switched off on every computer. Nothing can be printed until you turn it on again."),
    ];

    async Task LoadPrintAsync()
    {
        if (_printLoading) return;
        _printLoading = true;
        try { _print = PrintAdmin is { } admin ? await admin.GetAsync() : null; }
        catch { _print = null; }
        _printLoaded = true;
        _printLoading = false;
        await MainThread.InvokeOnMainThreadAsync(Render);
    }

    void Printing(VerticalStackLayout body)
    {
        if (!_printLoaded)
        {
            body.Children.Add(new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Start });
            _ = LoadPrintAsync();
            return;
        }
        if (_print is null)
        {
            body.Children.Add(ClinicTheme.Text("Printing settings unavailable", "HeadlineSm"));
            body.Children.Add(ClinicTheme.Text("These settings are kept on the clinic server, so they need an internet connection. Nothing has been changed.", "MutedBody"));
            body.Children.Add(ClinicUi.Button("Try again", async () => { _printLoaded = false; Render(); await Task.CompletedTask; }, ButtonKind.Secondary));
            return;
        }

        if (!string.IsNullOrWhiteSpace(_printMessage)) body.Children.Add(ClinicUi.Alert(_printMessage, _printMessageOk));

        // 1. how the clinic prints
        var modes = new VerticalStackLayout { Spacing = 10 };
        modes.Children.Add(ClinicTheme.Text("How printing works", "HeadlineSm"));
        modes.Children.Add(ClinicTheme.Text("One choice for every computer in the clinic.", "MutedBody"));
        foreach (var (mode, title, hint) in PrintModes)
        {
            var selected = _print.Mode == mode;
            var m = mode;
            var pick = ClinicUi.Button((selected ? "●  " : "○  ") + title, async () => await SetPrintModeAsync(m), selected ? ButtonKind.Primary : ButtonKind.Secondary);
            var row = new VerticalStackLayout { Spacing = 2, Children = { pick, ClinicTheme.Text(hint, "MutedSm") } };
            modes.Children.Add(row);
        }
        body.Children.Add(Section(modes));

        // 2. the background picture of each document
        var layouts = new VerticalStackLayout { Spacing = 14 };
        layouts.Children.Add(ClinicTheme.Text("Print layout (background)", "HeadlineSm"));
        layouts.Children.Add(ClinicTheme.Text("Upload a picture for each document (PNG or JPG, up to 6 MB, the size of the paper). The writing stays where it is and prints on top of your picture, so leave the writing areas clear. “Back to default” returns to the plain layout.", "MutedBody"));
        var admin = PrintAdmin;
        foreach (var (kind, _, label) in PrintPolicyKinds.All)
        {
            var k = kind;
            var has = _print.BackgroundVersions.ContainsKey(kind);
            var busy = _printBusyKind == kind;
            var thumb = new Border
            {
                WidthRequest = 72, HeightRequest = 96, StrokeThickness = 1, Stroke = ClinicTheme.Color("OutlineVariant"),
                BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            };
            if (has && admin?.BackgroundBytes(kind) is { Length: > 0 } bytes)
                thumb.Content = new Image { Aspect = Aspect.AspectFit, Source = ImageSource.FromStream(() => new MemoryStream(bytes)) };
            else
                thumb.Content = new Label { Text = has ? "…" : "Default", FontSize = 11, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, TextColor = ClinicTheme.Color("OnSurfaceVariant") };

            var on = _print.IsEnabled(kind);
            var toggle = ClinicUi.Button(on ? "Printing: On" : "Printing: Off", async () => await SetPrintEnabledAsync(k, !on), on ? ButtonKind.Primary : ButtonKind.Danger);
            toggle.IsEnabled = !busy;
            var upload = ClinicUi.Button(has ? "Replace picture" : "Upload picture", async () => await UploadPrintBackgroundAsync(k), ButtonKind.Secondary);
            var clear = ClinicUi.Button("Back to default", async () => await ClearPrintBackgroundAsync(k), ButtonKind.Ghost);
            upload.IsEnabled = !busy;
            clear.IsEnabled = has && !busy;
            var text = new VerticalStackLayout
            {
                Spacing = 4, VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    ClinicTheme.Text(label, "LabelLg"),
                    ClinicTheme.Text(busy ? "Saving…" : (on ? "" : "Printing is off for this document. ") + (has ? "Your own background" : "Plain default layout"), "MutedSm"),
                    new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { toggle, upload, clear } },
                },
            };
            var line = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            line.Add(thumb, 0, 0);
            line.Add(text, 1, 0);
            layouts.Children.Add(line);
        }
        body.Children.Add(Section(layouts));

        // 3. the tests listed on the lab request (default = what is printed on the paper pad)
        var isCustom = _print.LabTests is { Count: > 0 };
        IReadOnlyList<string> labList = isCustom ? _print.LabTests! : ClinicNode.Core.Printing.ClinicFormsHtml.LabDefaultItems;
        var lab = new VerticalStackLayout { Spacing = 8 };
        lab.Children.Add(ClinicTheme.Text("Lab request tests", "HeadlineSm"));
        lab.Children.Add(ClinicTheme.Text($"The tests printed on the lab request (up to {ClinicNode.Core.Printing.ClinicFormsHtml.LabMaxItems}). The default is the list on your paper pad. Remove a test or add your own; “Back to default” returns to the paper's list. A test the doctor orders that is not on the list prints under “Others”.", "MutedBody"));
        lab.Children.Add(ClinicTheme.Text(isCustom ? "Your own list" : "Default list (as on the paper)", "LabelMd"));
        foreach (var name in labList)
        {
            var n = name;
            var remove = ClinicUi.Button("Remove", async () => await RemoveLabTestAsync(labList, n), ButtonKind.Ghost);
            var row = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            row.Add(new Label { Text = name, VerticalOptions = LayoutOptions.Center, FontSize = 14, TextColor = ClinicTheme.Color("OnSurface") }, 0, 0);
            row.Add(remove, 1, 0);
            lab.Children.Add(row);
        }
        var entry = new Entry { Placeholder = "New test, e.g. Vitamin D", MaxLength = ClinicNode.Core.Printing.ClinicFormsHtml.LabMaxItemName };
        var add = ClinicUi.Button("Add test", async () => await AddLabTestAsync(labList, entry.Text), ButtonKind.Secondary);
        var addRow = new Grid { ColumnSpacing = 10, Margin = new Thickness(0, 8, 0, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        addRow.Add(entry, 0, 0);
        addRow.Add(add, 1, 0);
        lab.Children.Add(addRow);
        var reset = ClinicUi.Button("Back to default (the paper's list)", async () => await SetLabTestsAsync(null), ButtonKind.Ghost);
        reset.IsEnabled = isCustom;
        lab.Children.Add(reset);
        body.Children.Add(Section(lab));
    }

    async Task SetLabTestsAsync(IReadOnlyList<string>? tests)
    {
        if (PrintAdmin is not { } admin) return;
        var err = await admin.SetLabTestsAsync(tests);
        _printMessageOk = err is null;
        _printMessage = err ?? (tests is null ? "Back to the list on the paper pad." : "Saved. The lab request now lists these tests.");
        _print = await admin.GetAsync() ?? _print;
        Render();
    }

    async Task RemoveLabTestAsync(IReadOnlyList<string> current, string name)
    {
        var next = current.Where(t => !string.Equals(t, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (next.Count == 0) { _printMessageOk = false; _printMessage = "Keep at least one test, or use “Back to default”."; Render(); return; }
        await SetLabTestsAsync(next);
    }

    async Task AddLabTestAsync(IReadOnlyList<string> current, string? typed)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(typed ?? "", @"\s+", " ").Trim();
        string? problem = null;
        if (t.Length == 0) problem = "Type the name of the test first.";
        else if (t.Length > ClinicNode.Core.Printing.ClinicFormsHtml.LabMaxItemName) problem = $"A test name can have at most {ClinicNode.Core.Printing.ClinicFormsHtml.LabMaxItemName} characters.";
        else if (current.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase))) problem = "That test is already on the list.";
        else if (current.Count >= ClinicNode.Core.Printing.ClinicFormsHtml.LabMaxItems) problem = $"The lab request can list at most {ClinicNode.Core.Printing.ClinicFormsHtml.LabMaxItems} tests.";
        if (problem is not null) { _printMessageOk = false; _printMessage = problem; Render(); return; }
        await SetLabTestsAsync([.. current, t]);
    }

    async Task SetPrintModeAsync(ClinicPrintMode mode)
    {
        if (PrintAdmin is not { } admin) return;
        var err = await admin.SetModeAsync(mode);
        _printMessageOk = err is null;
        _printMessage = err ?? "Saved. Every computer follows this within a few minutes.";
        _print = await admin.GetAsync() ?? _print;
        Render();
    }

    async Task UploadPrintBackgroundAsync(ClinicPrintKind kind)
    {
        if (PrintAdmin is not { } admin) return;
        FileResult? file;
        try { file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Choose a PNG or JPG picture", FileTypes = FilePickerFileType.Images }); }
        catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("AdminSettings.PrintPick", ex); return; }
        if (file is null) return;
        byte[] data;
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            data = ms.ToArray();
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("AdminSettings.PrintRead", ex);
            _printMessageOk = false; _printMessage = "That file could not be read."; Render(); return;
        }
        _printBusyKind = kind; Render();
        var err = await admin.UploadBackgroundAsync(kind, data);
        _printBusyKind = null;
        _printMessageOk = err is null;
        _printMessage = err ?? "Saved. Prints now use your picture.";
        _print = await admin.GetAsync() ?? _print;
        Render();
    }

    async Task SetPrintEnabledAsync(ClinicPrintKind kind, bool enabled)
    {
        if (PrintAdmin is not { } admin) return;
        _printBusyKind = kind; Render();
        var err = await admin.SetEnabledAsync(kind, enabled);
        _printBusyKind = null;
        _printMessageOk = err is null;
        _printMessage = err ?? (enabled ? "Printing is on for this document." : "Printing is off for this document on every computer.");
        _print = await admin.GetAsync() ?? _print;
        Render();
    }

    async Task ClearPrintBackgroundAsync(ClinicPrintKind kind)
    {
        if (PrintAdmin is not { } admin) return;
        _printBusyKind = kind; Render();
        var err = await admin.ClearBackgroundAsync(kind);
        _printBusyKind = null;
        _printMessageOk = err is null;
        _printMessage = err ?? "Back to the plain layout.";
        _print = await admin.GetAsync() ?? _print;
        Render();
    }
}
//JJQR - END
