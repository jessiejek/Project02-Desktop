//JJQR - AI (Nov 21, 2026) - /staff/bookings/[id]: "Booking for", Next step (Check in / Reschedule / Mark no-show / Undo check-in / Record payment / Waive professional fee / View receipt / Cancel booking), Patient, Doctor, Payment cards.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.Services;
using ClinicNode.Core.Print;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffBookingDetailPage : StaffLivePage
{
    private readonly Guid _id;
    private readonly ContentView _body = new();
    private readonly Label _pageError = StaffUi.Error();
    //JJQR - AI (21 Nov 2026) - STAFF PARITY (web /staff/bookings/[id]): ErrorState "This booking didn’t load" only while nothing is loaded yet; once the
    // booking is on screen a failed refresh keeps it silently (the web shows no warning there). Not found = Next's 404 text.
    protected override (string Title, string Message)? LoadErrorState => ("This booking didn’t load", "Check the connection to the clinic server, then try again.");
    protected override string? StaleLoadWarning => "";
    protected override void OnLoadStateChanged(bool failed) => _body.IsVisible = !failed;
    //JJQR - END

    public StaffBookingDetailPage(IServiceProvider sp, Guid bookingId) : base(sp, "Booking")
    {
        _id = bookingId;
        ClinicNode.App.Views.Shared.TopBarOnPushed.Attach(this, sp); //JJQR - AI (Nov 22, 2026) - top bar on pushed pages, set BEFORE the page is shown //JJQR - END
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            //JJQR - AI (21 Nov 2026) - web back link: "‹ Today’s queue" → /staff/queue
            Children = { StaffUi.Link("‹ Today’s queue", () => Shell.Current.GoToAsync("//staff-queue")), LoadError, ErrorStateView, _body }, //JJQR - AI (21 Nov 2026) - + ErrorStateView //JJQR - END
            //JJQR - END
        });
    }

    //JJQR - AI (21 Nov 2026) - live updates for THIS booking only (status / payment changed at another desk or by the doctor)
    protected override IReadOnlyList<string> HubEvents =>
        [ClinicNode.Api.Live.ClinicHubEvents.QueueUpdated, ClinicNode.Api.Live.ClinicHubEvents.PaymentUpdated, ClinicNode.Api.Live.ClinicHubEvents.PatientCheckedIn];
    protected override bool WantsHubEvent(ClinicNode.Api.Live.ClinicHubEvent e) => e.BookingId is null || e.BookingId == _id;
    //JJQR - END

    protected override async Task LoadAsync()
    {
        var b = await Svc<IStaffBookingService>().GetAsync(_id);
        if (b is null)
        {
            //JJQR - AI (21 Nov 2026) - STAFF PARITY: the web calls notFound() (Next's default 404 page) for a missing booking
            _body.Content = new VerticalStackLayout { Children = { ClinicTheme.Text("404", "HeadlineSm"), ClinicTheme.Text("This page could not be found.", "MutedBody") } };
            //JJQR - END
            return;
        }
        var a = b.Actions;
        var col = new VerticalStackLayout { Spacing = 16 };
        col.Children.Add(ClinicTheme.Text("Booking for", "MutedSm"));
        col.Children.Add(ClinicTheme.Text(b.PatientName.Length > 0 ? b.PatientName : "Patient", "HeadlineLg"));
        col.Children.Add(ClinicTheme.Text($"{StaffUi.Date(b.AppointmentDate)} · {StaffUi.Time(b.SlotStart)}{(b.QueueNumber is { } q ? " · " + q : "")}", "MutedBody"));
        //JJQR - AI (21 Nov 2026) - cancelled / no-show / expired visits owe nothing: payment shows "—", not "Unpaid"
        View payPill = StaffUi.IsVoidBooking(b.Status) && b.PaymentStatus == PaymentStatus.Unpaid
            ? ClinicTheme.Text("—", "MutedBody")
            : ClinicTheme.MakeStatusPill(b.PaymentStatus.ToString());
        col.Children.Add(new HorizontalStackLayout { Spacing = 6, Children = { ClinicTheme.MakeStatusPill(b.Status.ToString()), payPill } });
        //JJQR - END

        //JJQR - AI (21 Nov 2026) - a Pending booking shows the action row too (its only action is "Check in")
        if (a.HasAny || b.Status == BookingStatus.Pending)
        //JJQR - END
        {
            var buttons = new List<View>();
            //JJQR - AI (21 Nov 2026) - Jessie/allen0: "Check in" only while the booking is Pending
            if (b.Status == BookingStatus.Pending) buttons.Add(StaffUi.Btn("Check in", () => Do(() => Svc<IStaffBookingService>().CheckInAsync(_id))));
            //JJQR - END
            if (a.Reschedule) buttons.Add(StaffUi.Btn("Reschedule", () => RescheduleAsync(b), "SecondaryButton"));
            //JJQR - AI (21 Nov 2026) - Jessie/allen1: "Mark no-show" only while the booking is Pending (was: Confirmed only, from StaffRules)
            if (b.Status == BookingStatus.Pending) buttons.Add(StaffUi.Btn("Mark no-show", NoShowAsync, "SecondaryButton"));
            //JJQR - END
            if (a.UndoCheckIn) buttons.Add(StaffUi.Btn("Undo check-in", () => Do(() => Svc<IStaffBookingService>().UndoCheckInAsync(_id)), "SecondaryButton"));
            if (a.RecordPayment) buttons.Add(StaffUi.Btn("Record payment", async () =>
            {
                //JJQR - AI (21 Nov 2026) - online payment: write the payment through to SQLite so a receipt print right after works
                if (await RecordPaymentPage.ShowAsync(this, Svc<IStaffPaymentsService>(), _id, b.PatientName, b.PatientCode, b.AmountDue, receiptHint: true) is not null)
                    await ClinicNode.App.ViewModels.Staff.StaffLocalWriteThrough.EnsureBookingAsync(Sp, _id, withPayment: true);
                //JJQR - END
                await ReloadAsync();
            }));
            if (a.RecordPayment && a.Waive) buttons.Add(StaffUi.Btn("Waive professional fee", WaiveAsync, "SecondaryButton"));
            if (a.ViewReceipt) buttons.Add(StaffUi.Btn("View receipt", () => ReceiptPage.ShowAsync(this, Svc<IStaffPaymentsService>(), _id), "SecondaryButton"));
            //JJQR - AI (21 Nov 2026) - web: ghost button, red text
            if (a.Cancel) buttons.Add(StaffUi.DangerGhost("Cancel booking", CancelAsync));
            //JJQR - END
            if (b.QueueNumber is not null && b.AppointmentDate == ClinicClock.Today)
                //JJQR - AI (21 Nov 2026) - write-through first (online booking not pulled yet)
                buttons.Add(StaffUi.Btn("Print slip", async () =>
                {
                    await ClinicNode.App.ViewModels.Staff.StaffLocalWriteThrough.EnsureBookingAsync(Sp, _id, withPayment: false);
                    await PrintActions.RunAsync(this, () => Svc<IPrintDocumentFactory>().QueueSlipAsync(_id));
                }, "GhostButton"));
                //JJQR - END
            col.Children.Add(StaffUi.Card(ClinicTheme.Text("Next step", "HeadlineSm"), _pageError, StaffUi.Wrap(buttons.ToArray())));
        }

        //JJQR - AI (Nov 22, 2026) - Patient | Doctor | Payment side by side (three full-width cards stacked down the page left most of the screen empty)
        var patientCard = StaffUi.Card(
            ClinicTheme.Text("Patient", "HeadlineSm"),
            Info("Patient code", b.PatientCode),
            Info("Sex", b.PatientSex?.ToString()),
            Info("Date of birth", b.PatientDob is { } dob ? StaffUi.Date(dob) : null),
            Info("Contact", b.PatientContact),
            Info("Email", b.PatientEmail)); //JJQR - AI (21 Nov 2026) - STAFF PARITY: removed desktop-only "Open patient" link (the web Patient card has none) //JJQR - END
        var doctorCard = StaffUi.Card(
            ClinicTheme.Text("Doctor", "HeadlineSm"),
            Info("Name", b.DoctorName),
            Info("Specialty", b.DoctorSpecialization));
        var pay = new List<View>
        {
            ClinicTheme.Text("Payment", "HeadlineSm"),
            Info("Mode", b.PaymentMode == PaymentMode.PayAtClinic ? "Pay at clinic" : "Online"),
            Info("Amount due", StaffRules.Peso(b.AmountDue)),
        };
        if (a.Settled && b.OrNumber is not null) pay.Add(Info("OR number", b.OrNumber));
        if (b.PaymentStatus == PaymentStatus.Waived && !string.IsNullOrWhiteSpace(b.WaivedReason))
            pay.Add(ClinicTheme.Text($"Professional fee waived: {b.WaivedReason}", "BodySm"));
        var grid = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        grid.Add(patientCard, 0, 0); grid.Add(doctorCard, 1, 0); grid.Add(StaffUi.Card(pay.ToArray()), 2, 0);
        foreach (var v in grid.Children.OfType<View>()) v.VerticalOptions = LayoutOptions.Start;
        col.Children.Add(grid);
        //JJQR - END
        _body.Content = col;
    }

    static View Info(string label, string? value) =>
        new VerticalStackLayout { Spacing = 2, Children = { ClinicTheme.Text(label, "MutedSm"), ClinicTheme.Text(string.IsNullOrWhiteSpace(value) ? "—" : value, "BodyMd") } };

    async Task Do(Func<Task<StaffResult>> act)
    {
        StaffUi.Show(_pageError, null);
        var r = await act();
        if (!r.Succeeded) StaffUi.Show(_pageError, r.Error ?? "Could not update the booking.");
        await ReloadAsync();
    }

    async Task NoShowAsync()
    {
        await StaffDialog.ShowAsync(this, "Mark as no-show?", "The patient didn’t arrive for this appointment.", "Reason (optional)", null, "Back", "Mark no-show",
            async reason =>
            {
                var r = await Svc<IStaffBookingService>().MarkNoShowAsync(_id, reason);
                return r.Succeeded ? null : r.Error ?? "Could not update booking.";
            }, danger: true);
        await ReloadAsync();
    }

    async Task CancelAsync()
    {
        await StaffDialog.ShowAsync(this, "Cancel this booking?", "The booking will end as Cancelled. Your reason is kept on the booking.", "Reason", null, "Keep booking", "Cancel booking",
            async reason =>
            {
                var r = await Svc<IStaffBookingService>().CancelAsync(_id, reason);
                return r.Succeeded ? null : r.Error ?? "Could not cancel the booking.";
            }, danger: true, hint: "At least 5 characters.");
        await ReloadAsync();
    }

    async Task WaiveAsync()
    {
        //JJQR - AI (21 Nov 2026) - 409 or_number_duplicate on waive: close the reason dialog, then ask for the OR (prefilled with the clashing number,
        // trimmed, required, max 100) and retry ONLINE ONLY with the same reason. A session that expired mid-way shows "sign in again"; nothing saved locally.
        string? waivedReason = null;
        ClinicNode.Api.Services.PaymentOrClash? clash = null;
        await StaffDialog.ShowAsync(this, "Waive the professional fee", "Only record a waiver the doctor has already approved.", "Reason", null, "Back", "Record waiver",
            async reason =>
            {
                var r = await Svc<IStaffPaymentsService>().WaiveAsync(_id, reason);
                if (!r.Succeeded && ClinicNode.Api.Services.PaymentOutcome.TryGetOrClash(r, out var c)) { waivedReason = reason; clash = c; return null; }
                return r.Succeeded ? null : r.Error ?? "Could not waive the fee.";
            }, hint: "At least 5 characters.");
        if (clash is not null && IPlatformApplication.Current?.Services is { } services)
        {
            var api = ClinicNode.Api.Services.ApiStaffPaymentsService.Create(services);
            await StaffDialog.ShowAsync(this, "OR number already used", $"OR number {clash.OrNumber} is already used on another payment. Nothing was saved. Type the OR number for this waiver.",
                "OR number", null, "Back", "Record waiver",
                async typed =>
                {
                    var r = await api.WaiveWithOrAsync(_id, waivedReason, typed);
                    return r.Succeeded ? null : r.Error ?? "Could not waive the fee.";
                }, hint: "Required. Up to 100 characters.", multiline: false, initialText: clash.OrNumber, maxLength: ClinicNode.Api.Services.ApiStaffPaymentsService.OrNumberMaxLength);
        }
        //JJQR - END
        await ReloadAsync();
    }

    async Task RescheduleAsync(StaffBookingDetail b)
    {
        var date = new DatePicker { Date = ClinicClock.Today.AddDays(1).ToDateTime(TimeOnly.MinValue), MinimumDate = ClinicClock.Today.ToDateTime(TimeOnly.MinValue) };
        var reason = new Entry { Placeholder = "Reason (optional)" };
        var error = StaffUi.Error();
        var page = new ContentPage { Title = "Reschedule Booking", BackgroundColor = ClinicTheme.Color("Background") };
        var done = new TaskCompletionSource<Guid?>();
        var confirm = StaffUi.Btn("Confirm Reschedule", async () =>
        {
            StaffUi.Show(error, null);
            if (date.Date is not { } picked) { StaffUi.Show(error, "Choose a new appointment date."); return; }
            var r = await Svc<IStaffBookingService>().RescheduleAsync(_id, DateOnly.FromDateTime(picked), reason.Text);
            if (!r.Succeeded) { StaffUi.Show(error, r.Error ?? "Could not reschedule."); return; }
            done.TrySetResult(r.Id);
            await Navigation.PopModalAsync();
        });
        var back = StaffUi.Btn("Back", async () => { done.TrySetResult(null); await Navigation.PopModalAsync(); }, "SecondaryButton");
        page.BackgroundColor = Colors.Transparent;
        page.ControlTemplate = null;
        var rbody = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new Border { Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(14, 10), Content = ClinicTheme.Text($"{b.PatientName} · currently {StaffUi.Date(b.AppointmentDate)}", "LabelMd") },
                error,
                StaffUi.Field("New appointment date", ClinicPickers.DateBox(null, date, null, true)),
                StaffUi.Field("Reason (optional)", reason),
            }
        };
        var rfooter = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { back, confirm } };
        page.Content = ClinicNode.App.Controls.ClinicModal.Build("Reschedule booking", rbody, rfooter, async () => { done.TrySetResult(null); await Navigation.PopModalAsync(); }, 480);
        await Navigation.PushModalAsync(page);
        var newId = await done.Task;
        if (newId is { } id)
        {
            // Web: router.push(`/staff/bookings/${new_booking_id}`) — replace this page with the new booking.
            var next = new StaffBookingDetailPage(Sp, id);
            Navigation.InsertPageBefore(next, this);
            await Navigation.PopAsync();
        }
        else await ReloadAsync();
    }
}

