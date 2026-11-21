//JJQR - AI (21 Nov 2026) - Doctor Performance, 1:1 with Project02-fe components/doctor/performance (PerformanceDashboard, KpiCards, PerformanceChart, PerformanceLists, PeriodToggle):
// header "Good morning, Dr. X" + headline + "Back to Today"; Today / This week / This month toggle; range line; error state with Retry; loading text; empty-period note;
// "Collected {phrase}" hero with Billed block + change chips + waived/refunded line; Patients seen card; "Still to collect" card; "Last 14 days" chart with Collected | Patients toggle,
// tap a bar for its day readout; "Most common diagnoses"; "Visits still to be paid"; "Follow-ups due"; "Also" strip. Numbers/wording come from DoctorPerformanceRules (unchanged).
// Data: IDoctorPerformanceService (online GET /api/doctor/performance, local fallback). Live: LiveHubSubscription (PaymentUpdated, QueueUpdated) + the web's 60 s poll.
using ClinicNode.Api.Live;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Live;
using ClinicNode.App.Views.Staff;
using ClinicNode.App.Views.Doctor.Extra; //JJQR - AI (21 Nov 2026) DoctorTemplateEditorPage / DoctorAppointmentDetailPage / DoctorExtraUi live in the Extra namespace //JJQR - END
using ClinicNode.Core.DoctorExtra;
using R = ClinicNode.Core.DoctorExtra.DoctorPerformanceRules;

namespace ClinicNode.App.Views.Doctor.Extra2;

public sealed class DoctorPerformancePage : StaffLivePage
{
    private readonly Label _title = ClinicTheme.Text("Your performance", "HeadlineMd");
    private readonly Label _desc = ClinicTheme.Text("Gathering your numbers…", "MutedBody");
    private readonly VerticalStackLayout _periods = StaffUi.Column(0);
    private readonly Label _range = ClinicTheme.Text("", "MutedSm");
    private readonly VerticalStackLayout _body = StaffUi.Column(12);
    private PerformancePeriodName _period = PerformancePeriodName.Today;
    private ChartMetric _metric = ChartMetric.Collected;
    private DateOnly? _picked;
    private DoctorPerformanceReport? _report;
    private string? _doctorName;
    private DateTimeOffset _fetchedAt;
    private bool _failed;
    private Guid? _doctorId;
    private LiveHubSubscription? _hub;
    private IDispatcherTimer? _poll;

    public DoctorPerformancePage(IServiceProvider sp) : base(sp, "Performance", live: false)
    {
        var back = StaffUi.Btn("Back to Today", async () => { if (Navigation.NavigationStack.Count > 1) await Navigation.PopAsync(); else await Shell.Current.GoToAsync("//today"); }, "SecondaryButton");
        back.HorizontalOptions = LayoutOptions.Start;
        Content = StaffUi.Scroll(new VerticalStackLayout { Spacing = 16, Children = { _title, _desc, back, LoadError, _periods, _range, _body } });
    }

    protected override async Task LoadAsync()
    {
        _doctorId = DoctorExtraUi.DoctorId(Sp);
        if (_doctorId is null) { _report = null; Render(); return; }
        try
        {
            _doctorName ??= (await Svc<IDoctorProfileService>().GetMineAsync())?.FullName;
            var r = await Svc<IDoctorPerformanceService>().GetAsync(_doctorId.Value, _period);
            _report = r; _fetchedAt = DateTimeOffset.UtcNow; _failed = false;
        }
        catch
        {
            // a silent refresh that fails keeps what is on screen; only a first load / Retry shows the error (web PerformanceDashboard)
            if (_report is null) _failed = true;
        }
        Render();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _hub ??= new LiveHubSubscription(Sp, [ClinicHubEvents.QueueUpdated, ClinicHubEvents.PaymentUpdated], ReloadAsync, e => e.DoctorId is null || e.DoctorId == _doctorId);
        _hub.Attach();
        _poll ??= Dispatcher.CreateTimer();
        _poll.Interval = TimeSpan.FromSeconds(60);
        _poll.Tick -= OnPoll; _poll.Tick += OnPoll;
        _poll.Start();
    }

    private void OnPoll(object? s, EventArgs e) => _ = ReloadAsync();

    protected override void OnDisappearing()
    {
        _hub?.Detach();
        _poll?.Stop();
        base.OnDisappearing();
    }

