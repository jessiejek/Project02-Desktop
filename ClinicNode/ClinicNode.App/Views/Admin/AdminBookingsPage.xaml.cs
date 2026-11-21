// JJQR - AI (Nov 21, 2026)
using ClinicNode.App.ViewModels.Admin;
//JJQR - AI (21 Nov 2026)
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
//JJQR - END

namespace ClinicNode.App.Views.Admin;

public partial class AdminBookingsPage : ContentPage
{
    readonly AdminBookingsViewModel _vm;
    public AdminBookingsPage(AdminBookingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        //JJQR - AI (21 Nov 2026) - redraw the week grid whenever the week data changes
        _vm.WeekChanged += (_, _) => MainThread.BeginInvokeOnMainThread(BuildWeekGrid);
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - web header + tabs + List tab
        _vm.ListChanged += (_, _) => MainThread.BeginInvokeOnMainThread(RenderList);
        BuildHeader();
        BuildFilters();
        RenderList();
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - web /admin/bookings List tab
    readonly Entry _search = new() { Placeholder = "Patient name, code or booking ID" };
    readonly Picker _doctor = new();
    readonly Picker _status = new();
    readonly DatePicker _date = new() { Date = null };
    readonly VerticalStackLayout _listBody = new() { Spacing = 16 };
    readonly HorizontalStackLayout _tabs = new() { Spacing = 4 };
    CancellationTokenSource? _debounce;
    bool _syncingFilters;

    void BuildHeader()
    {
        var walkIn = new Button { Text = "+ New walk-in", Style = ClinicTheme.Style("PrimaryButton") };
        walkIn.Clicked += (_, _) => _vm.OpenWalkInCommand.Execute(null);
        Header.Children.Add(ClinicUi.PageHeader("Bookings",
            "Every visit across all doctors. Search by patient or booking ID, or switch to the week view to see the schedule.", walkIn));
        Header.Children.Add(_tabs);
        RenderTabs();
    }

    void RenderTabs()
    {
        _tabs.Children.Clear();
        foreach (var (week, label) in new[] { (false, "List"), (true, "Week view") })
        {
            var b = new Button { Text = label, Style = ClinicTheme.Style(_vm.IsWeek == week ? "SegmentedButtonSelected" : "SegmentedButton") };
            var captured = week;
            b.Clicked += async (_, _) =>
            {
                if (captured) await _vm.ShowWeekAsync(); else { _vm.ShowList(); await _vm.LoadAsync(); }
                RenderTabs();
            };
            _tabs.Children.Add(b);
        }
    }

    void BuildFilters()
    {
        _doctor.ItemsSource = _vm.DoctorOptions;
        _status.ItemsSource = _vm.StatusOptions;
        _doctor.SelectedItem = _vm.SelectedDoctor;
        _status.SelectedItem = _vm.SelectedStatus;
        _search.Text = _vm.Query;
        var dateField = new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text("Visit date", "FieldLabel"), _date, ClinicTheme.Text("Any date when empty", "FieldHint") } };
        var grid = new Grid { ColumnSpacing = 16 };
        for (var i = 0; i < 4; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.Add(ClinicUi.Field("Search", _search), 0, 0);
        grid.Add(ClinicUi.Field("Doctor", _doctor), 1, 0);
        grid.Add(ClinicUi.Field("Status", _status), 2, 0);
        grid.Add(dateField, 3, 0);
        ListArea.Children.Add(new Border { Style = ClinicTheme.Style("Card"), Content = grid });
        ListArea.Children.Add(_listBody);

        _search.TextChanged += async (_, _) => await ApplyAsync(250);
        _doctor.SelectedIndexChanged += async (_, _) => await ApplyAsync(0);
        _status.SelectedIndexChanged += async (_, _) => await ApplyAsync(0);
        _date.DateSelected += async (_, _) => await ApplyAsync(0);
    }