/// <summary>Web "Receipt" modal: OR number, patient, doctor, visit date, amount, method, reference, issued.</summary>
public sealed class ReceiptPage : ContentPage
{
    public static async Task ShowAsync(Page from, IStaffPaymentsService svc, Guid bookingId)
    {
        var r = await svc.GetReceiptAsync(bookingId);
        var page = new ReceiptPage { Title = "Receipt", BackgroundColor = ClinicTheme.Color("Background") };
        var col = new VerticalStackLayout { Padding = 24, Spacing = 12, MaximumWidthRequest = 480 };
        col.Children.Add(ClinicTheme.Text("Receipt", "HeadlineMd"));
        if (r is null) col.Children.Add(ClinicTheme.Text("No receipt for this booking yet.", "MutedBody"));
        else
        {
            col.Children.Add(ClinicTheme.Text($"OR {r.OrNumber ?? "—"}", "HeadlineSm"));
            void Add(string l, string v) => col.Children.Add(new VerticalStackLayout { Children = { ClinicTheme.Text(l, "MutedSm"), ClinicTheme.Text(v, "BodyMd") } });
            Add("Patient", r.PatientName);
            Add("Doctor", r.DoctorName);
            Add("Visit date", StaffUi.Date(r.VisitDate));
            Add("Amount", StaffRules.Peso(r.Amount));
            Add("Method", r.Method is { } m ? StaffRules.PaymentMethodLabel(m) : (r.Status == PaymentStatus.Waived ? "Waived" : "—"));
            if (!string.IsNullOrWhiteSpace(r.ReferenceNumber)) Add("Reference", r.ReferenceNumber!);
            Add("Issued", StaffUi.DateTimeText(r.IssuedAt));
            if (r.PendingSync) col.Children.Add(ClinicTheme.Text("Saved on this computer — will sync to the clinic server when it’s reachable.", "MutedSm"));
        }
        col.Children.Add(StaffUi.Btn("Close", () => from.Navigation.PopModalAsync()));
        page.Content = new ScrollView { Content = col };
        await from.Navigation.PushModalAsync(page);
    }
}
//JJQR - END