    private void Render()
    {
        _periods.Children.Clear();
        _body.Children.Clear();
        if (_doctorId is null)
        {
            _title.Text = "No doctor profile linked";
            _desc.Text = "This login has no staff profile on the server, so your performance cannot load. Ask your clinic administrator to link your account to a doctor profile.";
            _range.Text = "";
            return;
        }
        _periods.Children.Add(DoctorExtraUi.Tabs(R.PeriodOptions.Select(p => (p.Id.ToString(), p.Label)).ToArray(), () => _period.ToString(),
            id => { _period = Enum.Parse<PerformancePeriodName>(id); _picked = null; _report = null; _failed = false; Render(); _ = ReloadAsync(); }));

        var r = _report;
        _title.Text = r is not null ? R.Greeting(_fetchedAt, _doctorName) : "Your performance";
        _desc.Text = r is not null ? R.Headline(_period, r.Current, r.Previous)
            : _failed ? "Your numbers are safe — we just couldn't fetch them right now." : "Gathering your numbers…";
        _range.Text = r is not null ? R.RangeLabel(r.Period) : "";

        if (_failed)
        {
            _body.Children.Add(StaffUi.Card(
                ClinicTheme.Text("We couldn't load your numbers", "HeadlineSm"),
                ClinicTheme.Text("Something went wrong on our side — nothing is lost. Please try again in a moment.", "MutedBody"),
                StaffUi.Btn("Try again", () => { _failed = false; Render(); return ReloadAsync(); }, "SecondaryButton")));
            return;
        }
        if (r is null) { _body.Children.Add(ClinicTheme.Text("Gathering your numbers…", "MutedBody")); return; }

        var c = r.Current; var prev = r.Previous; var pl = R.PreviousLabel(_period);
        if (R.IsEmptyPeriod(c)) _body.Children.Add(StaffUi.Card(ClinicTheme.Text("ⓘ " + R.EmptyMessage(_period), "BodyMd")));

        // KPI row
        var hero = StaffUi.Column(6);
        hero.Children.Add(ClinicTheme.Text($"Collected {R.PeriodPhrase(_period)}", "LabelLg"));
        hero.Children.Add(ClinicTheme.Text(R.PesoLabel(c.Collected), "HeadlineLg"));
        hero.Children.Add(Chip(R.DescribeChange((double)c.Collected, (double)prev.Collected, pl)));
        var billed = StaffUi.Column(2);
        billed.Children.Add(ClinicTheme.Text("Billed", "MutedSm"));
        billed.Children.Add(ClinicTheme.Text(R.PesoLabel(c.Billed), "HeadlineMd"));
        billed.Children.Add(Chip(R.DescribeChange((double)c.Billed, (double)prev.Billed, pl)));
        hero.Children.Add(StaffUi.Card(billed));
        hero.Children.Add(ClinicTheme.Text(R.CollectedFootnote(c), "MutedSm"));

        var pat = StaffUi.Column(6);
        pat.Children.Add(ClinicTheme.Text("Patients seen", "LabelLg"));
        pat.Children.Add(ClinicTheme.Text(c.PatientsSeen.ToString(), "HeadlineLg"));
        pat.Children.Add(Chip(R.DescribeChange(c.PatientsSeen, prev.PatientsSeen, pl)));
        pat.Children.Add(ClinicTheme.Text(R.PatientsDetail(c), "MutedSm"));

        var unpaid = StaffUi.Column(6);
        unpaid.Children.Add(ClinicTheme.Text("Still to collect", "LabelLg"));
        unpaid.Children.Add(ClinicTheme.Text(R.PesoLabel(r.Unpaid.Total), "HeadlineLg"));
        unpaid.Children.Add(ClinicTheme.Text(R.UnpaidDetail(r.Unpaid.Count), "MutedSm"));
        _body.Children.Add(Cols((StaffUi.Card(hero), 1), (StaffUi.Card(pat), 1), (StaffUi.Card(unpaid), 1))); //JJQR - AI (Nov 21, 2026) - cards share the full width //JJQR - END

        _body.Children.Add(Cols((StaffUi.Card(ChartView(r)), 2), (StaffUi.Card(DiagnosesView(r)), 1)));
        _body.Children.Add(Cols((StaffUi.Card(UnpaidView(r)), 1), (StaffUi.Card(FollowUpsView(r)), 1)));

        var also = StaffUi.Wrap();
        foreach (var a in R.AlsoItems(c))
        {
            var one = StaffUi.Column(0);
            one.Children.Add(ClinicTheme.Text(a.Value, "HeadlineSm"));
            one.Children.Add(ClinicTheme.Text(a.Label, "MutedSm"));
            one.Margin = new Thickness(0, 0, 20, 8);
            also.Children.Add(one);
        }
        _body.Children.Add(StaffUi.Card(ClinicTheme.Text("ALSO", "LabelMd"), also));
    }

