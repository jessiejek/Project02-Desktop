// JJQR - AI (Nov 21, 2026) - doctor "Your clinic today", ported 1:1 for function from Project02-fe /doctor/dashboard.
// Header actions, Your patient queue (up-next Start/Resume cards + Needs attention), Availability (day status), Clinic reports & recent bookings,
// Practice analytics (month / year / custom), Clinic hours link, announcements. Code-built (functionality first, shared style keys only).
using System.Globalization;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.Services;
using ClinicNode.Core.Print;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.Auth;
using ClinicNode.Data;
using ClinicNode.Core.Events;
using ClinicNode.Core.Sync;
using ClinicNode.Core.Practice;
using ClinicNode.Sync;
//JJQR - AI (21 Nov 2026) - live hub (docs/ONLINE-FIRST.md §9)
using ClinicNode.Api.Live;
using ClinicNode.App.Views.Shared.Live;
using ClinicNode.Core.Doctor;
//JJQR - END
//JJQR - AI (21 Nov 2026) - 1:1 pass: Needs-attention "unpaid" + Recent Bookings open the appointment detail (web /doctor/appointments/{id})
using ClinicNode.App.Views.Doctor.Extra;
//JJQR - END

namespace ClinicNode.App.Views.Doctor;

public sealed class TodayPage : ContentPage
{
    private readonly IAuthService _auth;
    private readonly IDoctorDashboardService _dash;
    private readonly ISyncStatusSource _sync;
    private readonly IClinicEventBus _events;
    // JJQR - AI (Nov 21, 2026)
    private readonly IPrintDocumentFactory _print;
    private readonly NodeOptions _options;
    private string? _actionError;
    // JJQR - END
    private LiveRefresh? _live;
    //JJQR - AI (21 Nov 2026) - /hubs/clinic push (web useClinicHubEvent PatientCheckedIn / QueueUpdated / PaymentUpdated); LiveRefresh stays as the poll fallback
    private readonly IServiceProvider _services;
    private LiveHubSubscription? _hub;
    //JJQR - END

    private readonly Label _greeting = ClinicTheme.Text("", "MutedBody");
    private readonly Label _syncLabel = ClinicTheme.Text("", "MutedSm");
    private readonly Label _statusError = StaffUi.Error();
    private readonly VerticalStackLayout _body = new() { Spacing = 24 };

    private DoctorDashboard? _data;
    private bool _loadError;
    private string? _dayStatusError;
    private DoctorDayStatusEnum? _savingStatus;
    private bool _reportsOpen;

    private enum Range { Month, Year, Custom }
    private Range _range = Range.Month;
    //JJQR - AI (21 Nov 2026) - web: From / To start empty and totals load as soon as both are picked (no "Show totals" button)
    private DateTime? _customFrom;
    private DateTime? _customTo;
    private bool _customApplied => _customFrom is not null && _customTo is not null;
    private bool _loadingCustom;
    //JJQR - END
    private PracticeAnalytics? _analytics;
    private bool _analyticsError;

    public TodayPage(IAuthService auth, IDoctorDashboardService dash, ISyncStatusSource sync, IClinicEventBus events, IPrintDocumentFactory print, NodeOptions options,
        //JJQR - AI (21 Nov 2026)
        IServiceProvider services)
        //JJQR - END
    {
        //JJQR - AI (21 Nov 2026)
        _services = services;
        //JJQR - END
        _auth = auth;
        _dash = dash;
        _sync = sync;
        _events = events;
        // JJQR - AI (Nov 21, 2026)
        _print = print;
        _options = options;
        // JJQR - END
        Title = "Your clinic today";
        BackgroundColor = ClinicTheme.Color("Background");
        Content = new ScrollView { Content = new ContentView { Padding = 24, Content = _body } };
        //JJQR - AI (Nov 23, 2026) - owner (Windows): "Today doesn't show anything until I open another page and come back". Right after sign-in the first OnAppearing can be missed or run before the account is ready.
        // Start from Loaded and NavigatedTo too (all safe to repeat), and re-check a few seconds later that something is on screen.
        Loaded += (_, _) => EnsureLoaded();
        NavigatedTo += (_, _) => EnsureLoaded();
        //JJQR - AI (Nov 23, 2026) - owner (Windows, Offline mode): Today stayed empty after switching mode. The Connection panel closes without Today appearing again on Windows, so reload when the work mode changes.
        if (services.GetService(typeof(IWorkModeService)) is IWorkModeService mode)
            mode.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(() => { _ = ReloadAsync(); });
        //JJQR - END
        //JJQR - END
    }

