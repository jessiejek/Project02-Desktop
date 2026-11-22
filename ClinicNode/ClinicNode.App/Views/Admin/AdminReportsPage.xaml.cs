//JJQR - AI (21 Nov 2026) - web /admin/reports layout (see AdminReportsPage.xaml)
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.Views.Admin;

public partial class AdminReportsPage : ContentPage
{
    readonly AdminReportsViewModel _vm;

    public AdminReportsPage(AdminReportsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Render();
        await _vm.LoadWebAsync();
        Render();
    }

    static string Short(DateOnly d) => AdminViewModelBase.ClinicDateShort(d);
    static string Money(decimal v) => AdminViewModelBase.Peso(v);
    static string MoneyOrZero(decimal v) => v == 0 ? "₱0" : AdminViewModelBase.Peso(v);

    async Task ReloadAsync() { Render(); await _vm.LoadWebAsync(); Render(); }

    void Render()
    {
        Root.Children.Clear();
        var from = DateOnly.FromDateTime(_vm.DateFrom);
        var to = DateOnly.FromDateTime(_vm.DateTo);

        var export = ClinicUi.Button("Export CSV", async () =>
        {
            var msg = await _vm.ExportWebAsync();
            if (msg is not null) ClinicToast.Show(msg, msg.StartsWith("Saved") ? ToastKind.Success : ToastKind.Error);
        }, ButtonKind.Secondary);
        export.IsEnabled = _vm.Loaded && _vm.CanExport && !_vm.RangeInvalid;
        export.ImageSource = new FontImageSource { FontFamily = ClinicIcons.SolidFamily, Glyph = "\uF019", Size = 14, Color = ClinicTheme.Color("Primary") }; // web <Icon name="download">
        Root.Children.Add(ClinicUi.PageHeader("Clinic reports", $"Revenue, unpaid visits and follow-ups for {Short(from)} – {Short(to)}.", export));

        // From / To
        var fromPicker = new DatePicker { Date = _vm.DateFrom, WidthRequest = 176 };
        var toPicker = new DatePicker { Date = _vm.DateTo, WidthRequest = 176 };
        fromPicker.DateSelected += async (_, e) => { if (e.NewDate is { } d) { _vm.DateFrom = d; await ReloadAsync(); } };
        toPicker.DateSelected += async (_, e) => { if (e.NewDate is { } d) { _vm.DateTo = d; await ReloadAsync(); } };
        var range = new HorizontalStackLayout
        {
            Spacing = 16,
            Children =
            {
                new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text("From", "FieldLabel"), ClinicPickers.DateBox(null, fromPicker) } },
                new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text("To", "FieldLabel"), ClinicPickers.DateBox(null, toPicker) } },
            },
        };
        var rangeCard = new VerticalStackLayout { Spacing = 12, Children = { range } };
        if (_vm.RangeInvalid)
            rangeCard.Children.Add(ClinicUi.Alert($"The start date is after the end date. Pick a start on or before {Short(to)}.", success: false));
        Root.Children.Add(new Border { Style = ClinicTheme.Style("Card"), Content = rangeCard });

        if (_vm.RangeInvalid) return;
        if (_vm.LoadFailed)
        {
            Root.Children.Add(ClinicTheme.Text("Reports unavailable", "HeadlineSm"));
            Root.Children.Add(ClinicTheme.Text("Report data could not be loaded for this range. No totals are shown while the request has failed.", "MutedBody"));
            Root.Children.Add(ClinicUi.Button("Try again", ReloadAsync, ButtonKind.Secondary));
            return;
        }
        if (!_vm.Loaded || _vm.IsBusy) { Root.Children.Add(new ActivityIndicator { IsRunning = true }); return; }

        var daily = _vm.Daily;
        var unpaid = _vm.Unpaid;
        var revenue = daily.Sum(r => r.Revenue);
        var unpaidTotal = unpaid.Sum(r => r.AmountDue);

        // stat tiles
        var tiles = new Grid { ColumnSpacing = 16 };
        var stats = new (string Label, string Value)[]
        {
            ("Revenue", MoneyOrZero(revenue)),
            ("Unpaid after visit", MoneyOrZero(unpaidTotal)),
            ("Visits", daily.Sum(r => r.TotalBookings).ToString()),
            ("No-shows", daily.Sum(r => r.NoShowCount).ToString()),
        };
        for (var i = 0; i < stats.Length; i++)
        {
            tiles.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var value = ClinicTheme.Text(stats[i].Value, "HeadlineLg");
            if (i == 0) value.TextColor = ClinicTheme.Color("Primary");
            tiles.Add(new Border
            {
                Style = ClinicTheme.Style("Card"),
                Content = new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text(stats[i].Label, "LabelMd"), value } },
            }, i, 0);
        }
        Root.Children.Add(tiles);

        // Completed visits not yet paid
        var unpaidCard = new VerticalStackLayout { Spacing = 16 };
        var unpaidHead = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        unpaidHead.Add(ClinicTheme.Text("Completed visits not yet paid", "HeadlineSm"), 0, 0);
        unpaidHead.Add(ClinicTheme.Text($"{unpaid.Count} visits · {MoneyOrZero(unpaidTotal)}", "LabelMd"), 1, 0);
        unpaidCard.Children.Add(unpaidHead);
        if (unpaid.Count == 0)
            unpaidCard.Children.Add(ClinicTheme.Text("Every completed visit in this range is settled.", "MutedBody"));
        else
        {
            var columns = new List<TableColumn>
            {
                new("Patient", new GridLength(1.4, GridUnitType.Star), o => ClinicTheme.Text(((UnpaidCompletedRow)o).PatientName, "LabelLg")),
                new("Doctor", GridLength.Star, o => ClinicUi.Cell(((UnpaidCompletedRow)o).DoctorName)),
                new("Visit date", new GridLength(130), o => ClinicUi.Cell(Short(((UnpaidCompletedRow)o).AppointmentDate))),
                new("Amount due", new GridLength(120), o => new Label { Text = Money(((UnpaidCompletedRow)o).AmountDue), Style = ClinicTheme.Style("TableCell"), HorizontalTextAlignment = TextAlignment.End }),
                new("", new GridLength(160), o =>
                {
                    var link = new Button { Text = "Open booking", Style = ClinicTheme.Style("GhostButton"), HorizontalOptions = LayoutOptions.End };
                    var id = ((UnpaidCompletedRow)o).BookingId;
                    link.Clicked += (_, _) => _vm.OpenBookingCommand.Execute(id);
                    return link;
                }),
            };
            unpaidCard.Children.Add(ClinicUi.Table(columns, unpaid.Cast<object>(), ""));
        }
        Root.Children.Add(new Border { Style = ClinicTheme.Style("Card"), Content = unpaidCard });

        // Pending follow-ups
        var fuCard = new VerticalStackLayout { Spacing = 16, Children = { ClinicTheme.Text("Pending follow-ups", "HeadlineSm") } };
        if (_vm.FollowUps.Count == 0)
            fuCard.Children.Add(ClinicTheme.Text("No pending follow-ups in this range.", "MutedBody"));
        else
        {
            var list = new VerticalStackLayout();
            foreach (var f in _vm.FollowUps)
            {
                var g = new Grid { Padding = new Thickness(0, 8), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                g.Add(new VerticalStackLayout
                {
                    Children =
                    {
                        ClinicTheme.Text(string.IsNullOrWhiteSpace(f.PatientName) ? "Unknown Patient" : f.PatientName, "LabelLg"),
                        ClinicTheme.Text($"{(string.IsNullOrWhiteSpace(f.Reason) ? "Follow-up" : f.Reason)} · {(string.IsNullOrWhiteSpace(f.DoctorName) ? "Unknown Doctor" : f.DoctorName)}", "LabelMd"),
                    },
                }, 0, 0);
                g.Add(ClinicTheme.Text(Short(f.FollowUpDate), "LabelMd"), 1, 0);
                if (list.Children.Count > 0) list.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") });
                list.Children.Add(g);
            }
            fuCard.Children.Add(list);
        }
        Root.Children.Add(new Border { Style = ClinicTheme.Style("Card"), Content = fuCard });

        // Day by day
        var dayCard = new VerticalStackLayout { Spacing = 16, Children = { ClinicTheme.Text("Day by day", "HeadlineSm") } };
        if (daily.Count == 0)
            dayCard.Children.Add(ClinicTheme.Text("No bookings in this range.", "MutedBody"));
        else
        {
            static View Right(string t, bool bold = false) => new Label { Text = t, Style = ClinicTheme.Style("TableCell"), HorizontalTextAlignment = TextAlignment.End, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None };
            var rows = daily.Cast<object>().ToList();
            var total = new DailySummaryRow(default, daily.Sum(r => r.TotalBookings), daily.Sum(r => r.CompletedCount), daily.Sum(r => r.PaidCount), daily.Sum(r => r.UnpaidCount), daily.Sum(r => r.NoShowCount), revenue);
            rows.Add(total);
            bool IsTotal(object o) => ReferenceEquals(o, total);
            var columns = new List<TableColumn>
            {
                new("Date", new GridLength(1.4, GridUnitType.Star), o => IsTotal(o) ? new Label { Text = "Total", Style = ClinicTheme.Style("TableCell"), FontAttributes = FontAttributes.Bold } : ClinicUi.Cell(Short(((DailySummaryRow)o).AppointmentDate))),
                new("Total", GridLength.Star, o => Right(((DailySummaryRow)o).TotalBookings.ToString(), IsTotal(o))),
                new("Completed", GridLength.Star, o => Right(((DailySummaryRow)o).CompletedCount.ToString(), IsTotal(o))),
                new("Paid", GridLength.Star, o => Right(((DailySummaryRow)o).PaidCount.ToString(), IsTotal(o))),
                new("Unpaid", GridLength.Star, o => Right(((DailySummaryRow)o).UnpaidCount.ToString(), IsTotal(o))),
                new("No-shows", GridLength.Star, o => Right(((DailySummaryRow)o).NoShowCount.ToString(), IsTotal(o))),
                new("Revenue", new GridLength(1.3, GridUnitType.Star), o => Right(MoneyOrZero(((DailySummaryRow)o).Revenue), IsTotal(o))),
            };
            dayCard.Children.Add(ClinicUi.Table(columns, rows, ""));
        }
        Root.Children.Add(new Border { Style = ClinicTheme.Style("Card"), Content = dayCard });
    }
}
//JJQR - END