    //JJQR - AI (Nov 21, 2026) - a row of cards that always fills the width (star-weighted columns) instead of wrapping like tags
    private static View Cols(params (View View, double Weight)[] cells)
    {
        var g = new Grid { ColumnSpacing = 16 };
        for (var i = 0; i < cells.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(cells[i].Weight, GridUnitType.Star)));
            cells[i].View.VerticalOptions = LayoutOptions.Fill;
            g.Add(cells[i].View, i, 0);
        }
        return g;
    }
    //JJQR - END

    private static View Cell(View v) { v.Margin = new Thickness(0, 0, 12, 12); return v; }

    /// <summary>web ChangeChip: green for positive, quiet grey for a dip/neutral (never red).</summary>
    private static View Chip(PerformanceChange ch)
    {
        var pos = ch.Tone == ChangeTone.Positive;
        return new Border
        {
            HorizontalOptions = LayoutOptions.Start, Padding = new Thickness(10, 4), StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            BackgroundColor = ClinicTheme.Color(pos ? "StatusSuccessSurface" : "StatusNeutralSurface"),
            Content = new Label { Text = ch.Text, FontSize = 12, TextColor = ClinicTheme.Color(pos ? "StatusSuccess" : "StatusNeutral") },
        };
    }

    private View ChartView(DoctorPerformanceReport r)
    {
        var col = StaffUi.Column(8);
        col.Children.Add(ClinicTheme.Text("Last 14 days", "HeadlineSm"));
        col.Children.Add(ClinicTheme.Text("Completed visits, by clinic day", "MutedSm"));
        col.Children.Add(DoctorExtraUi.Tabs([("Collected", "Collected"), ("PatientsSeen", "Patients")], () => _metric.ToString(),
            id => { _metric = Enum.Parse<ChartMetric>(id); Render(); }));
        if (r.Series.All(d => d.Collected == 0 && d.PatientsSeen == 0))
        {
            col.Children.Add(ClinicTheme.Text("Your daily chart will fill in as you complete visits.", "MutedBody"));
            return col;
        }
        var today = r.Series.Count > 0 ? r.Series[^1].Date : r.Today;
        var chart = R.BuildChart(r.Series, _metric, today);
        var canvas = new AbsoluteLayout { WidthRequest = chart.Width, HeightRequest = chart.Height };
        foreach (var t in chart.Ticks)
        {
            AbsoluteLayout.SetLayoutBounds(Add(canvas, new BoxView { Color = ClinicTheme.Color("OutlineVariant") }), new Rect(48, t.Y, chart.Width - 56, 1));
            var lbl = new Label { Text = _metric == ChartMetric.Collected ? R.PesoCompact(t.Value) : ((long)t.Value).ToString(), FontSize = 11, HorizontalTextAlignment = TextAlignment.End, TextColor = ClinicTheme.Color("OnSurfaceVariant") };
            AbsoluteLayout.SetLayoutBounds(Add(canvas, lbl), new Rect(0, t.Y - 7, 42, 16));
        }
        for (var i = 0; i < chart.Bars.Count; i++)
        {
            var b = chart.Bars[i];
            var active = b.IsToday || (_picked ?? today) == b.Date;
            var hit = new BoxView { Color = Colors.Transparent };
            var date = b.Date;
            hit.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { _picked = date; Render(); }) });
            AbsoluteLayout.SetLayoutBounds(Add(canvas, hit), new Rect(b.X - 4, 12, b.Width + 8, chart.Baseline - 12));
            if (b.Height > 0)
                AbsoluteLayout.SetLayoutBounds(Add(canvas, new BoxView { CornerRadius = 4, Color = ClinicTheme.Color("Primary").WithAlpha(active ? 1f : 0.35f), InputTransparent = true }), new Rect(b.X, b.Y, b.Width, b.Height));
            if (i % 2 == (chart.Bars.Count - 1) % 2)
                AbsoluteLayout.SetLayoutBounds(Add(canvas, new Label { Text = b.Label, FontSize = 10.5, HorizontalTextAlignment = TextAlignment.Center, InputTransparent = true,
                    FontAttributes = b.IsToday ? FontAttributes.Bold : FontAttributes.None, TextColor = ClinicTheme.Color(b.IsToday ? "Primary" : "OnSurfaceVariant") }), new Rect(b.X - 10, chart.Baseline + 6, b.Width + 20, 16));
        }
        col.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = canvas });
        var day = r.Series.FirstOrDefault(d => d.Date == (_picked ?? today));
        col.Children.Add(ClinicTheme.Text(day is null ? "" : R.DayReadout(day, today), "BodyMd"));
        return col;
    }

    private static T Add<T>(AbsoluteLayout l, T v) where T : View { l.Children.Add(v); return v; }

    private static View DiagnosesView(DoctorPerformanceReport r)
    {
        var col = StaffUi.Column(8);
        col.Children.Add(ClinicTheme.Text("Most common diagnoses", "HeadlineSm"));
        col.Children.Add(ClinicTheme.Text("From your charts in this period", "MutedSm"));
        var rows = R.DiagnosisBars(r.TopDiagnoses);
        if (rows.Count == 0) { col.Children.Add(ClinicTheme.Text("Diagnoses will show up here once you add them to your charts.", "MutedBody")); return col; }
        foreach (var d in rows)
        {
            col.Children.Add(ClinicTheme.Text($"{d.Label}{(string.IsNullOrWhiteSpace(d.Code) ? "" : "  " + d.Code)}   {d.Count}", "BodyMd"));
            col.Children.Add(new BoxView { HeightRequest = 8, WidthRequest = 2.4 * d.Pct, HorizontalOptions = LayoutOptions.Start, CornerRadius = 4, Color = ClinicTheme.Color("Primary").WithAlpha(0.7f) });
        }
        return col;
    }

    private View UnpaidView(DoctorPerformanceReport r)
    {
        var col = StaffUi.Column(8);
        col.Children.Add(ClinicTheme.Text("Visits still to be paid", "HeadlineSm"));
        if (r.Unpaid.Count > 0) col.Children.Add(ClinicTheme.Text($"{R.PesoLabel(r.Unpaid.Total)} in total", "MutedSm"));
        if (r.Unpaid.Items.Count == 0) { col.Children.Add(ClinicTheme.Text("✓ Everything is settled — nothing outstanding.", "MutedBody")); return col; }
        foreach (var u in r.Unpaid.Items)
        {
            var id = u.BookingId;
            col.Children.Add(StaffUi.Row(
                ClinicTheme.Text(string.IsNullOrWhiteSpace(u.PatientName) ? "Patient" : u.PatientName, "LabelLg"),
                ClinicTheme.Text($"{DoctorExtraRules.VisitDate(u.AppointmentDate)} · {R.PesoLabel(u.AmountDue)}", "MutedSm"),
                StaffUi.Link("View appointment", () => StaffUi.PushAsync<DoctorAppointmentDetailPage>(this, id))));
        }
        var more = r.Unpaid.Count - r.Unpaid.Items.Count;
        if (more > 0) col.Children.Add(ClinicTheme.Text($"and {more} more — the front desk can see the full list.", "MutedSm"));
        return col;
    }

    private View FollowUpsView(DoctorPerformanceReport r)
    {
        var col = StaffUi.Column(8);
        col.Children.Add(ClinicTheme.Text("Follow-ups due", "HeadlineSm"));
        if (r.FollowUpsDue.Count > 0) col.Children.Add(ClinicTheme.Text(r.FollowUpsDue.Count.ToString(), "MutedSm"));
        if (r.FollowUpsDue.Items.Count == 0) { col.Children.Add(ClinicTheme.Text("✓ No follow-ups waiting on you.", "MutedBody")); return col; }
        foreach (var f in r.FollowUpsDue.Items)
        {
            var pid = f.PatientId;
            col.Children.Add(StaffUi.Row(
                ClinicTheme.Text(string.IsNullOrWhiteSpace(f.PatientName) ? "Patient" : f.PatientName, "LabelLg"),
                ClinicTheme.Text($"{(string.IsNullOrWhiteSpace(f.Reason) ? "Follow-up visit" : f.Reason!.Trim())} · {DoctorExtraRules.VisitDate(f.FollowUpDate)}", "MutedSm"),
                StaffUi.Link("Open chart", () => DoctorPatientChartPageOpen(pid))));
        }
        var more = r.FollowUpsDue.Count - r.FollowUpsDue.Items.Count;
        if (more > 0) col.Children.Add(ClinicTheme.Text($"and {more} more.", "MutedSm"));
        return col;
    }

    private Task DoctorPatientChartPageOpen(Guid patientId) => ClinicNode.App.Views.Doctor.Extra.DoctorPatientChartPage.OpenAsync(this, patientId);
}
//JJQR - END
