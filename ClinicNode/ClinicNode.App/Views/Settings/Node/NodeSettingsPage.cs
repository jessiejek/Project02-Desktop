//JJQR - AI (Nov 21, 2026) - "This computer" settings page (code-only, like the other node pages). Logic lives in NodeSettingsViewModel.
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Settings.Node;
using ClinicNode.Core.NodeSettings;
using ClinicNode.Core.Printing;
using ClinicNode.App.Services.Backup;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Wire;

namespace ClinicNode.App.Views.Settings.Node;

public sealed class NodeSettingsPage : ContentPage, INodeSettingsDialogs
{
    readonly NodeSettingsViewModel _vm;

    public NodeSettingsPage(ISyncEndpoint endpoint, ISyncNodeRegistration registration, ISyncService sync, PrintingOptions printing,
        NodeOptions node, DatabaseEncryptionResult encryption, NodeBackupCoordinator backup)
    {
        _vm = new NodeSettingsViewModel(endpoint, registration, sync, printing, node, encryption, this, backup,
            appVersion: AppInfo.Current.VersionString, machineName: DeviceInfo.Current.Name);
        BindingContext = _vm;
        Title = "This computer";
        BackgroundColor = ClinicTheme.Color("Background");
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 20,
                MaximumWidthRequest = 760,
                Children = { Header(), SyncSection(), RegisterSection(), PrintingSection(), BackupSection() },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Detach();
    }

