//JJQR - AI (Nov 21, 2026) - /staff/payments "Collect payments": today first then earlier days; doctor-waived rows at ₱0 with "Confirm waiver"; exact-amount Record payment.
using ClinicNode.App.Controls;
using ClinicNode.App.Services;
using ClinicNode.Core.Print;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffPaymentsPage : StaffLivePage
{
    private readonly FlexLayout _stats = new() { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
    private readonly VerticalStackLayout _list = new() { Spacing = 0 };
    private readonly Label _success = StaffUi.Success();
    private readonly Label _waiverError = StaffUi.Error();
    //JJQR - AI (21 Nov 2026) - STAFF PARITY (web /staff/payments): <ErrorState title="The payment list didn’t load" …/> replaces the counts and list
    private readonly VerticalStackLayout _body = new() { Spacing = 16 };
    protected override (string Title, string Message)? LoadErrorState => ("The payment list didn’t load", "Check the connection to the clinic server, then try again.");
    protected override void OnLoadStateChanged(bool failed) => _body.IsVisible = !failed;
    //JJQR - END

    public StaffPaymentsPage(IServiceProvider sp) : base(sp, "Payments")
    {
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Header("Collect payments", "Patients whose visit is done and who still owe the fee, today first, then earlier days."),
                //JJQR - AI (21 Nov 2026) - STAFF PARITY
                LoadError, ErrorStateView, BodyOf(_body, _stats, _success, _waiverError, _list),
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
        _list.Children.Clear();
        if (board.Rows.Count == 0)
        {
            _list.Children.Add(ClinicTheme.Text("Everyone has paid. Nothing to collect right now.", "MutedBody"));
            return;
        }
        foreach (var r in board.Rows) _list.Children.Add(BuildRow(r));
    }

    View BuildRow(CollectRow r)
    {
        var views = new List<View>
        {
            ClinicTheme.Text(r.PatientName, "HeadlineSm"),
            ClinicTheme.Text($"{(r.PatientCode.Length > 0 ? r.PatientCode + " · " : "")}{StaffUi.Date(r.AppointmentDate)} · Queue {r.QueueNumber ?? "—"}", "MutedSm"),
            ClinicTheme.Text(StaffRules.Peso(r.AmountDue), "HeadlineSm"),
        };
        if (r.IsWaived)
        {
            views.Add(new HorizontalStackLayout { Spacing = 6, Children = { ClinicTheme.MakeStatusPill("Waived"), ClinicTheme.Text("by doctor" + (string.IsNullOrWhiteSpace(r.WaivedReason) ? "" : $" — {r.WaivedReason}"), "MutedSm") } });
            views.Add(StaffUi.Btn("Confirm waiver", () => ConfirmWaiverAsync(r), "SecondaryButton"));
        }
        else
        {
            views.Add(StaffUi.Btn("Record payment", () => RecordAsync(r), "SecondaryButton"));
        }
        return StaffUi.Row(views.ToArray());
    }

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
