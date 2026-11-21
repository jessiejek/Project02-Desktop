//JJQR - AI (Nov 21, 2026) - /staff/dashboard "Front desk today": 3 glance cards, "N patients are ready to pay" + Go to payments, shortcuts, Arrivals (Check in / Undo check-in / Cancel with reason).
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffHomePage : StaffLivePage
{
    private readonly FlexLayout _stats = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    private readonly ContentView _banner = new();
    private readonly VerticalStackLayout _arrivals = new() { Spacing = 0 };
    private readonly Label _actionError = StaffUi.Error();
    private readonly ReprintPanel _reprint; // JJQR - AI (Nov 21, 2026) - allen2 reprint flags badge + list
    //JJQR - AI (21 Nov 2026) - web <AnnouncementsPanel/> under the header: latest 3 active announcements, newest first; hidden when none or on error.
    private readonly VerticalStackLayout _announcements = new() { Spacing = 8 };
    //JJQR - END
    //JJQR - AI (21 Nov 2026) - STAFF PARITY: web shows <ErrorState title="Today’s list didn’t load" …/> INSTEAD of the stats, banner, shortcuts and
    // Arrivals when the first load fails (header + announcements stay). _body holds what the error state replaces.
    private readonly VerticalStackLayout _body = new() { Spacing = 16 };
    protected override (string Title, string Message)? LoadErrorState => ("Today’s list didn’t load", "Check the connection to the clinic server, then try again.");
    protected override void OnLoadStateChanged(bool failed) => _body.IsVisible = !failed;
    //JJQR - END

    public StaffHomePage(IServiceProvider sp) : base(sp, "Today")
    {
        _reprint = new ReprintPanel(sp);
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Header("Front desk today", $"{DateTime.Today:dddd, MMMM d}  ·  Who is booked, who has arrived, and who is waiting to pay.",
                    StaffUi.Btn("New walk-in", () => Shell.Current.GoToAsync("//staff-walk-in"))),
                LoadError,
                //JJQR - AI (21 Nov 2026) - STAFF PARITY: error state card replaces the body below on a failed load
                ErrorStateView,
                BuildBody(),
                //JJQR - END
            }
        });
    }

    //JJQR - AI (21 Nov 2026) - STAFF PARITY: the part of the dashboard that the web's ErrorState replaces
    View BuildBody()
    {
        _body.Children.Add(_reprint);
        _body.Children.Add(_stats);
        _body.Children.Add(StaffUi.Card(
            StaffUi.Wrap(ClinicTheme.Text("Arrivals", "HeadlineSm"), ClinicTheme.Text("   ", "BodyMd"), StaffUi.Link("Open queue board", () => Shell.Current.GoToAsync("//staff-queue"))),
            _actionError,
            _arrivals));
        //JJQR - AI (Nov 22, 2026) - announcements now sit under the work, not above it
        _body.Children.Add(_announcements);
        //JJQR - END
        return _body;
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - live updates (dashboard: arrivals, queue counts, "ready to pay")
    protected override IReadOnlyList<string> HubEvents => [ClinicNode.Api.Live.ClinicHubEvents.PatientCheckedIn, ClinicNode.Api.Live.ClinicHubEvents.QueueUpdated, ClinicNode.Api.Live.ClinicHubEvents.PaymentUpdated];
    //JJQR - END

    protected override async Task LoadAsync()
    {
        await _reprint.RefreshAsync();
        //JJQR - AI (21 Nov 2026) - announcements never block the dashboard
        await LoadAnnouncementsAsync();
        //JJQR - END
        var d = await Svc<IStaffDashboardService>().GetAsync();
        _stats.Children.Clear();
        _stats.Children.Add(StaffUi.Stat("Booked or checked in", d.BookedOrCheckedIn.ToString(), () => Shell.Current.GoToAsync("//staff-queue")));
        _stats.Children.Add(StaffUi.Stat("Ready for payment", d.ReadyForPayment.ToString(), () => Shell.Current.GoToAsync("//staff-payments")));
        _stats.Children.Add(StaffUi.Stat("Patients on today’s list", d.PatientsToday.ToString(), () => Shell.Current.GoToAsync("//staff-queue")));

        _banner.Content = d.ReadyForPayment > 0
            ? StaffUi.Card(
                ClinicTheme.Text($"{d.ReadyForPayment} {(d.ReadyForPayment == 1 ? "patient is" : "patients are")} ready to pay.", "BodyMd"),
                StaffUi.Btn("Go to payments", () => Shell.Current.GoToAsync("//staff-payments"), "SecondaryButton"))
            : null;

        //JJQR - AI (Nov 22, 2026) - arrivals as a table; the duplicate "ready to pay" card and the three button-tabs are gone (the tiles above already link there)
        _arrivals.Children.Clear();
        _arrivals.Children.Add(ClinicUi.Table(
            [
                new TableColumn("Time", new GridLength(100), o => ClinicTheme.Text(StaffUi.Time(((ArrivalRow)o).SlotStart), "BodyMd")),
                new TableColumn("Patient", GridLength.Star, o => StaffUi.TwoLine(((ArrivalRow)o).PatientName, null)),
                new TableColumn("Queue", new GridLength(96), o => ClinicTheme.MakeLaneBadge(((ArrivalRow)o).QueueNumber)),
                new TableColumn("Status", new GridLength(130), o => ClinicTheme.MakeStatusPill(((ArrivalRow)o).Status.ToString())),
                new TableColumn("", new GridLength(240), o => ArrivalActions((ArrivalRow)o)),
            ], d.Arrivals.Cast<object>(), "No one is booked or waiting right now."));
    }

    View ArrivalActions(ArrivalRow a)
    {
        var row = a;
        var buttons = new List<View>();
        if (a.CanCancel) buttons.Add(StaffUi.DangerGhost("Cancel", () => CancelAsync(row)));
        //JJQR - AI (21 Nov 2026) - Jessie/allen0: "Check in" only while the booking is Pending; "Undo check-in" stays for checked-in rows; no button otherwise
        if (a.Status == ClinicApp.Domain.Enums.BookingStatus.Pending)
            buttons.Add(StaffUi.Btn("Check in", () => ToggleAsync(row), "SecondaryButton"));
        else if (a.Status == ClinicApp.Domain.Enums.BookingStatus.CheckedIn)
            buttons.Add(StaffUi.Btn("Undo check-in", () => ToggleAsync(row), "SecondaryButton"));
        //JJQR - END
        return StaffUi.Actions(buttons.ToArray());
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - AnnouncementsPanel: title, body, date (newest first, max 3).
    async Task LoadAnnouncementsAsync()
    {
        _announcements.Children.Clear();
        try
        {
            foreach (var a in (await Svc<IStaffAnnouncementsService>().ListActiveAsync()).Take(3))
                _announcements.Children.Add(StaffUi.Card(
                    ClinicTheme.Text(a.Title, "HeadlineSm"),
                    ClinicTheme.Text(a.Body, "BodyMd"),
                    ClinicTheme.Text(StaffUi.Date(DateOnly.FromDateTime(a.PostedDate.ToOffset(TimeSpan.FromHours(8)).DateTime)), "MutedSm")));
        }
        catch { /* web: renders nothing when the call fails */ }
        _announcements.IsVisible = _announcements.Children.Count > 0;
    }
    //JJQR - END

    async Task ToggleAsync(ArrivalRow a)
    {
        StaffUi.Show(_actionError, null);
        var r = await Svc<IStaffDashboardService>().ToggleCheckInAsync(a.BookingId);
        if (!r.Succeeded) StaffUi.Show(_actionError, "Could not update the check-in. Please try again.");
        await ReloadAsync();
    }

    async Task CancelAsync(ArrivalRow a)
    {
        var who = a.PatientName.Length > 0 ? a.PatientName : "this patient";
        var queue = a.QueueNumber is { } q ? $" (queue {q})" : "";
        await StaffDialog.ShowAsync(this, "Cancel this booking?", $"Cancel the booking for {who}{queue}? They will be removed from today's list.",
            "Reason", "e.g. Patient did not come, changed their mind, duplicate entry", "Keep booking", "Cancel booking",
            async reason =>
            {
                var r = await Svc<IStaffDashboardService>().CancelAsync(a.BookingId, reason);
                return r.Succeeded ? null : r.Error ?? "Could not cancel the booking. Please try again.";
            }, danger: true);
        await ReloadAsync();
    }
}
//JJQR - END