    // ── sections ──
    static View Card(string title, string? hint, params View[] body)
    {
        var stack = new VerticalStackLayout { Spacing = 10 };
        stack.Children.Add(ClinicTheme.Text(title, "HeadlineSm"));
        if (hint is not null) stack.Children.Add(ClinicTheme.Text(hint, "MutedBody"));
        foreach (var v in body) stack.Children.Add(v);
        return new Border
        {
            Padding = 16,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = ClinicTheme.RadiusLg },
            Stroke = ClinicTheme.Color("Outline"),
            BackgroundColor = Colors.White,
            Content = stack,
        };
    }

    static Label Bound(string path, string style, string? visiblePath = null)
    {
        var l = new Label().Apply(style);
        l.SetBinding(Label.TextProperty, path);
        if (visiblePath is not null) l.SetBinding(IsVisibleProperty, visiblePath);
        return l;
    }

    static Entry BoundEntry(string path, string placeholder, bool password = false, Keyboard? keyboard = null)
    {
        var e = new Entry { Placeholder = placeholder, IsPassword = password, MinimumHeightRequest = ClinicTheme.MinTouch };
        if (keyboard is not null) e.Keyboard = keyboard;
        e.SetBinding(Entry.TextProperty, new Binding(path, BindingMode.TwoWay));
        return e;
    }

    static Button Action(string text, string commandPath, string? enabledPath = null, bool secondary = true)
    {
        var b = new Button { Text = text, MinimumHeightRequest = ClinicTheme.MinTouch };
        b.Apply(secondary ? "SecondaryButton" : "PrimaryButton");
        b.SetBinding(Button.CommandProperty, commandPath);
        if (enabledPath is not null) b.SetBinding(IsEnabledProperty, enabledPath);
        return b;
    }

    static View Header() => new VerticalStackLayout
    {
        Spacing = 4,
        Children =
        {
            ClinicTheme.Text("This computer", "HeadlineMd"),
            ClinicTheme.Text("Sync, printers and backups for this clinic computer.", "MutedBody"),
        },
    };

    View SyncSection()
    {
        var url = BoundEntry(nameof(NodeSettingsViewModel.SyncUrl), "http://localhost:5055", keyboard: Keyboard.Url);
        var msg = Bound(nameof(NodeSettingsViewModel.UrlMessage), "MutedBody", nameof(NodeSettingsViewModel.HasUrlMessage));
        msg.SetBinding(Label.TextColorProperty, new Binding(nameof(NodeSettingsViewModel.UrlMessageIsError), converter: new BoolToColor(ClinicTheme.Color("Error"), ClinicTheme.Color("OnSurfaceVariant"))));
        var buttons = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                Action("Save", nameof(NodeSettingsViewModel.SaveUrlCommand), nameof(NodeSettingsViewModel.IsUrlIdle), secondary: false),
                Action("Test connection", nameof(NodeSettingsViewModel.TestUrlCommand), nameof(NodeSettingsViewModel.IsUrlIdle)),
                Action("Use default", nameof(NodeSettingsViewModel.ResetUrlCommand), nameof(NodeSettingsViewModel.IsUrlIdle)),
            },
        };
        var statusLine = Bound(nameof(NodeSettingsViewModel.SyncStatusText), "BodyMd");
        var result = Bound(nameof(NodeSettingsViewModel.SyncResult), "MutedBody");
        var syncNow = Action("Sync now", nameof(NodeSettingsViewModel.SyncNowCommand), nameof(NodeSettingsViewModel.IsSyncIdle), secondary: false);
        var spinner = new ActivityIndicator { HeightRequest = 20, WidthRequest = 20 };
        spinner.SetBinding(ActivityIndicator.IsRunningProperty, nameof(NodeSettingsViewModel.IsSyncBusy));
        return Card("Sync server", _vm.UrlHint,
            ConnectionRow(),
            ClinicTheme.Text("Server address", "FieldLabel"), url, buttons, msg,
            ClinicTheme.Text("Sync status", "FieldLabel"), statusLine,
            new HorizontalStackLayout { Spacing = 10, Children = { syncNow, spinner } }, result);
    }

    View ConnectionRow()
    {
        var pillText = new Label { Padding = new Thickness(2, 0) };
        pillText.Apply("BodyMd");
        pillText.SetBinding(Label.TextProperty, nameof(NodeSettingsViewModel.ConnectionStatusText));
        pillText.SetBinding(Label.TextColorProperty, new Binding(nameof(NodeSettingsViewModel.ConnectionState), converter: new ConnectionStateToForeground()));
        var pill = new Border
        {
            Padding = new Thickness(12, 4),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = pillText,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
        };
        pill.SetBinding(BackgroundColorProperty, new Binding(nameof(NodeSettingsViewModel.ConnectionState), converter: new ConnectionStateToBackground()));
        var checkedAt = Bound(nameof(NodeSettingsViewModel.LastCheckedText), "MutedBody");
        return new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                ClinicTheme.Text("API connection", "FieldLabel"),
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        pill,
                        Action("Recheck", nameof(NodeSettingsViewModel.RecheckCommand), nameof(NodeSettingsViewModel.IsConnectionIdle)),
                    },
                },
                checkedAt,
            },
        };
    }

    View RegisterSection()
    {
        var status = Bound(nameof(NodeSettingsViewModel.RegistrationStatus), "MutedBody");
        var error = Bound(nameof(NodeSettingsViewModel.RegisterError), "ErrorText", nameof(NodeSettingsViewModel.HasRegisterError));
        return Card("Register this node",
            "Paste a cloud admin access token to register this computer for sync. The node secret is stored in the system keychain.",
            status,
            ClinicTheme.Text("Admin access token", "FieldLabel"), BoundEntry(nameof(NodeSettingsViewModel.AdminToken), "Admin access token", password: true),
            ClinicTheme.Text("Computer name", "FieldLabel"), BoundEntry(nameof(NodeSettingsViewModel.NodeName), "Name for this computer"),
            error,
            Action("Register this node", nameof(NodeSettingsViewModel.RegisterCommand), nameof(NodeSettingsViewModel.IsRegisterIdle), secondary: false));
    }

    View PrintingSection()
    {
        var body = new List<View>
        {
            ClinicTheme.Text("Default printer for forms (prescription, lab, certificate, receipt)", "FieldLabel"),
            BoundEntry(nameof(NodeSettingsViewModel.FormsPrinterName), "Printer name (empty = choose in the print dialog)"),
            SwitchRow("Always show the print dialog", nameof(NodeSettingsViewModel.ShowPrintDialog)),
            ClinicTheme.Text("Queue slip", "FieldLabel"),
            PickerRow("Slip layout", NodeSettingsViewModel.SlipStyles, nameof(NodeSettingsViewModel.SlipStyle)),
            PickerRow("Connection", NodeSettingsViewModel.Transports, nameof(NodeSettingsViewModel.Transport)),
            BoundEntry(nameof(NodeSettingsViewModel.ThermalPrinterName), "Thermal printer name or ipp:// address"),
            BoundEntry(nameof(NodeSettingsViewModel.ThermalPrinterHost), "Network printer address (ESC/POS over TCP)"),
            BoundEntry(nameof(NodeSettingsViewModel.ThermalPrinterPort), "Port (9100)", keyboard: Keyboard.Numeric),
            SwitchRow("Cut paper after each slip", nameof(NodeSettingsViewModel.ThermalCut)),
            ClinicTheme.Text("Per document", "FieldLabel"),
        };
        var rows = new VerticalStackLayout { Spacing = 10, BindingContext = _vm };
        foreach (var row in _vm.Documents) rows.Children.Add(DocumentRow(row));
        body.Add(rows);
        body.Add(SwitchRow("Keep a PDF copy of every print (contains patient data)", nameof(NodeSettingsViewModel.KeepPdfCopies)));
        body.Add(Bound(nameof(NodeSettingsViewModel.PrintingMessage), "MutedBody"));
        body.Add(Action("Save printer settings", nameof(NodeSettingsViewModel.SavePrintingCommand), secondary: false));
        return Card("Printers and paper", "Choose a printer and the sheet to use for each document. “Auto” uses the form's own size.", body.ToArray());
    }

    static View DocumentRow(DocumentPrinterRow row)
    {
        var printer = new Entry { Placeholder = "Printer (empty = default)", Text = row.PrinterName, MinimumHeightRequest = ClinicTheme.MinTouch };
        printer.TextChanged += (_, e) => row.PrinterName = e.NewTextValue;
        var paper = new Picker { Title = "Paper", ItemsSource = NodeSettingsViewModel.Papers.ToList(), SelectedItem = row.Paper, MinimumWidthRequest = 120 };
        paper.SelectedIndexChanged += (_, _) => { if (paper.SelectedItem is PrinterPaper p) row.Paper = p; };
        var dialog = new Picker { Title = "Dialog", ItemsSource = NodeSettingsViewModel.DialogChoices, SelectedIndex = row.DialogChoice, MinimumWidthRequest = 180 };
        dialog.SelectedIndexChanged += (_, _) => { if (dialog.SelectedIndex >= 0) row.DialogChoice = dialog.SelectedIndex; };
        return new VerticalStackLayout
        {
            Spacing = 4,
            Children = { ClinicTheme.Text(row.Label, "BodyMd"), printer, new HorizontalStackLayout { Spacing = 8, Children = { ClinicPickers.PickerBox(null, paper, 150), ClinicPickers.PickerBox(null, dialog, 220) } } },
        };
    }

    static View SwitchRow(string text, string path)
    {
        var sw = new Switch();
        sw.SetBinding(Switch.IsToggledProperty, new Binding(path, BindingMode.TwoWay));
        return new HorizontalStackLayout { Spacing = 8, Children = { sw, new Label { Text = text, VerticalOptions = LayoutOptions.Center } } };
    }

    static View PickerRow<T>(string title, IReadOnlyList<T> items, string path) where T : notnull
    {
        var p = new Picker { Title = title, ItemsSource = items.ToList() };
        p.SetBinding(Picker.SelectedItemProperty, new Binding(path, BindingMode.TwoWay));
        return ClinicPickers.PickerBox(null, p, 260);
    }

    View BackupSection()
    {
        return Card("Backup and restore", "A backup is one password-protected file with all patient data. Keep it somewhere safe (not on this computer only).",
            Bound(nameof(NodeSettingsViewModel.EncryptionStatus), "MutedBody"),
            new HorizontalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    Action("Back up now…", nameof(NodeSettingsViewModel.BackupCommand), nameof(NodeSettingsViewModel.IsBackupIdle), secondary: false),
                    Action("Restore from a backup…", nameof(NodeSettingsViewModel.RestoreCommand), nameof(NodeSettingsViewModel.IsBackupIdle)),
                },
            },
            Bound(nameof(NodeSettingsViewModel.BackupMessage), "MutedBody"));
    }

    // ── INodeSettingsDialogs ──
    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) =>
        MainThread.InvokeOnMainThreadAsync(() => DisplayAlert(title, message, accept, cancel));

    public Task<string?> PromptPasswordAsync(string title, string message) =>
        MainThread.InvokeOnMainThreadAsync(() => DisplayPromptAsync(title, message, "OK", "Cancel", "Password", keyboard: Keyboard.Default));

    public Task InformAsync(string title, string message) =>
        MainThread.InvokeOnMainThreadAsync(() => DisplayAlert(title, message, "OK"));

    sealed class BoolToColor(Color whenTrue, Color whenFalse) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is true ? whenTrue : whenFalse;
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    sealed class ConnectionStateToBackground : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is NodeSettingsViewModel.ApiConnectionState s
                ? ClinicTheme.Color(s switch
                {
                    NodeSettingsViewModel.ApiConnectionState.Online => "PrimaryContainer",
                    NodeSettingsViewModel.ApiConnectionState.Offline => "ErrorContainer",
                    NodeSettingsViewModel.ApiConnectionState.LocalOnly => "SurfaceContainerHigh",
                    _ => "SurfaceContainerHigh",
                })
                : ClinicTheme.Color("SurfaceContainerHigh");
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    sealed class ConnectionStateToForeground : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is NodeSettingsViewModel.ApiConnectionState s
                ? ClinicTheme.Color(s switch
                {
                    NodeSettingsViewModel.ApiConnectionState.Online => "OnPrimaryContainer",
                    NodeSettingsViewModel.ApiConnectionState.Offline => "OnErrorContainer",
                    NodeSettingsViewModel.ApiConnectionState.LocalOnly => "OnSurface",
                    _ => "OnSurfaceVariant",
                })
                : ClinicTheme.Color("OnSurfaceVariant");
        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}
//JJQR - END
