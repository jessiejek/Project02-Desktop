//JJQR - AI (21 Nov 2026) - web /admin/dashboard layout (see AdminHomePage.xaml). Live refresh on clinic events + 60 s fallback poll, as on the web.
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;

namespace ClinicNode.App.Views.Admin;

public partial class AdminHomePage : ContentPage
{
    readonly AdminDashboardViewModel _vm;
    IDispatcherTimer? _poll;

    public AdminHomePage(AdminDashboardViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ClinicNode.App.Controls.Skeleton.WrapAdminPage(this); //JJQR - AI (Nov 22, 2026) - skeleton until the first load finishes //JJQR - END
        _vm.Reloaded -= OnReloaded;
        _vm.Reloaded += OnReloaded;
        _vm.Attach();
        _poll ??= Dispatcher.CreateTimer();
        _poll.Interval = TimeSpan.FromSeconds(60);
        _poll.Tick -= OnPoll;
        _poll.Tick += OnPoll;
        _poll.Start();
        Render();
        await _vm.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        _poll?.Stop();
        _vm.Detach();
        _vm.Reloaded -= OnReloaded;
        base.OnDisappearing();
    }

    void OnPoll(object? sender, EventArgs e) => _ = _vm.LoadAsync();
    void OnReloaded(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(Render);

    static Task Go(string route) => Shell.Current.GoToAsync(route);

    void Render()
    {
        Root.Children.Clear();
        if (!_vm.Loaded) { Root.Children.Add(new ActivityIndicator { IsRunning = true }); return; }
        if (_vm.HasError)
        {
            Root.Children.Add(ClinicTheme.Text("Dashboard unavailable", "HeadlineSm"));
            Root.Children.Add(ClinicTheme.Text(_vm.Error ?? "", "MutedBody"));
            var retry = ClinicUi.Button(_vm.IsBusy ? "Retrying…" : "Try again", _vm.LoadAsync, ButtonKind.Secondary);
            Root.Children.Add(retry);
            return;
        }

        var refresh = ClinicUi.Button(_vm.IsBusy ? "Refreshing…" : "Refresh", _vm.LoadAsync, ButtonKind.Secondary);
        Root.Children.Add(ClinicUi.PageHeader("Clinic overview", _vm.Description, refresh));

        // Today
        Root.Children.Add(Section("Today", Cards(3,
            Stat("event_note", _vm.AppointmentsToday, "Appointments today", "//admin-bookings"),
            Stat("payments", _vm.RevenueToday, "Revenue today", "//admin-reports"),
            Stat("event_busy", _vm.NoShowsToday, "No-shows today", null))));

        // Needs follow-up
        Root.Children.Add(Section("Needs follow-up", Cards(4,
            Stat("pending_actions", _vm.PaymentProofs, "Payment proofs to verify", "//admin-bookings"),
            Stat("receipt_long", _vm.UnpaidCompleted, "Completed visits not yet paid", "//admin-reports"),
            Stat("hourglass_top", _vm.OnHoldThisMonth, "On hold this month", "//admin-bookings"),
            Stat("follow_the_signs", _vm.FollowUpsDue, "Follow-ups due in 7 days", null))));

        // This month (1/3) + Doctor share (2/3)
        var month = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                ClinicTheme.Text("This month", "HeadlineSm"),
                Spacer(12), ClinicTheme.Text(_vm.MonthRevenue, "HeadlineLg"), ClinicTheme.Text("Revenue", "LabelMd"),
                Spacer(12), ClinicTheme.Text(_vm.MonthAppointments, "HeadlineMd"), ClinicTheme.Text("Appointments", "LabelMd"),
            },
        };
        var share = new VerticalStackLayout { Spacing = 8, Children = { ClinicTheme.Text("Doctor share of the latest 20 bookings", "HeadlineSm") } };
        if (_vm.DoctorLoads.Count == 0) share.Children.Add(ClinicTheme.Text("No doctors to show.", "MutedBody"));
        foreach (var d in _vm.DoctorLoads)
        {
            var row = new Grid
            {
                ColumnSpacing = 16,
                ColumnDefinitions = { new ColumnDefinition(new GridLength(160)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(40)) },
            };
            var name = ClinicTheme.Text(d.Name, "LabelMd"); name.LineBreakMode = LineBreakMode.WordWrap; name.VerticalOptions = LayoutOptions.Center;
            row.Add(name, 0, 0);
            row.Add(new ProgressBar { Progress = d.BarProgress, ProgressColor = ClinicTheme.Color("Primary"), VerticalOptions = LayoutOptions.Center }, 1, 0);
            var count = ClinicTheme.Text(d.CountLabel, "LabelMd"); count.HorizontalTextAlignment = TextAlignment.End; count.VerticalOptions = LayoutOptions.Center;
            row.Add(count, 2, 0);
            share.Children.Add(row);
        }
        var split = new Grid
        {
            ColumnSpacing = 24,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(2, GridUnitType.Star)) },
        };
        split.Add(new Border { Style = ClinicTheme.Style("Card"), Content = month }, 0, 0);
        split.Add(new Border { Style = ClinicTheme.Style("Card"), Content = share }, 1, 0);
        Root.Children.Add(split);

        // Latest bookings
        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        head.Add(ClinicTheme.Text("Latest bookings", "HeadlineMd"), 0, 0);
        var all = new Button { Text = "All bookings", Style = ClinicTheme.Style("GhostButton") };
        all.Clicked += async (_, _) => await Go("//admin-bookings");
        head.Add(all, 1, 0);
        var columns = new List<TableColumn>
        {
            new("Patient", new GridLength(1.3, GridUnitType.Star), o => ClinicTheme.Text(((DashboardRecentBookingRow)o).PatientName, "LabelLg")),
            new("Doctor", GridLength.Star, o => ClinicUi.Cell(((DashboardRecentBookingRow)o).DoctorName)),
            new("Time", new GridLength(100), o => ClinicUi.Cell(((DashboardRecentBookingRow)o).Time)),
            new("Queue", new GridLength(80), o => new Label { Text = ((DashboardRecentBookingRow)o).Queue, Style = ClinicTheme.Style("TableCell"), HorizontalTextAlignment = TextAlignment.Center }),
            new("Status", GridLength.Auto, o =>
            {
                var r = (DashboardRecentBookingRow)o;
                return string.IsNullOrEmpty(r.StatusKey) ? ClinicUi.Cell(r.Status) : AdminPill.Make(r.StatusKey);
            }),
        };
        Root.Children.Add(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                head,
                ClinicUi.Table(columns, _vm.LatestBookings.Cast<object>(), "No bookings yet.",
                    o => _vm.OpenBookingCommand.Execute(((DashboardRecentBookingRow)o).BookingId)),
            },
        });
    }

    static View Spacer(double h) => new BoxView { HeightRequest = h, Color = Colors.Transparent };

    static View Section(string title, View content) => new VerticalStackLayout
    {
        Spacing = 16,
        Children = { ClinicTheme.Text(title, "HeadlineMd"), content },
    };

    static View Cards(int columns, params View[] cards)
    {
        var g = new Grid { ColumnSpacing = 16 };
        for (var i = 0; i < columns; i++) g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < cards.Length; i++) g.Add(cards[i], i % columns, 0);
        return g;
    }

    // web StatCard: icon tile (primary/10) + headline-lg value + label-md label; the whole card links when href is set
    static View Stat(string icon, string value, string label, string? route)
    {
        var iconTile = new Border
        {
            WidthRequest = 40, HeightRequest = 40,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            BackgroundColor = ClinicTheme.Color("Primary").WithAlpha(0.1f),
            HorizontalOptions = LayoutOptions.Start,
            Content = new ClinicIcon { Name = icon, IconSize = 20, IconColor = ClinicTheme.Color("Primary"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        var card = new Border
        {
            Style = ClinicTheme.Style("Card"),
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children = { iconTile, Spacer(8), ClinicTheme.Text(value, "HeadlineLg"), ClinicTheme.Text(label, "LabelMd") },
            },
        };
        if (route is not null)
            card.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Go(route)) });
        return card;
    }
}
//JJQR - END
