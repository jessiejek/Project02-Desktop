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

        _body.Children.Add(StaffUi.Card(ClinicTheme.Text("The clinic runs a walk-in FCFS queue — there are no appointment slots. These hours are informational (shown to patients); day-to-day availability is set from the dashboard status toggle.", "MutedBody")));

        _days.Clear();
        var hours = StaffUi.Column(8);
        hours.Children.Add(ClinicTheme.Text("Weekly Hours", "HeadlineSm"));
        foreach (var d in a.Days)
        {
            var active = new CheckBox { IsChecked = d.IsActive, VerticalOptions = LayoutOptions.Center };
            var start = new TimePicker { Time = d.StartTime.ToTimeSpan(), IsEnabled = d.IsActive };
            var end = new TimePicker { Time = d.EndTime.ToTimeSpan(), IsEnabled = d.IsActive };
            active.CheckedChanged += (_, e) => { start.IsEnabled = e.Value; end.IsEnabled = e.Value; };
            _days.Add((d.DayOfWeek, active, start, end));
            hours.Children.Add(new HorizontalStackLayout
            {
                Spacing = 12,
                Children = { active, new Label { Text = DoctorExtraRules.DayNames[d.DayOfWeek], WidthRequest = 110, VerticalOptions = LayoutOptions.Center, Style = ClinicTheme.Style("BodyMd") }, start, ClinicTheme.Text("–", "MutedSm") //JJQR - AI (21 Nov 2026) web separator is an en dash //JJQR - END
, end },
            });
        }
        _body.Children.Add(StaffUi.Card(hours));

        //JJQR - AI (21 Nov 2026) - web: "Daily patient limit" is its own card (heading + "Leave blank for no limit." + input). Read-only here: the API keeps it Admin-only.
        _limit.Text = a.DailyPatientLimit?.ToString() ?? "";
        _limit.IsReadOnly = true;
        _body.Children.Add(StaffUi.Card(
            ClinicTheme.Text("Daily patient limit", "HeadlineSm"),
            ClinicTheme.Text("Leave blank for no limit.", "MutedBody"),
            _limit));
        //JJQR - END

        var blocked = StaffUi.Column(8);
        blocked.Children.Add(ClinicTheme.Text("Blocked Dates", "HeadlineSm"));
        if (a.BlockedDates.Count == 0) blocked.Children.Add(ClinicTheme.Text("No blocked dates. Your weekly hours apply.", "MutedBody"));
        foreach (var b in a.BlockedDates)
            //JJQR - AI (21 Nov 2026) - web row: "{yyyy-MM-dd} — {reason}" + a delete action
            blocked.Children.Add(StaffUi.Row(
                ClinicTheme.Text($"{b.Date:yyyy-MM-dd} — {b.Reason}", "BodyMd"),
                StaffUi.DangerGhost("Remove", () => RemoveAsync(b))));
            //JJQR - END
        blocked.Children.Add(StaffUi.Btn("Block a date", () => Navigation.PushModalAsync(new BlockDateDialog(this)), "SecondaryButton"));
        _body.Children.Add(StaffUi.Card(blocked));
        //JJQR - AI (21 Nov 2026) - web: the Save button sits below the three cards
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
            BackgroundColor = ClinicTheme.Color("Background");
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
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Spacing = 12, Padding = 24, MaximumWidthRequest = 520,
                    Children =
                    {
                        ClinicTheme.Text("Add Blocked Date", "HeadlineMd"), error,
                        date, reason, affected,
                        StaffUi.Wrap(StaffUi.Btn("Cancel", () => Navigation.PopModalAsync(), "SecondaryButton"), block),
                    },
                },
            };
            //JJQR - END
        }
    }
}
//JJQR - END