    async Task ApplyAsync(int delayMs)
    {
        if (_syncingFilters) return;
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        try { await Task.Delay(delayMs, cts.Token); } catch (TaskCanceledException) { return; }
        await _vm.ApplyFiltersAsync(_search.Text, _doctor.SelectedItem as string, _status.SelectedItem as string, _date.Date);
    }

    void ClearFilters()
    {
        _syncingFilters = true;
        _search.Text = "";
        _doctor.SelectedItem = "All doctors";
        _status.SelectedItem = "All statuses";
        _date.Date = null;
        _syncingFilters = false;
        _ = _vm.ApplyFiltersAsync("", null, null, null);
    }

    void RenderList()
    {
        _listBody.Children.Clear();
        if (_doctor.SelectedItem is null && _vm.DoctorOptions.Count > 0) { _syncingFilters = true; _doctor.SelectedItem = _vm.SelectedDoctor; _status.SelectedItem = _vm.SelectedStatus; _syncingFilters = false; }
        if (!_vm.Loaded) { _listBody.Children.Add(new ActivityIndicator { IsRunning = true }); return; }
        if (_vm.ListFailed)
        {
            _listBody.Children.Add(ClinicTheme.Text("Bookings unavailable", "HeadlineSm"));
            _listBody.Children.Add(ClinicTheme.Text(AdminBookingsViewModel.ListErrorText, "MutedBody"));
            _listBody.Children.Add(ClinicUi.Button(_vm.IsBusy ? "Retrying…" : "Try again", _vm.LoadAsync, ButtonKind.Secondary));
            return;
        }
        var countLine = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var count = ClinicTheme.Text(_vm.StatusMessage ?? "", "LabelMd"); count.VerticalOptions = LayoutOptions.Center;
        countLine.Add(count, 0, 0);
        if (_vm.IsFiltered)
        {
            var clear = new Button { Text = "Clear filters", Style = ClinicTheme.Style("GhostButton") };
            clear.Clicked += (_, _) => ClearFilters();
            countLine.Add(clear, 1, 0);
        }
        _listBody.Children.Add(countLine);

        var columns = new List<TableColumn>
        {
            new("Patient", new GridLength(1.6, GridUnitType.Star), o =>
            {
                var r = (BookingRow)o;
                return new Label
                {
                    Style = ClinicTheme.Style("TableCell"),
                    FormattedText = new FormattedString
                    {
                        Spans =
                        {
                            new Span { Text = string.IsNullOrWhiteSpace(r.PatientName) ? "—" : r.PatientName, FontAttributes = FontAttributes.Bold },
                            new Span { Text = string.IsNullOrWhiteSpace(r.PatientCode) ? "" : "  " + r.PatientCode, FontFamily = "Courier New", FontSize = 13, TextColor = ClinicTheme.Color("OnSurfaceVariant") },
                        },
                    },
                };
            }),
            new("Visit date", new GridLength(130), o => ClinicUi.Cell(((BookingRow)o).VisitDate)),
            new("Doctor", GridLength.Star, o => ClinicUi.Cell(((BookingRow)o).Doctor)),
            new("Status", GridLength.Auto, o => AdminPill.Make(((BookingRow)o).StatusKey)),
            new("Payment", GridLength.Auto, o =>
            {
                var r = (BookingRow)o;
                return string.IsNullOrEmpty(r.PaymentKey) ? ClinicTheme.Text("—", "MutedSm") : AdminPill.Make(r.PaymentKey);
            }),
        };
        _listBody.Children.Add(ClinicUi.Table(columns, _vm.Items.Cast<object>(), _vm.EmptyMessage,
            o => _vm.OpenBookingCommand.Execute(((BookingRow)o).BookingId)));
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - web week table: header row "Doctor | Mon 17 … Sun 23", one row per doctor, each visit a tappable chip (patient + time)
    void BuildWeekGrid()
    {
        var grid = WeekGrid;
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        var lead = _vm.ShowDoctorColumn ? 1 : 0;
        if (lead == 1) grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(160)));
        for (var d = 0; d < 7; d++) grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var headerBg = ClinicTheme.Color("SurfaceContainerLow");
        void HeaderCell(string text, int col)
        {
            var cell = new Border
            {
                StrokeThickness = 0,
                BackgroundColor = headerBg,
                Padding = new Thickness(12, 8),
                Content = ClinicTheme.Text(text, "LabelMd"),
            };
            Grid.SetRow(cell, 0); Grid.SetColumn(cell, col);
            grid.Children.Add(cell);
        }
        if (lead == 1) HeaderCell("Doctor", 0);
        for (var d = 0; d < _vm.WeekDayHeaders.Count; d++) HeaderCell(_vm.WeekDayHeaders[d], lead + d);

