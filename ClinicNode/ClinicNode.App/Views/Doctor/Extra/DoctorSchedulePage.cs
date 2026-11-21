//JJQR - AI (Nov 21, 2026) - D-SCHEDULE: clinic availability (web doctor/schedule/page.tsx): weekly hours, daily patient limit, blocked dates with the affected-bookings warning.
//JJQR - AI (21 Nov 2026) - saves online-first (IDoctorScheduleService routed by DoctorExtraModule: PUT /api/doctors/{id}/schedules per day, POST/DELETE blocked dates).
// The daily patient limit is Admin-only: shown read-only and passed back unchanged (the API save never sends it).
//JJQR - END
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Sync;

namespace ClinicNode.App.Views.Doctor.Extra;

public sealed class DoctorSchedulePage : StaffLivePage
{
    private readonly VerticalStackLayout _body = StaffUi.Column();
    private readonly Label _error = StaffUi.Error();
    private readonly Label _saved = StaffUi.Success();
    private readonly List<(short Day, CheckBox Active, TimePicker Start, TimePicker End)> _days = [];
    private readonly Entry _limit = new() { Placeholder = "No limit", Keyboard = Keyboard.Numeric };
    private Guid _doctorId;

    public DoctorSchedulePage(IServiceProvider sp) : base(sp, "Availability", live: false)
    {
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Header("Clinic availability", "Set your weekly hours, daily patient limit and dates away from the clinic."),
                LoadError, _error, _saved, _body,
            },
        });
    }

    protected override async Task LoadAsync()
    {
        _body.Children.Clear();
        var doctorId = DoctorExtraUi.DoctorId(Sp);
        if (doctorId is null) { _body.Children.Add(DoctorExtraUi.NotLinked()); return; }
        _doctorId = doctorId.Value;
        //JJQR - AI (21 Nov 2026) - web ErrorState: "Availability unavailable" / "Your schedule could not be loaded. Please try again." + retry
        DoctorAvailability? a;
        try { a = await Svc<IDoctorScheduleService>().GetAsync(_doctorId); } catch { a = null; }
        if (a is null)
        {
            _body.Children.Add(StaffUi.Card(
                ClinicTheme.Text("Availability unavailable", "HeadlineSm"),
                ClinicTheme.Text("Your schedule could not be loaded. Please try again.", "MutedBody"),
                StaffUi.Btn("Try again", () => ReloadAsync(), "SecondaryButton")));
            return;
        }
        //JJQR - END

        //JJQR - AI (Nov 21, 2026) - desktop layout: info bar on top; LEFT = weekly hours as a clear table with on/off toggles and time fields; RIGHT = daily limit + blocked dates; Save below
        _body.Children.Add(new Border
        {
            BackgroundColor = ClinicTheme.Color("StatusInfoSurface"), Stroke = ClinicTheme.Color("StatusInfoBorder"), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, Padding = new Thickness(16, 12),
            Content = new Label { Text = "The clinic runs a walk-in first-come queue, so there are no appointment slots. These hours are informational; day-to-day availability is set from the dashboard status toggle.", FontSize = 13, TextColor = ClinicTheme.Color("StatusInfo") },
        });

        _days.Clear();
        var hours = new VerticalStackLayout { Spacing = 0 };
        hours.Children.Add(new Label { Text = "Weekly hours", FontFamily = ClinicTheme.FontSemiBold, FontSize = 17, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var d in a.Days)
        {
            var active = new CheckBox { IsChecked = d.IsActive, IsVisible = false }; // value holder; the visible control is the toggle below
            var start = new TimePicker { Time = d.StartTime.ToTimeSpan(), IsEnabled = d.IsActive };
            var end = new TimePicker { Time = d.EndTime.ToTimeSpan(), IsEnabled = d.IsActive };
            var toggle = new Switch { IsToggled = d.IsActive, OnColor = ClinicTheme.Color("Primary"), VerticalOptions = LayoutOptions.Center };
            var closed = new Label { Text = "Closed", FontSize = 13, TextColor = ClinicTheme.Color("OnSurfaceVariant"), VerticalOptions = LayoutOptions.Center, IsVisible = !d.IsActive };
            var dayName = new Label { Text = DoctorExtraRules.DayNames[d.DayOfWeek], FontFamily = ClinicTheme.FontMedium, FontSize = 14, VerticalOptions = LayoutOptions.Center, TextColor = d.IsActive ? ClinicTheme.Color("OnSurface") : ClinicTheme.Color("OnSurfaceVariant") };
            toggle.Toggled += (_, e) =>
            {
                active.IsChecked = e.Value; start.IsEnabled = e.Value; end.IsEnabled = e.Value;
                closed.IsVisible = !e.Value;
                dayName.TextColor = e.Value ? ClinicTheme.Color("OnSurface") : ClinicTheme.Color("OnSurfaceVariant");
            };
            _days.Add((d.DayOfWeek, active, start, end));
            var row = new Grid
            {
                ColumnSpacing = 14, Padding = new Thickness(4, 10),
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(56)), new ColumnDefinition(new GridLength(120)),
                    new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star),
                },
            };
            row.Add(toggle, 0, 0);
            row.Add(dayName, 1, 0);
            row.Add(ClinicNode.App.Controls.ClinicPickers.TimeBox(this, start), 2, 0);
            row.Add(new Label { Text = "to", FontSize = 13, TextColor = ClinicTheme.Color("OnSurfaceVariant"), VerticalOptions = LayoutOptions.Center }, 3, 0);
            row.Add(ClinicNode.App.Controls.ClinicPickers.TimeBox(this, end), 4, 0);
            row.Add(closed, 5, 0);
            row.Add(active, 5, 0);
            hours.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.6f) });
            hours.Children.Add(row);
        }
        var hoursCard = StaffUi.Card(hours);

        // daily patient limit: shown as a plain value (it is changed by the clinic admin), not as a box that looks editable
        _limit.Text = a.DailyPatientLimit?.ToString() ?? "";
        _limit.IsReadOnly = true;
        var limitValue = new Label { Text = string.IsNullOrWhiteSpace(_limit.Text) ? "No limit" : $"{_limit.Text} patients per day", FontFamily = ClinicTheme.FontSemiBold, FontSize = 20 };
        var limitCard = StaffUi.Card(
            new Label { Text = "Daily patient limit", FontFamily = ClinicTheme.FontSemiBold, FontSize = 17 },
            limitValue,
            ClinicTheme.Text("Set by the clinic admin. Ask them to change it.", "MutedSm"));

        var blocked = StaffUi.Column(8);
        blocked.Children.Add(new Label { Text = "Blocked dates", FontFamily = ClinicTheme.FontSemiBold, FontSize = 17 });
        if (a.BlockedDates.Count == 0) blocked.Children.Add(ClinicTheme.Text("No blocked dates. Your weekly hours apply.", "MutedBody"));
        foreach (var b in a.BlockedDates)
            blocked.Children.Add(StaffUi.Row(
                ClinicTheme.Text($"{b.Date:yyyy-MM-dd} — {b.Reason}", "BodyMd"),
                StaffUi.DangerGhost("Remove", () => RemoveAsync(b))));
        blocked.Children.Add(StaffUi.Btn("Block a date", () => Navigation.PushModalAsync(new BlockDateDialog(this)), "SecondaryButton"));
        var blockedCard = StaffUi.Card(blocked);

        var right = new VerticalStackLayout { Spacing = 16, Children = { limitCard, blockedCard } };
        var cols = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(new GridLength(3, GridUnitType.Star)), new ColumnDefinition(new GridLength(2, GridUnitType.Star)) } };
        cols.Add(hoursCard, 0, 0);
        cols.Add(right, 1, 0);
        _body.Children.Add(cols);
        _body.Children.Add(StaffUi.Btn("Save availability", SaveAsync));
        //JJQR - END
    }

    private async Task SaveAsync()
    {
        StaffUi.Show(_error, null); StaffUi.Show(_saved, null);
        //JJQR - AI (21 Nov 2026) - web handleSave checks, in this order: end after start for each active day, then the daily-limit format
        foreach (var d in _days)
            if (d.Active.IsChecked && !((d.End.Time ?? TimeSpan.Zero) > (d.Start.Time ?? TimeSpan.Zero)))
            { StaffUi.Show(_error, $"{DoctorExtraRules.DayNames[d.Day]}: end time must be after start time."); return; }
        //JJQR - END
        var days = _days.Select(d => new ScheduleDay(d.Day, d.Active.IsChecked, TimeOnly.FromTimeSpan(d.Start.Time ?? TimeSpan.Zero), TimeOnly.FromTimeSpan(d.End.Time ?? TimeSpan.Zero))).ToList();
        var r = await Svc<IDoctorScheduleService>().SaveAsync(_doctorId, days, _limit.Text);
        if (!r.Succeeded) { StaffUi.Show(_error, r.Error); return; }
        StaffUi.Show(_saved, $"Schedule saved at {DoctorExtraUi.Pht(DateTimeOffset.Now)}.");
    }

    private async Task RemoveAsync(BlockedDateRow b)
    {
        StaffUi.Show(_error, null); StaffUi.Show(_saved, null);
        var r = await Svc<IDoctorScheduleService>().RemoveBlockedDateAsync(_doctorId, b.Id);
        if (!r.Succeeded) { StaffUi.Show(_error, r.Error); return; }
        await ReloadAsync();
    }

    /// <summary>"Add Blocked Date" modal: date, reason, live affected-bookings count.</summary>
    private sealed class BlockDateDialog : ContentPage
    {
        public BlockDateDialog(DoctorSchedulePage owner)
        {
            Title = "Add Blocked Date";
            BackgroundColor = Colors.Transparent;
            var svc = StaffUi.Get<IDoctorScheduleService>(owner.Sp);
            var error = StaffUi.Error();
            //JJQR - AI (21 Nov 2026) - web modal: no field labels, date starts unpicked, Reason placeholder "Reason", "Block this date" disabled until a date is picked and the booking check finished without error
            var date = new DatePicker { MinimumDate = ClinicApp.Domain.ClinicClock.Today.ToDateTime(TimeOnly.MinValue), Date = ClinicApp.Domain.ClinicClock.Today.ToDateTime(TimeOnly.MinValue) };
            var reason = new Entry { Placeholder = "Reason" };
            var affected = ClinicTheme.Text("Pick a date to see how many existing bookings would be affected.", "MutedSm");
            var picked = false; var loading = false; var failed = false;
            var block = StaffUi.Btn("Block this date", async () =>
            {
                if (!picked || loading || failed) return;
                var r = await svc.AddBlockedDateAsync(owner._doctorId, DateOnly.FromDateTime(date.Date ?? DateTime.Today), reason.Text);
                if (!r.Succeeded) { StaffUi.Show(error, "This date could not be blocked. Please try again."); return; }
                await Navigation.PopModalAsync();
                await owner.ReloadAsync();
            });
            void Sync() => block.IsEnabled = picked && !loading && !failed;
            async Task CheckAsync()
            {
                picked = true; loading = true; failed = false; Sync();
                affected.Text = "Checking bookings on this date…";
                try
                {
                    var n = await svc.CountAffectedBookingsAsync(owner._doctorId, DateOnly.FromDateTime(date.Date ?? DateTime.Today));
                    affected.Text = DoctorExtraRules.AffectedBookingsText(n);
                }
                catch { failed = true; affected.Text = "We could not check existing bookings. Choose the date again before blocking it."; }
                loading = false; Sync();
            }
            date.DateSelected += async (_, _) => await CheckAsync();
            Sync();
            ControlTemplate = null;
            var body = new VerticalStackLayout
            {
                Spacing = 14,
                Children =
                {
                    error,
                    StaffUi.Field("Date to block", ClinicPickers.DateBox(null, date, null, true)),
                    StaffUi.Field("Reason (optional)", reason),
                    new Border { Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(12, 9), Content = affected },
                },
            };
            var footer = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { StaffUi.Btn("Cancel", () => Navigation.PopModalAsync(), "SecondaryButton"), block } };
            Content = ClinicModal.Build("Block a date", body, footer, () => Navigation.PopModalAsync(), 480);
            //JJQR - END
        }
    }
}
//JJQR - END