    //JJQR - AI (Nov 23, 2026)
    void EnsureLoaded()
    {
        if (_body.Children.Count == 0) OnAppearing();
    }
    //JJQR - END

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _syncLabel.Text = _sync.Current.Banner;
        //JJQR - AI (21 Nov 2026) - web fallback poll is 60 s
        _live ??= new LiveRefresh(this, _events, ReloadAsync, TimeSpan.FromSeconds(60));
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - events without a doctor id are "maybe mine"
        _hub ??= new LiveHubSubscription(_services,
            [ClinicHubEvents.PatientCheckedIn, ClinicHubEvents.QueueUpdated, ClinicHubEvents.PaymentUpdated],
            ReloadAsync,
            e => e.DoctorId is null || DoctorId is not { } me || e.DoctorId == me);
        _hub.Attach();
        //JJQR - END
        _hideSkeleton ??= ClinicNode.App.Controls.Skeleton.CoverPage(this); //JJQR - AI (Nov 22, 2026) - skeleton until the first load finishes //JJQR - END
        _ = ReloadAsync();
        //JJQR - AI (Nov 23, 2026) - watchdog: if nothing is on screen after 3 s (first load lost), load again
        Dispatcher.StartTimer(TimeSpan.FromSeconds(3), () => { if (_body.Children.Count == 0) _ = ReloadAsync(); return false; });
        //JJQR - END
    }

    protected override void OnDisappearing()
    {
        _live?.Dispose();
        _live = null;
        //JJQR - AI (21 Nov 2026)
        _hub?.Detach();
        //JJQR - END
        base.OnDisappearing();
    }

    Guid? DoctorId => _auth.Current?.DoctorId;

    private bool _reloading; //JJQR - AI (Nov 23, 2026) - one load at a time //JJQR - END

    async Task ReloadAsync()
    {
        if (_reloading) return;
        _reloading = true;
        try
        {
            //JJQR - AI (Nov 23, 2026) - right after sign-in the doctor profile can be a moment late: wait up to ~3 s before saying there is no profile
            for (var i = 0; i < 6 && DoctorId is null; i++) await Task.Delay(500);
            //JJQR - END
            if (DoctorId is not { } id)
            {
                _body.Children.Clear();
                _body.Children.Add(ClinicTheme.Text("No doctor profile linked", "HeadlineSm"));
                _body.Children.Add(ClinicTheme.Text("This login can sign in as Doctor but has no staff profile on the server, so the dashboard cannot load. Ask your clinic administrator to link your account to a doctor profile.", "MutedBody"));
                return;
            }
            //JJQR - AI (Nov 22, 2026) - the two server reads do not depend on each other: ask for both at the same time (was one after the other)
            var analyticsTask = LoadAnalyticsAsync(id);
            //JJQR - END
            try { _data = await _dash.GetAsync(id); _loadError = false; }
            catch (Exception ex)
            {
                // JJQR - AI (Nov 21, 2026) - persist the real cause next to clinic-node.db
                NodeErrorLog.Write(_options, "TodayPage.GetAsync", ex);
                _loadError = true;
                // JJQR - END
            }
            await analyticsTask;
            try { Render(); }
            catch (Exception ex)
            {
                //JJQR - AI (Nov 23, 2026) - never leave the page blank: log it and show a retry
                NodeErrorLog.Write(_options, "TodayPage.Render", ex);
                _body.Children.Clear();
                _body.Children.Add(ClinicTheme.Text("Today could not be shown", "HeadlineSm"));
                _body.Children.Add(StaffUi.Btn("Try again", () => { _ = ReloadAsync(); return Task.CompletedTask; }, "SecondaryButton"));
                //JJQR - END
            }
        }
        finally
        {
            _reloading = false;
            _hideSkeleton?.Invoke(); //JJQR - AI (Nov 22, 2026) - first load done (also when it failed) //JJQR - END
        }
    }
    private Action? _hideSkeleton; //JJQR - AI (Nov 22, 2026) //JJQR - END

    async Task LoadAnalyticsAsync(Guid id)
    {
        try
        {
            var today = ClinicClock.Today;
            var (from, to, month) = _range switch
            {
                Range.Month => (new DateOnly(today.Year, today.Month, 1), today, true),
                Range.Year => (new DateOnly(today.Year, 1, 1), today, false),
                //JJQR - AI (21 Nov 2026)
                _ => (DateOnly.FromDateTime(_customFrom ?? DateTime.Today), DateOnly.FromDateTime(_customTo ?? DateTime.Today), false),
                //JJQR - END
            };
            if (_range == Range.Custom && !_customApplied) { _analytics = null; _analyticsError = false; return; }
            //JJQR - AI (21 Nov 2026) - web "Loading…" while the custom range loads
            if (_range == Range.Custom) { _loadingCustom = true; }
            try { _analytics = await _dash.GetAnalyticsAsync(id, from, to, month); }
            finally { _loadingCustom = false; }
            //JJQR - END
            _analyticsError = false;
        }
        catch (Exception ex)
        {
            // JJQR - AI (Nov 21, 2026)
            NodeErrorLog.Write(_options, "TodayPage.GetAnalyticsAsync", ex);
            _analyticsError = true;
            // JJQR - END
        }
    }

    // ---- rendering ----
    //JJQR - AI (Nov 23, 2026) - owner (Windows): "Today shows for a few milliseconds, then nothing". Render reuses a few views (greeting, sync label, error box). They stayed children of the previous render's panels, and Windows refuses to add a view that already has a parent (the Mac allows it), so the second render died half-way and left the page empty.
    // Take every old panel apart first so the reused views have no parent.
    static void Detach(IView? v)
    {
        if (v is not Microsoft.Maui.Controls.Layout l) return;
        foreach (var c in l.Children.ToList()) Detach(c);
        l.Children.Clear();
    }
    void Render()
    {
        Detach(_body);
        _body.Children.Clear();
        var nameDate = $"{_auth.Current?.FullName ?? _data?.DoctorName ?? "Doctor"}  ·  {ClinicClock.Today.ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-PH"))}";
        _greeting.Text = nameDate;
        _syncLabel.Text = _sync.Current.Banner;

        //JJQR - AI (Nov 21, 2026) - header: title, date and sync status stacked on the left; the three actions on the right (no more overlap)
        _syncLabel.FontSize = 12;
        var headLeft = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { TitleWithStatus(), _greeting, _syncLabel } };
        var headActions = new HorizontalStackLayout
        {
            Spacing = 8, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                StaffUi.Btn("Your performance", () => GoAsync("//performance"), "SecondaryButton"),
                StaffUi.Btn("New walk-in consult", () => GoAsync("walkin"), "SecondaryButton"),
                StaffUi.Btn("Open appointments", () => GoAsync("//appointments")),
            },
        };
        var head = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        head.Add(headLeft, 0, 0);
        head.Add(headActions, 1, 0);
        _body.Children.Add(head);
        //JJQR - END
        if (_dayStatusError is not null) { StaffUi.Show(_statusError, _dayStatusError); _body.Children.Add(_statusError); }
        // JJQR - AI (Nov 21, 2026)
        else if (_actionError is not null) { StaffUi.Show(_statusError, _actionError); _body.Children.Add(_statusError); }
        // JJQR - END

        //JJQR - AI (Nov 21, 2026) - desktop dashboard: two columns instead of one long stack. Left = the work (queue, announcements, reports); right = availability, analytics, hours.
        var left = new VerticalStackLayout { Spacing = 20 };
        var right = new VerticalStackLayout { Spacing = 20 };
        left.Children.Add(QueueCard());
        if (_data?.Announcements is { Count: > 0 } ann) left.Children.Add(AnnouncementsCard(ann));
        left.Children.Add(ReportsCard());
        right.Children.Add(AvailabilityCard());
        right.Children.Add(AnalyticsCard());
        right.Children.Add(StaffUi.Card(
            ClinicTheme.Text("Clinic hours & days away", "HeadlineSm"),
            ClinicTheme.Text("Review your saved weekly hours and blocked dates before changing your availability.", "MutedBody"),
            StaffUi.Link("Manage availability", () => GoAsync("//availability"))));
        var cols = new Grid { ColumnSpacing = 20, ColumnDefinitions = { new ColumnDefinition(new GridLength(3, GridUnitType.Star)), new ColumnDefinition(new GridLength(2, GridUnitType.Star)) } };
        cols.Add(left, 0, 0);
        cols.Add(right, 1, 0);
        //JJQR - AI (Nov 21, 2026) - narrow windows: the dashboard stacks to ONE column (queue first, side cards below) so the queue table never gets squeezed
        var stacked = false;
        cols.SizeChanged += (_, _) =>
        {
            var narrow = cols.Width > 0 && cols.Width < 1080;
            if (narrow == stacked) return;
            stacked = narrow;
            cols.ColumnDefinitions.Clear(); cols.RowDefinitions.Clear();
            if (narrow)
            {
                cols.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                cols.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); cols.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                cols.RowSpacing = 20;
                Grid.SetColumn(left, 0); Grid.SetRow(left, 0); Grid.SetColumn(right, 0); Grid.SetRow(right, 1);
            }
            else
            {
                cols.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(3, GridUnitType.Star))); cols.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(2, GridUnitType.Star)));
                cols.RowSpacing = 0;
                Grid.SetColumn(left, 0); Grid.SetRow(left, 0); Grid.SetColumn(right, 1); Grid.SetRow(right, 0);
            }
        };
        //JJQR - END
        _body.Children.Add(cols);
        //JJQR - END
    }

    static T Margin<T>(T v) where T : View { v.Margin = new Thickness(0, 8, 8, 0); return v; }

    //JJQR - AI (21 Nov 2026) - 1:1 helpers
    View TitleWithStatus()
    {
        var row = new HorizontalStackLayout { Spacing = 12 };
        row.Children.Add(ClinicTheme.Text("Your clinic today", "HeadlineLg"));
        if (!_loadError && _data is { } d)
        {
            var pill = ClinicTheme.MakeStatusPill(d.DayStatus.ToString());
            pill.VerticalOptions = LayoutOptions.Center;
            row.Children.Add(pill);
        }
        return row;
    }

    /// <summary>web "Needs attention" unpaid row: "{name} — completed, unpaid" · "₱{amount} due" (normalises the online feed's wording).</summary>
    static (string Text, string Aside) UnpaidLine(AttentionItem a)
    {
        var aside = a.Aside.EndsWith(" due", StringComparison.Ordinal) ? a.Aside
            : a.Text.EndsWith(" still to collect", StringComparison.Ordinal) ? a.Text[..^" still to collect".Length] + " due"
            : a.Aside;
        return ($"{(string.IsNullOrWhiteSpace(a.PatientName) ? "A patient" : a.PatientName)} — completed, unpaid", aside);
    }

    /// <summary>web StatCard-style figure: big number + muted label (with an optional coloured suffix).</summary>
    static View Figure(string value, string label, string? suffix = null, string? suffixColor = null)
    {
        var caption = new Label { Style = ClinicTheme.Style("MutedSm") };
        var fs = new FormattedString();
        fs.Spans.Add(new Span { Text = label });
        if (suffix is not null) fs.Spans.Add(new Span { Text = " " + suffix, TextColor = ClinicTheme.Color(suffixColor ?? "OnSurfaceVariant") });
        caption.FormattedText = fs;
        return new VerticalStackLayout { Spacing = 2, Margin = new Thickness(0, 0, 32, 8), Children = { ClinicTheme.Text(value, "HeadlineMd"), caption } };
    }

    Task OpenAppointmentAsync(Guid bookingId) =>
        bookingId == Guid.Empty ? GoAsync("//appointments") : StaffUi.PushAsync<DoctorAppointmentDetailPage>(this, bookingId);
    //JJQR - END


    // JJQR - AI (Nov 21, 2026)
    async Task CallNextAsync()
    {
        if (DoctorId is not { } id) return;
        _actionError = null;
        //JJQR - AI (21 Nov 2026) - G9: online-first (PUT /api/queue/{id}/call), local fallback
        var res = await Queue.CallNextAsync(id);
        //JJQR - END
        if (!res.Succeeded || res.Id is null)
        {
            _actionError = res.Error ?? "No one is waiting to be called.";
            Render();
            return;
        }
        await GoAsync($"consult?bookingId={res.Id}");
    }

    async Task StartConsultAsync(Guid bookingId, bool resume)
    {
        if (DoctorId is not { } id) return;
        _actionError = null;
        if (!resume)
        {
            //JJQR - AI (21 Nov 2026) - G9: online-first (PUT /api/queue/{id}/call), local fallback
            var res = await Queue.StartConsultAsync(id, bookingId);
            //JJQR - END
            if (!res.Succeeded)
            {
                _actionError = res.Error ?? "This patient is not ready to consult.";
                Render();
                return;
            }
        }
        await GoAsync($"consult?bookingId={bookingId}");
    }

    View PrintButtons(Guid bookingId, bool hasRx, bool hasLabs)
    {
        if (!hasRx && !hasLabs) return new ContentView { IsVisible = false };
        var row = new HorizontalStackLayout { Spacing = 8 };
        if (hasRx)
            row.Children.Add(StaffUi.Btn("Print Rx", () => PrintActions.RunAsync(this, () => _print.PrescriptionAsync(bookingId)), "SecondaryButton"));
        if (hasLabs)
            row.Children.Add(StaffUi.Btn("Print Labs", () => PrintActions.RunAsync(this, () => _print.LabRequestAsync(bookingId)), "SecondaryButton"));
        return row;
    }
    // JJQR - END

    static Task GoAsync(string route) => Shell.Current.GoToAsync(route);

    //JJQR - AI (21 Nov 2026) - registered by DoctorModule.AddDoctorModule(); before that line is wired the local dashboard service is used as before
    IConsultationQueueService Queue => _services.GetService(typeof(IConsultationQueueService)) as IConsultationQueueService ?? new LocalConsultationQueueService(_dash);
    //JJQR - END

    View QueueCard()
    {
        var col = new VerticalStackLayout { Spacing = 12 };
        var d = _data;
        string Num(int n) => _loadError || d is null ? "—" : n.ToString();
        //JJQR - AI (Nov 21, 2026) - queue header: title on the left; on the right three count chips and the "Call next" action
        View CountChip(string label, string value, string bg, string fg) => new Border
        {
            StrokeThickness = 0, BackgroundColor = ClinicTheme.Color(bg), Padding = new Thickness(12, 6), VerticalOptions = LayoutOptions.Center,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Content = new HorizontalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label { Text = value, FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, TextColor = ClinicTheme.Color(fg) },
                    new Label { Text = label, FontSize = 13, TextColor = ClinicTheme.Color(fg), VerticalOptions = LayoutOptions.Center },
                },
            },
        };
        var topRight = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        topRight.Children.Add(CountChip("waiting", Num(d?.Waiting ?? 0), "StatusWarningSurface", "StatusWarning"));
        topRight.Children.Add(CountChip("in progress", Num(d?.InProgress ?? 0), "StatusInfoSurface", "StatusInfo"));
        topRight.Children.Add(CountChip("completed", Num(d?.CompletedToday ?? 0), "StatusSuccessSurface", "StatusSuccess"));
        if (!_loadError && d is { Waiting: > 0 }) topRight.Children.Add(StaffUi.Btn("Call next", CallNextAsync));
        var topRow = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        topRow.Add(new Label { Text = "Your patient queue", FontFamily = ClinicTheme.FontSemiBold, FontSize = 18, VerticalOptions = LayoutOptions.Center }, 0, 0);
        topRow.Add(topRight, 1, 0);
        col.Children.Add(topRow);
        //JJQR - END

        if (_loadError || d is null)
        {
            col.Children.Add(ClinicTheme.Text("Your queue is unavailable. No waiting-patient totals are shown.", "MutedBody"));
            col.Children.Add(StaffUi.Btn("Reload queue", ReloadAsync, "SecondaryButton"));
            return Border(col);
        }

        if (d.UpNext.Count == 0)
            col.Children.Add(ClinicTheme.Text(d.TotalToday == 0 ? "No one's checked in yet today." : $"Queue's clear — {d.CompletedToday} seen today.", "MutedSm"));
        // JJQR - AI (Nov 21, 2026) - start consult, compact print when Rx/labs exist.
        //JJQR - AI (Nov 21, 2026) - the queue as a compact TABLE: queue no. | patient | visit | status | action (one row per patient, easy to scan)
        if (d.UpNext.Count > 0)
        {
            var cols = new[] { new GridLength(92), GridLength.Star, new GridLength(110), new GridLength(150), new GridLength(170) }; //JJQR - AI (Nov 21, 2026) fixed action column so every row lines up //JJQR - END
            Grid RowGrid(Thickness pad)
            {
                var g = new Grid { ColumnSpacing = 16, Padding = pad };
                foreach (var w in cols) g.ColumnDefinitions.Add(new ColumnDefinition(w));
                return g;
            }
            var head = RowGrid(new Thickness(14, 8));
            head.BackgroundColor = ClinicTheme.Color("SurfaceContainerLow");
            string[] heads = ["QUEUE", "PATIENT", "VISIT", "STATUS", ""];
            for (var h = 0; h < heads.Length; h++) head.Add(ClinicTheme.Text(heads[h], "LabelSm"), h, 0);
            var table = new VerticalStackLayout { Spacing = 0 };
            table.Children.Add(head);
            foreach (var u in d.UpNext)
            {
                var id = u.BookingId;
                var resume = u.InProgress;
                var row = RowGrid(new Thickness(14, 12));
                row.BackgroundColor = resume ? ClinicTheme.Color("PrimarySubtle") : ClinicTheme.Color("SurfaceContainerLowest");
                // queue number: priority lane (S-) gets an amber tag, regular (R-) a neutral one
                var num = new Border
                {
                    StrokeThickness = 0, Padding = new Thickness(10, 5), HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                    BackgroundColor = ClinicTheme.Color(u.IsPriority ? "StatusWarningSurface" : "SurfaceContainer"),
                    Content = new Label { Text = u.QueueNumber, FontFamily = ClinicTheme.FontSemiBold, FontSize = 14, TextColor = ClinicTheme.Color(u.IsPriority ? "StatusWarning" : "OnSurface") },
                };
                var who = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children = { new Label { Text = u.PatientName, FontFamily = ClinicTheme.FontSemiBold, FontSize = 15 } } };
                if (u.IsPriority) who.Children.Add(new Label { Text = "Priority · Senior / PWD", FontSize = 12, TextColor = ClinicTheme.Color("StatusWarning") });
                var visit = new Label { Text = u.VisitType == ClinicApp.Domain.Enums.VisitType.FollowUp ? "Follow-up" : "New visit", FontSize = 14, TextColor = ClinicTheme.Color("OnSurfaceVariant"), VerticalOptions = LayoutOptions.Center };
                var status = new Border
                {
                    StrokeThickness = 0, Padding = new Thickness(10, 5), HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                    BackgroundColor = ClinicTheme.Color(resume ? "StatusInfoSurface" : "StatusNeutralSurface"),
                    Content = new Label { Text = resume ? "In progress" : $"Waiting {u.WaitMinutes}m", FontFamily = ClinicTheme.FontMedium, FontSize = 12, TextColor = ClinicTheme.Color(resume ? "StatusInfo" : "StatusNeutral") },
                };
                var act = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
                var print = PrintButtons(id, u.HasPrescription, u.HasLabOrders);
                if (print is not null) act.Children.Add(print);
                act.Children.Add(StaffUi.Btn(resume ? "Resume" : "Start", () => StartConsultAsync(id, resume), resume ? "PrimaryButton" : "SecondaryButton"));
                row.Add(num, 0, 0); row.Add(who, 1, 0); row.Add(visit, 2, 0); row.Add(status, 3, 0); row.Add(act, 4, 0);
                table.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.6f) });
                table.Children.Add(row);
            }
            col.Children.Add(new Border
            {
                StrokeThickness = 1, Stroke = ClinicTheme.Color("OutlineVariant"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Padding = 0, Content = table,
            });
        }
        // JJQR - END

        if (d.Unpaid.Count + d.Charts.Count + d.FollowUps.Count > 0)
        {
            col.Children.Add(ClinicTheme.Text("Needs attention", "LabelSm"));
            AttentionList(col, d.Unpaid, "more unpaid visits");
            AttentionList(col, d.Charts, "more charts to finish");
            AttentionList(col, d.FollowUps, "more follow-ups due");
        }
        //JJQR - AI (21 Nov 2026) - no Refresh button on the web card (live hub + 60 s poll refresh it)
        //JJQR - END
        return Border(col);
    }

    void AttentionList(VerticalStackLayout col, IReadOnlyList<AttentionItem> items, string moreLabel)
    {
        //JJQR - AI (21 Nov 2026) - web: PRN follow-ups are saved with today's date but are not "due today"; an item without a booking id
        // (online follow-up feed) opens the appointments list instead of an empty consultation.
        items = items.Where(a => !(a.Kind == AttentionKind.FollowUpDue && (a.Text.Contains("PRN", StringComparison.OrdinalIgnoreCase) || a.Aside.Contains("PRN", StringComparison.OrdinalIgnoreCase)))).ToList();
        //JJQR - END
        foreach (var a in items.Take(5))
        {
            var id = a.BookingId;
            var mode = a.Kind switch { AttentionKind.FinishChart => "amend", AttentionKind.FollowUpDue => "view", _ => "view" };
            //JJQR - AI (21 Nov 2026) - web: unpaid → /doctor/appointments/{id} with "{name} — completed, unpaid" · "₱x due"; charts → consult ?mode=amend; follow-ups → ?mode=view
            var (text, aside) = a.Kind == AttentionKind.UnpaidCompleted ? UnpaidLine(a) : (a.Text, a.Aside);
            var row = StaffUi.Row(ClinicTheme.Text(text, "BodyMd"), ClinicTheme.Text(aside, "LabelSm"));
            var tap = new TapGestureRecognizer();
            var kind = a.Kind;
            tap.Tapped += async (_, _) =>
            {
                if (kind == AttentionKind.UnpaidCompleted || id == Guid.Empty) await OpenAppointmentAsync(id);
                else await GoAsync($"consult?bookingId={id}&mode={mode}");
            };
            //JJQR - END
            row.GestureRecognizers.Add(tap);
            col.Children.Add(row);
        }
        if (items.Count > 5)
            col.Children.Add(StaffUi.Link($"+{items.Count - 5} {moreLabel} — view all", () => GoAsync("//appointments")));
    }

    View AvailabilityCard()
    {
        var d = _data;
        var col = new VerticalStackLayout { Spacing = 8 };
        col.Children.Add(ClinicTheme.Text("Availability", "LabelLg"));
        col.Children.Add(ClinicTheme.Text(
            _loadError || d is null ? "Queue information is unavailable."
            : d.Waiting > 0 ? $"{d.Waiting} waiting" + (d.LongestWaitMinutes > 0 ? $" · longest wait {d.LongestWaitMinutes}m" : "")
            : "No one waiting right now.", "MutedSm"));
        if (d is null)
        {
            col.Children.Add(ClinicTheme.Text("Your status for today is unknown — it couldn't be loaded.", "ErrorText"));
            col.Children.Add(StaffUi.Btn("Retry", ReloadAsync, "SecondaryButton"));
            return Border(col);
        }
        var row = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (s, label) in new[] { (DoctorDayStatusEnum.Available, "Available"), (DoctorDayStatusEnum.RunningLate, "Running Late"), (DoctorDayStatusEnum.UnavailableToday, "Unavailable Today") })
        {
            var status = s;
            var b = StaffUi.Btn(_savingStatus == s ? $"{label}…" : label, () => SetStatusAsync(status), d.DayStatus == s ? "PrimaryButton" : "SecondaryButton");
            b.IsEnabled = _savingStatus is null;
            b.Margin = new Thickness(0, 0, 8, 8);
            row.Children.Add(b);
        }
        col.Children.Add(row);
        return Border(col);
    }

    async Task SetStatusAsync(DoctorDayStatusEnum status)
    {
        if (DoctorId is not { } id || _savingStatus is not null) return;
        _savingStatus = status; _dayStatusError = null; Render();
        var res = await _dash.SetDayStatusAsync(id, status);
        _savingStatus = null;
        if (!res.Succeeded) _dayStatusError = "Your availability could not be updated. Please try again.";
        await ReloadAsync();
    }

    View AnnouncementsCard(IReadOnlyList<AnnouncementItem> items)
    {
        var col = new VerticalStackLayout { Spacing = 8 };
        col.Children.Add(ClinicTheme.Text("Announcements", "LabelLg"));
        foreach (var a in items)
            col.Children.Add(StaffUi.Row(ClinicTheme.Text(a.Title, "LabelLg"), ClinicTheme.Text(a.Body, "MutedSm")));
        return Border(col);
    }

    View ReportsCard()
    {
        var col = new VerticalStackLayout { Spacing = 12 };
        col.Children.Add(StaffUi.Btn(_reportsOpen ? "▾  Clinic reports & recent bookings" : "▸  Clinic reports & recent bookings", () => { _reportsOpen = !_reportsOpen; Render(); return Task.CompletedTask; }, "GhostButton"));
        if (_reportsOpen)
        {
            var d = _data;
            if (d is null)
            {
                col.Children.Add(ClinicTheme.Text("Reports are unavailable.", "MutedBody"));
                col.Children.Add(StaffUi.Btn("Try again", ReloadAsync, "SecondaryButton"));
            }
            else
            {
                col.Children.Add(StaffUi.Wrap(
                    //JJQR - AI (21 Nov 2026) - web: a "Your performance" card (title + "Patients, collected and billed, trends") then the four StatCards
                    PerformanceCard(),
                    //JJQR - END
                    StaffUi.Stat("Pending Verifications (month)", d.PendingVerifications.ToString()),
                    StaffUi.Stat("On Hold (month)", d.OnHoldMonth.ToString()),
                    StaffUi.Stat("Unpaid Completed (month)", d.UnpaidCompletedMonth.ToString()),
                    StaffUi.Stat("Follow-Ups Due in 7 Days", d.FollowUpsDue7Days.ToString())));
                col.Children.Add(ClinicTheme.Text("Recent Bookings", "LabelLg"));
                if (d.Recent.Count == 0) col.Children.Add(ClinicTheme.Text("No bookings yet.", "MutedSm"));
                foreach (var r in d.Recent)
                {
                    var id = r.BookingId;
                    var line = new Grid
                    {
                        ColumnSpacing = 12,
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
                    };
                    line.Add(ClinicTheme.Text(r.PatientName, "BodyMd"), 0);
                    line.Add(ClinicTheme.Text(r.Slot, "MutedSm"), 1);
                    line.Add(ClinicTheme.Text($"Q# {r.QueueNumber ?? "—"}", "MutedSm"), 2);
                    line.Add(ClinicTheme.MakeStatusPill(r.Status.ToString()), 3);
                    var tap = new TapGestureRecognizer();
                    //JJQR - AI (21 Nov 2026) - web rowHref = /doctor/appointments/{id}
                    tap.Tapped += async (_, _) => await OpenAppointmentAsync(id);
                    //JJQR - END
                    line.GestureRecognizers.Add(tap);
                    // JJQR - AI (Nov 21, 2026)
                    col.Children.Add(new VerticalStackLayout { Spacing = 4, Children = { line, PrintButtons(id, r.HasPrescription, r.HasLabOrders) } });
                    // JJQR - END
                }
            }
        }
        return Border(col);
    }

    //JJQR - AI (21 Nov 2026)
    View PerformanceCard()
    {
        var card = new Border
        {
            Style = ClinicTheme.Style("Card"), Margin = new Thickness(0, 0, 12, 12),
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children = { ClinicTheme.Text("Your performance", "HeadlineSm"), ClinicTheme.Text("Patients, collected and billed, trends", "MutedSm") },
            },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await GoAsync("//performance");
        card.GestureRecognizers.Add(tap);
        return card;
    }
    //JJQR - END

    View AnalyticsCard()
    {
        var col = new VerticalStackLayout { Spacing = 12 };
        var today = ClinicClock.Today;
        var label = _range switch
        {
            Range.Month => today.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("en-PH")),
            Range.Year => today.Year.ToString(),
            //JJQR - AI (21 Nov 2026)
            _ => _customApplied ? $"{DateOnly.FromDateTime(_customFrom!.Value):yyyy-MM-dd} to {DateOnly.FromDateTime(_customTo!.Value):yyyy-MM-dd}" : "Custom range",
            //JJQR - END
        };
        col.Children.Add(ClinicTheme.Text($"Practice Analytics — {label}", "HeadlineSm"));
        var seg = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (r, text) in new[] { (Range.Month, "This Month"), (Range.Year, "This Year"), (Range.Custom, "Custom Range") })
        {
            var range = r;
            var b = StaffUi.Btn(text, async () => { _range = range; if (DoctorId is { } id) await LoadAnalyticsAsync(id); Render(); }, _range == r ? "PrimaryButton" : "SecondaryButton");
            b.Margin = new Thickness(0, 0, 8, 8);
            seg.Children.Add(b);
        }
        col.Children.Add(seg);

        //JJQR - AI (21 Nov 2026) - web DatePicker From / to / To (To cannot be before From); totals load once both are picked
        if (_range == Range.Custom)
        {
            var from = new DatePicker { Date = _customFrom };
            var to = new DatePicker { Date = _customTo };
            if (_customFrom is { } min) to.MinimumDate = min;
            async Task Picked()
            {
                if (_customFrom is { } f && _customTo is { } t && t < f) _customTo = f;
                if (DoctorId is { } id && _customApplied) { _loadingCustom = true; Render(); await LoadAnalyticsAsync(id); }
                Render();
            }
            from.DateSelected += async (_, e) => { _customFrom = e.NewDate; await Picked(); };
            to.DateSelected += async (_, e) => { _customTo = e.NewDate; await Picked(); };
            col.Children.Add(StaffUi.Wrap(Margin(ClinicPickers.DateBox(null, from)), Margin(ClinicTheme.Text("to", "MutedSm")), Margin(ClinicPickers.DateBox(null, to))));
        }
        //JJQR - END

        if (_analyticsError)
            col.Children.Add(ClinicTheme.Text("Practice totals could not be loaded. No estimated figures are shown.", "MutedBody"));
        else if (_range == Range.Custom && !_customApplied)
            col.Children.Add(ClinicTheme.Text("Pick a start and end date to see totals for that range.", "MutedBody"));
        //JJQR - AI (21 Nov 2026)
        else if (_range == Range.Custom && _loadingCustom)
            col.Children.Add(ClinicNode.App.Controls.Skeleton.Card(4)); //JJQR - AI (Nov 22, 2026) - skeleton //JJQR - END
        //JJQR - END
        else if (_analytics is { } a)
        {
            //JJQR - AI (21 Nov 2026) - web: three figures; the "vs last month" delta is green when ≥ 0, red when < 0 (month range only)
            var p = _range == Range.Month ? a.CollectedDeltaPercent : null;
            col.Children.Add(StaffUi.Wrap(
                Figure(a.Totals.CompletedVisits.ToString(), "Completed visits"),
                Figure(Peso(a.Totals.Collected), "Collected", p is { } v ? $"({(v >= 0 ? "+" : "")}{v}% vs last month)" : null, p >= 0 ? "StatusSuccess" : "StatusDanger"),
                Figure(Peso(a.Totals.Waived), "Waived")));
            //JJQR - END
            if (_range != Range.Custom && a.Trend.Count == 0)
                col.Children.Add(ClinicTheme.Text("No completed appointments yet.", "MutedBody"));
            if (_range != Range.Custom && a.Trend.Count > 1)
            {
                //JJQR - AI (21 Nov 2026) - web table: Month | Completed visits | Collected
                col.Children.Add(ClinicTheme.Text("MONTHLY TREND", "LabelMd"));
                col.Children.Add(new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
                    ColumnSpacing = 16,
                    Children = { ClinicTheme.Text("Month", "LabelMd"), Place(ClinicTheme.Text("Completed visits", "LabelMd"), 1), Place(ClinicTheme.Text("Collected", "LabelMd"), 2) },
                });
                //JJQR - END
                foreach (var t in a.Trend)
                    col.Children.Add(new Grid
                    {
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
                        ColumnSpacing = 16,
                        Children = { ClinicTheme.Text(t.Period, "BodyMd"), Place(ClinicTheme.Text(t.CompletedVisits.ToString(), "BodyMd"), 1) /* JJQR - AI (21 Nov 2026) - web shows the number only ... JJQR - END */, Place(ClinicTheme.Text(Peso(t.Collected), "BodyMd"), 2) },
                    });
            }
        }
        return Border(col);
    }

    static string Peso(decimal n) => "₱" + n.ToString("N0", CultureInfo.GetCultureInfo("en-PH"));

    static View Place(View v, int column) { Grid.SetColumn(v, column); return v; }

    static Border Border(View content) => new() { Style = ClinicTheme.Style("Card"), Content = content };
}
// JJQR - END
