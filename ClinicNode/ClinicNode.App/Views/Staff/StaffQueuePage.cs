//JJQR - AI (Nov 21, 2026) - /staff/queue "Today’s queue": S- lane first, six counts, check in / call in / hold / complete / no-show / cancel (reason), print slip, open booking.
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.Services;
using ClinicNode.Core.Print;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffQueuePage : StaffLivePage
{
    private readonly FlexLayout _stats = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    private readonly VerticalStackLayout _list = new() { Spacing = 0 };
    private readonly Label _actionError = StaffUi.Error();
    private readonly ReprintPanel _reprint; // JJQR - AI (Nov 21, 2026) - allen2 reprint flags badge + list
    //JJQR - AI (21 Nov 2026) - STAFF PARITY (web /staff/queue): ErrorState before the first load; warning + last queue kept afterwards
    private readonly VerticalStackLayout _body = new() { Spacing = 16 };
    protected override (string Title, string Message)? LoadErrorState => ("The queue didn’t load", "Check the connection to the clinic server, then try again.");
    protected override string? StaleLoadWarning => "Couldn’t refresh just now. Showing the last loaded queue.";
    protected override void OnLoadStateChanged(bool failed) => _body.IsVisible = !failed;
    //JJQR - END

    public StaffQueuePage(IServiceProvider sp) : base(sp, "Queue")
    {
        _reprint = new ReprintPanel(sp);
        var page = StaffUi.Column();
        page.Children.Add(StaffUi.Header("Today’s queue",
            "Senior citizens and PWD are served first, then first come, first served. Call the next patient, hold, or mark a no-show.",
            StaffUi.Btn("Check in walk-in", () => Shell.Current.GoToAsync("//staff-walk-in"))));
        //JJQR - AI (21 Nov 2026) - STAFF PARITY: web order: error state · counts · refresh warning · action error · list
        page.Children.Add(ErrorStateView);
        _body.Children.Add(_reprint);
        _body.Children.Add(_stats);
        _body.Children.Add(LoadError);
        _body.Children.Add(_actionError);
        _body.Children.Add(_list);
        page.Children.Add(_body);
        //JJQR - END
        Content = StaffUi.Scroll(page);
    }

    //JJQR - AI (21 Nov 2026) - live updates (queue board: status changes and new arrivals)
    protected override IReadOnlyList<string> HubEvents => [ClinicNode.Api.Live.ClinicHubEvents.QueueUpdated, ClinicNode.Api.Live.ClinicHubEvents.PatientCheckedIn];
    //JJQR - END

    protected override async Task LoadAsync()
    {
        await _reprint.RefreshAsync();
        var (s, rows) = await Svc<IStaffQueueBoardService>().GetAsync();
        _stats.Children.Clear();
        foreach (var (label, value) in new[] { ("Waiting", s.Waiting), ("With doctor", s.WithDoctor), ("Booked, not arrived", s.BookedNotArrived), ("Completed", s.Completed), ("No-show", s.NoShow), ("Total today", s.Total) })
            _stats.Children.Add(StaffUi.Stat(label, value.ToString()));
        _list.Children.Clear();
        if (rows.Count == 0)
        {
            _list.Children.Add(ClinicTheme.Text("No one in the queue yet today.", "MutedBody"));
            return;
        }
        foreach (var r in rows) _list.Children.Add(BuildRow(r));
    }

    View BuildRow(StaffQueueRow r)
    {
        var e = r.Entry;
        var a = r.Actions;
        var head = new HorizontalStackLayout { Spacing = 8, Children = { ClinicTheme.Text(e.QueueNumber ?? "—", "HeadlineSm") } };
        if (r.IsPriority) head.Children.Add(ClinicTheme.Text("Priority", "PageKicker"));
        head.Children.Add(ClinicTheme.MakeStatusPill(e.Status.ToString()));
        var name = StaffUi.Link(string.IsNullOrWhiteSpace(e.PatientName) ? "—" : e.PatientName, () => StaffUi.PushAsync<StaffBookingDetailPage>(this, e.BookingId));
        var meta = ClinicTheme.Text($"{e.PatientCode} · {r.SourceLabel} · {r.VisitLabel} · {StaffRules.Peso(e.AmountDue)}", "MutedSm");

        var buttons = new List<View>();
        var id = e.BookingId;
        //JJQR - AI (21 Nov 2026) - web renderActions order: [Check in, No-show] · Call in · [Open consult, Complete] · [Hold, No-show] · Cancel (No-show / Cancel in red)
        //JJQR - AI (21 Nov 2026) - Jessie/allen0: staff "Check in" only while the booking is Pending (the server's check-in accepts Pending only)
        var canCheckIn = a.CheckIn && e.Status == BookingStatus.Pending;
        //JJQR - END
        if (canCheckIn) buttons.Add(StaffUi.Btn("Check in", () => Act(id, "check-in")));
        //JJQR - AI (21 Nov 2026) - Jessie/allen1: "No-show" also only while the booking is Pending (next to Check in; never after arrival)
        if (e.Status == BookingStatus.Pending) buttons.Add(StaffUi.DangerGhost("No-show", () => Act(id, "no-show")));
        //JJQR - END
        if (a.CallIn) buttons.Add(StaffUi.Btn("Call in", () => Act(id, "call")));
        if (a.OpenConsult) buttons.Add(StaffUi.Btn("Open consult", () => Shell.Current.GoToAsync($"consult?bookingId={id}"), "SecondaryButton"));
        if (a.Complete) buttons.Add(StaffUi.Btn("Complete", () => Act(id, "complete"), "SecondaryButton"));
        if (a.Hold) buttons.Add(StaffUi.Btn("Hold", () => Act(id, "hold"), "GhostButton"));
        //JJQR - AI (21 Nov 2026) - removed: the second No-show (after Hold) for CheckedIn / OnHold / InProgress / Confirmed - Pending only now (above)
        //JJQR - END
        if (a.Cancel) buttons.Add(StaffUi.DangerGhost("Cancel", () => CancelAsync(r)));
        //JJQR - END
        if (!string.IsNullOrWhiteSpace(e.QueueNumber))
            //JJQR - AI (21 Nov 2026) - write-through first (online check-in not pulled yet)
            buttons.Add(StaffUi.Btn("Print slip", async () =>
            {
                await ClinicNode.App.ViewModels.Staff.StaffLocalWriteThrough.EnsureBookingAsync(Sp, id, withPayment: false);
                await PrintActions.RunAsync(this, () => Svc<IPrintDocumentFactory>().QueueSlipAsync(id));
            }, "SecondaryButton"));
            //JJQR - END
        foreach (var b in buttons) b.Margin = new Thickness(0, 0, 8, 0);
        return StaffUi.Row(head, name, meta, StaffUi.Wrap(buttons.ToArray()));
    }

    async Task Act(Guid id, string action)
    {
        StaffUi.Show(_actionError, null);
        var r = await Svc<IStaffQueueBoardService>().ActAsync(id, action);
        if (!r.Succeeded) StaffUi.Show(_actionError, r.Error);
        await ReloadAsync();
    }

    async Task CancelAsync(StaffQueueRow r)
    {
        var e = r.Entry;
        var done = await StaffDialog.ShowAsync(this, $"Cancel queue {e.QueueNumber}?",
            $"Cancel {e.QueueNumber} for {e.PatientName}? They will be removed from today's queue.",
            "Reason", "e.g. Patient left, changed their mind, duplicate entry", "Keep booking", "Cancel booking",
            async reason => (await Svc<IStaffQueueBoardService>().CancelAsync(e.BookingId, reason)).Error, danger: true);
        if (done) await ReloadAsync();
    }
}
//JJQR - END
