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
                    ClinicTheme.Text(busy ? "Saving…" : has ? "Your own background" : "Plain default layout", "MutedSm"),
                    new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Children = { upload, clear } },
                },
            };
            var line = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            line.Add(thumb, 0, 0);
            line.Add(text, 1, 0);
            layouts.Children.Add(line);
        }
        body.Children.Add(Section(layouts));
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
