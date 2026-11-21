//JJQR - AI (Nov 21, 2026) - /staff/payments "Collect payments": today first then earlier days; doctor-waived rows at ₱0 with "Confirm waiver"; exact-amount Record payment.
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.App.Services;
using ClinicNode.Core.Print;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffPaymentsPage : StaffLivePage
{
    private readonly FlexLayout _stats = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    private readonly VerticalStackLayout _list = new() { Spacing = 0 };
    //JJQR - AI (Nov 22, 2026) - payments as a table with a search box (29 identical cards were hard to scan)
    private readonly Entry _search = new() { Placeholder = "Search patient name or code" };
    private IReadOnlyList<CollectRow> _rows = [];
    //JJQR - END
    private readonly Label _success = StaffUi.Success();
    private readonly Label _waiverError = StaffUi.Error();
    //JJQR - AI (21 Nov 2026) - STAFF PARITY (web /staff/payments): <ErrorState title="The payment list didn’t load" …/> replaces the counts and list
    private readonly VerticalStackLayout _body = new() { Spacing = 16 };
    protected override (string Title, string Message)? LoadErrorState => ("The payment list didn’t load", "Check the connection to the clinic server, then try again.");
    protected override void OnLoadStateChanged(bool failed) => _body.IsVisible = !failed;
    //JJQR - END

    public StaffPaymentsPage(IServiceProvider sp) : base(sp, "Payments")
    {
        _search.TextChanged += (_, _) => RenderList();
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Header("Collect payments", "Patients whose visit is done and who still owe the fee, today first, then earlier days."),
                //JJQR - AI (21 Nov 2026) - STAFF PARITY
                LoadError, ErrorStateView, BodyOf(_body, _stats, StaffUi.Field("Search", _search), _success, _waiverError, _list),
                //JJQR - END
            }
        });
    }

    //JJQR - AI (21 Nov 2026) - STAFF PARITY helper
    static View BodyOf(VerticalStackLayout body, params View[] children)
    {
        foreach (var c in children) body.Children.Add(c);
        return body;
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - live updates (payments: paid / waived, and visits completed (now ready to pay))
    protected override IReadOnlyList<string> HubEvents => [ClinicNode.Api.Live.ClinicHubEvents.PaymentUpdated, ClinicNode.Api.Live.ClinicHubEvents.QueueUpdated];
    //JJQR - END

    protected override async Task LoadAsync()
    {
        var board = await Svc<IStaffPaymentsService>().GetCollectBoardAsync();
        _stats.Children.Clear();
        _stats.Children.Add(StaffUi.Stat("Waiting to pay", board.WaitingToPay.ToString()));
        _stats.Children.Add(StaffUi.Stat("Total due", StaffRules.Peso(board.TotalDue)));
        _stats.Children.Add(StaffUi.Stat("Waived — to confirm", board.WaivedToConfirm.ToString()));
        _rows = board.Rows;
        RenderList();
    }

    void RenderList()
    {
        var q = (_search.Text ?? "").Trim();
        var shown = _rows.Where(r => q.Length == 0 || r.PatientName.Contains(q, StringComparison.OrdinalIgnoreCase) || r.PatientCode.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        _list.Children.Clear();
        _list.Children.Add(ClinicUi.Table(
            [
                new TableColumn("Patient", GridLength.Star, o => { var r = (CollectRow)o; return StaffUi.TwoLine(r.PatientName, r.PatientCode); }),
                new TableColumn("Visit date", new GridLength(130), o => ClinicTheme.Text(StaffUi.Date(((CollectRow)o).AppointmentDate), "BodyMd")),
                new TableColumn("Queue", new GridLength(90), o => ClinicTheme.MakeLaneBadge(((CollectRow)o).QueueNumber)),
                new TableColumn("Amount", new GridLength(110), o => ClinicTheme.Text(StaffRules.Peso(((CollectRow)o).AmountDue), "LabelLg")),
                new TableColumn("Note", new GridLength(230), o => NoteCell((CollectRow)o)),
                new TableColumn("", new GridLength(200), o => ActionCell((CollectRow)o)),
            ], shown.Cast<object>(), _rows.Count == 0 ? "Everyone has paid. Nothing to collect right now." : "No patient matches this search."));
    }

    static View NoteCell(CollectRow r) => r.IsWaived
        ? new HorizontalStackLayout { Spacing = 6, Children = { ClinicTheme.MakeStatusPill("Waived"), ClinicTheme.Text(string.IsNullOrWhiteSpace(r.WaivedReason) ? "by doctor" : r.WaivedReason!, "MutedSm") } }
        : ClinicTheme.Text("Waiting to pay", "MutedSm");

    View ActionCell(CollectRow r) => r.IsWaived
        ? StaffUi.Btn("Confirm waiver", () => ConfirmWaiverAsync(r), "SecondaryButton")
        : StaffUi.Btn("Record payment", () => RecordAsync(r));

    async Task RecordAsync(CollectRow r)
    {
        StaffUi.Show(_success, null);
        var paid = await RecordPaymentPage.ShowAsync(this, Svc<IStaffPaymentsService>(), r.BookingId, r.PatientName, r.PatientCode, r.AmountDue);
        if (paid is { } amount)
        {
            StaffUi.Show(_success, $"{StaffRules.Peso(amount)} recorded for {r.PatientName}.");
            // JJQR - AI (Nov 21, 2026) - print the payment receipt / OR
            //JJQR - AI (21 Nov 2026) - online payment: copy the paid payment + booking into SQLite before the receipt reads them
            await ClinicNode.App.ViewModels.Staff.StaffLocalWriteThrough.EnsureBookingAsync(Sp, r.BookingId, withPayment: true);
            //JJQR - END
            await PrintActions.RunAsync(this, () => Svc<IPrintDocumentFactory>().PaymentReceiptAsync(r.BookingId));
            // JJQR - END
        }
        await ReloadAsync();
    }

    async Task ConfirmWaiverAsync(CollectRow r)
    {
        StaffUi.Show(_success, null);
        StaffUi.Show(_waiverError, null);
        var res = await Svc<IStaffPaymentsService>().ConfirmWaiverAsync(r.PaymentId);
        if (res.Succeeded) StaffUi.Show(_success, $"{r.PatientName}: waiver confirmed — ₱0 collected.");
        else StaffUi.Show(_waiverError, res.Error);
        await ReloadAsync();
    }
}
//JJQR - END