        //JJQR - AI (21 Nov 2026) - web: ErrorState title "Week unavailable", message "This week's visits could not be loaded. Try again.", Retry
        if (_vm.WeekFailed)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var retry = new Button { Text = "Try again", Style = ClinicTheme.Style("SecondaryButton"), HorizontalOptions = LayoutOptions.Start };
            retry.Clicked += async (_, _) => await _vm.LoadWeekAsync();
            var failed = new VerticalStackLayout
            {
                Spacing = 8, Margin = new Thickness(12, 16),
                Children =
                {
                    ClinicTheme.Text("Week unavailable", "HeadlineSm"),
                    ClinicTheme.Text("This week's visits could not be loaded. Try again.", "MutedBody"),
                    retry,
                },
            };
            Grid.SetRow(failed, 1); Grid.SetColumn(failed, 0); Grid.SetColumnSpan(failed, 7 + lead);
            grid.Children.Add(failed);
            return;
        }
        //JJQR - END
        if (_vm.WeekRows.Count == 0)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var empty = new Label { Text = string.IsNullOrEmpty(_vm.WeekEmptyMessage) ? "No doctors found." : _vm.WeekEmptyMessage, Style = ClinicTheme.Style("MutedBody"), Margin = new Thickness(12, 16) };
            Grid.SetRow(empty, 1); Grid.SetColumn(empty, 0); Grid.SetColumnSpan(empty, 7 + lead);
            grid.Children.Add(empty);
            return;
        }

        var row = 1;
        foreach (var doc in _vm.WeekRows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (lead == 1)
            {
                var name = new Label { Text = doc.Doctor, Style = ClinicTheme.Style("BodyMd"), Margin = new Thickness(12) };
                Grid.SetRow(name, row); Grid.SetColumn(name, 0);
                grid.Children.Add(name);
            }
            for (var d = 0; d < doc.Days.Count; d++)
            {
                var stack = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(12) };
                var visits = doc.Days[d].Visits;
                if (visits.Count == 0)
                    stack.Children.Add(ClinicTheme.Text("—", "MutedSm"));
                foreach (var v in visits)
                {
                    var chip = new Border
                    {
                        StrokeThickness = 0,
                        BackgroundColor = ClinicTheme.Color("Primary").WithAlpha(0.10f),
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                        Padding = new Thickness(8, 4),
                        Content = new VerticalStackLayout
                        {
                            Children =
                            {
                                new Label { Text = v.Patient, Style = ClinicTheme.Style("LabelMd"), LineBreakMode = LineBreakMode.WordWrap },
                                ClinicTheme.Text(v.Time, "MutedSm"),
                            },
                        },
                    };
                    var id = v.BookingId;
                    chip.GestureRecognizers.Add(new TapGestureRecognizer { Command = _vm.OpenBookingCommand, CommandParameter = id });
                    stack.Children.Add(chip);
                }
                Grid.SetRow(stack, row); Grid.SetColumn(stack, lead + d);
                grid.Children.Add(stack);
            }
            row++;
        }
    }
    //JJQR - END

    protected override void OnAppearing()
    {
        base.OnAppearing();
        //JJQR - AI (21 Nov 2026) - keep live refresh after navigating away and back
        _vm.Attach();
        //JJQR - END
        _ = _vm.IsWeek ? _vm.LoadWeekAsync() : _vm.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        _vm.Detach();
        base.OnDisappearing();
    }
}
// JJQR - END
