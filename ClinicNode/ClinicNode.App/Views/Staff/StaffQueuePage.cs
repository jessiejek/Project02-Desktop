//JJQR - AI (Nov 21, 2026) - /staff/queue "Today’s queue": S- lane first, six counts, check in / call in / hold / complete / no-show / cancel (reason), print slip, open booking.
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.App.Services;
using ClinicNode.Core.Print;
using ClinicNode.Core.Staff;
using ClinicNode.App.Views.Shared.Queue; //JJQR - AI (2026-11-25) - GAV-4 //JJQR - END

namespace ClinicNode.App.Views.Staff;

public sealed class StaffQueuePage : StaffLivePage
{
    private readonly FlexLayout _stats = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    private readonly VerticalStackLayout _list = new() { Spacing = 0 };
    //JJQR - AI (Nov 22, 2026) - queue as a table: active patients first, filter chips (Active / Completed / No-show / All)
    private readonly HorizontalStackLayout _chips = new() { Spacing = 8 };
    private IReadOnlyList<StaffQueueRow> _rows = [];
    private string _filter = "active";
    //JJQR - END
    private readonly Label _actionError = StaffUi.Error();
    //JJQR - AI (2026-11-25) - why: GAV-4 "Not finished on an earlier day": staff see the clinic's earlier-day patients not finished (same scope as this
    // page's today queue; owner default, can change). Continue opens the booking (staff do not consult). The host holds only fresh views.
    private readonly VerticalStackLayout _leftoverHost = new() { Spacing = 0 };
    private LeftoverQueueSection? _leftoverSection;
    LeftoverQueueSection Leftovers => _leftoverSection ??= new LeftoverQueueSection(this, Sp, () => null,
        r => StaffUi.PushAsync<StaffBookingDetailPage>(this, r.BookingId), RenderLeftovers, ReloadAsync);
    void RenderLeftovers()
    {
        try
        {
            _leftoverHost.Children.Clear();
            if (Leftovers.Build() is { } v) _leftoverHost.Children.Add(v);
        }
        catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("StaffQueue.Leftovers", ex); }
    }
    //JJQR - END
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
        _body.Children.Add(_chips);
        _body.Children.Add(LoadError);
        _body.Children.Add(_actionError);
        _body.Children.Add(_list);
        _body.Children.Add(_leftoverHost); //JJQR - AI (2026-11-25) - GAV-4: under today's queue //JJQR - END
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
        var leftoverTask = Leftovers.LoadAsync(); //JJQR - AI (2026-11-25) - GAV-4 (never throws) //JJQR - END
        var (s, rows) = await Svc<IStaffQueueBoardService>().GetAsync();
        await leftoverTask; //JJQR - AI (2026-11-25) - GAV-4 //JJQR - END
        RenderLeftovers(); //JJQR - AI (2026-11-25) - GAV-4 //JJQR - END
        _stats.Children.Clear();
        foreach (var (label, value) in new[] { ("Waiting", s.Waiting), ("With doctor", s.WithDoctor), ("Booked, not arrived", s.BookedNotArrived), ("Completed", s.Completed), ("No-show", s.NoShow), ("Total today", s.Total) })
            _stats.Children.Add(StaffUi.Stat(label, value.ToString()));
        _rows = rows;
        RenderList();
    }

    static bool IsDone(StaffQueueRow r) => r.Entry.Status is BookingStatus.Completed;
    static bool IsGone(StaffQueueRow r) => r.Entry.Status is BookingStatus.NoShow or BookingStatus.Cancelled or BookingStatus.Expired;

    void RenderList()
    {
        int Count(string f) => _rows.Count(r => Matches(r, f));
        _chips.Children.Clear();
        foreach (var (key, label) in new[] { ("active", "Active"), ("completed", "Completed"), ("gone", "No-show / cancelled"), ("all", "All") })
        {
            var k = key;
            _chips.Children.Add(StaffUi.Chip($"{label}  {Count(k)}", _filter == k, () => { _filter = k; RenderList(); }));
        }
        var shown = _rows.Where(r => Matches(r, _filter))
            //JJQR - AI (Nov 22, 2026) - the patient being seen first, then waiting (priority lane already first), then the rest
            .OrderBy(r => r.Entry.Status == BookingStatus.InProgress ? 0 : IsDone(r) || IsGone(r) ? 2 : 1)
            .ToList();
        _list.Children.Clear();
        var empty = _rows.Count == 0 ? "No one in the queue yet today." : _filter == "active" ? "Nobody is waiting right now." : "Nothing here.";
        _list.Children.Add(ClinicUi.Table(
            [
                new TableColumn("Queue", new GridLength(96), o => ClinicTheme.MakeLaneBadge(((StaffQueueRow)o).Entry.QueueNumber)),
                new TableColumn("Patient", GridLength.Star, o => { var r = (StaffQueueRow)o; var id = r.Entry.BookingId; return StaffUi.TwoLine(r.Entry.PatientName, r.Entry.PatientCode, () => StaffUi.PushAsync<StaffBookingDetailPage>(this, id)); }),
                new TableColumn("Visit", new GridLength(150), o => { var r = (StaffQueueRow)o; return ClinicTheme.Text($"{r.SourceLabel} · {r.VisitLabel}", "BodyMd"); }),
                new TableColumn("Status", new GridLength(130), o => ClinicTheme.MakeStatusPill(((StaffQueueRow)o).Entry.Status.ToString())),
                new TableColumn("Fee", new GridLength(90), o => ClinicTheme.Text(StaffRules.Peso(((StaffQueueRow)o).Entry.AmountDue), "BodyMd")),
                new TableColumn("", new GridLength(400), o => BuildActions((StaffQueueRow)o)),
            ], shown.Cast<object>(), empty));
    }

    static bool Matches(StaffQueueRow r, string f) => f switch
    {
        "active" => !IsDone(r) && !IsGone(r),
        "completed" => IsDone(r),
        "gone" => IsGone(r),
        _ => true,
    };

    View BuildActions(StaffQueueRow r)
    {
        var e = r.Entry;
        var a = r.Actions;
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
        return StaffUi.Actions(buttons.ToArray());
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
