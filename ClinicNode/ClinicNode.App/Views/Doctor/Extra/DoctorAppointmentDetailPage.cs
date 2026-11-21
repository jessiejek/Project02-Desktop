//JJQR - AI (Nov 21, 2026) - D-APPT-VIEW: appointment detail (web doctor/appointments/[id]/page.tsx): patient, payment, fee, consult summary, previous consultations, open/view consultation + print forms.
//JJQR - AI (21 Nov 2026) - 1:1 with the web: Open Consultation (mode=complete); View Consultation (secondary, mode=view) + Edit / Amend (primary, mode=amend);
// AppointmentPrintActions only when the booking has an Rx / lab orders ("Print Prescription" / "Print Lab Orders"; compact "Print Rx" / "Print Labs" on
// previous consultations, which open the consultation in view mode); no med-cert print (the web has none here);
// the Chief Complaint / Assessment / Plan card only for a completed visit. Data is online-first (DoctorExtraModule).
//JJQR - END
//JJQR - AI (21 Nov 2026) - layout pass against the web page: hero card (name + date · time, status pill on the right; Queue / Payment line;
// call / mail contact row under a divider; "Fee breakdown … ₱total total · ₱due due" box), actions in their own card (Active: full-width
// Open Consultation; Completed: View | Edit side by side, print actions under a divider), bold Chief Complaint / Assessment / Plan labels,
// "Previous consultations" as a section of bordered, clickable rows (date + doctor, summary or "—", ICD-10, Rx, Follow-up when a date or
// note exists) with compact print actions under a divider; "No previous consultations for this patient." empty state. "Print summary"
// removed: the web page has no such action. Slot time hidden when "00:00" like the web.
//JJQR - END
using System.Globalization;
using ClinicNode.App.Controls;
using ClinicNode.App.Services;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Print;
using ClinicNode.Core.Printing;

namespace ClinicNode.App.Views.Doctor.Extra;

public sealed class DoctorAppointmentDetailPage : StaffLivePage
{
    private readonly Guid _bookingId;
    private readonly VerticalStackLayout _body = StaffUi.Column();

    public DoctorAppointmentDetailPage(IServiceProvider sp, Guid bookingId) : base(sp, "Appointment")
    {
        _bookingId = bookingId;
        Content = StaffUi.Scroll(new VerticalStackLayout { Spacing = 16, Children = { LoadError, _body } });
    }

    //JJQR - AI (21 Nov 2026) - small layout helpers for the web markup
    private static Label Txt(string text, string style, string? color = null)
    {
        var l = ClinicTheme.Text(text, style);
        l.LineBreakMode = LineBreakMode.WordWrap;
        if (color is not null) l.TextColor = ClinicTheme.Color(color);
        return l;
    }

    private static BoxView Divider() => new() { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") };

    /// <summary>web: <c>₱{Number(fee)}</c> (no thousands separator, no forced decimals).</summary>
    private static string WebPeso(decimal v) => "₱" + v.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>web <c>&lt;strong&gt;Label:&lt;/strong&gt; value</c>.</summary>
    private static Label Labeled(string label, string? value) => new()
    {
        Style = ClinicTheme.Style("BodyMd"),
        TextColor = ClinicTheme.Color("OnSurfaceVariant"),
        LineBreakMode = LineBreakMode.WordWrap,
        FormattedText = new FormattedString
        {
            Spans =
            {
                new Span { Text = label + ": ", FontAttributes = FontAttributes.Bold, TextColor = ClinicTheme.Color("OnSurface") },
                new Span { Text = value ?? "" },
            },
        },
    };

    private static FlexLayout Chips(params View[] items)
    {
        var f = StaffUi.Wrap();
        foreach (var v in items) f.Children.Add(DoctorExtraUi.Spaced(v, 16, 4));
        return f;
    }
    //JJQR - END

    protected override async Task LoadAsync()
    {
        _body.Children.Clear();
        _body.Children.Add(StaffUi.Link("‹ Back to appointments", () => Navigation.PopAsync()));
        var doctorId = DoctorExtraUi.DoctorId(Sp);
        if (doctorId is null) { _body.Children.Add(DoctorExtraUi.NotLinked()); return; }
        var d = await Svc<IDoctorAppointmentsService>().GetAsync(doctorId.Value, _bookingId);
        if (d is null) { _body.Children.Add(StaffUi.Card(ClinicTheme.Text("Appointment not found.", "HeadlineSm"))); return; }
        //JJQR - AI (21 Nov 2026) - web loadBookingPrintAvailability for this visit + every previous one
        var printable = new Dictionary<Guid, BookingPrintAvailability>();
        foreach (var id in d.PreviousConsultations.Select(p => p.BookingId).Prepend(d.BookingId).Distinct())
            printable[id] = await Svc<IDoctorPrintDataService>().GetPrintAvailabilityAsync(id);
        //JJQR - END

        //JJQR - AI (21 Nov 2026) - hero card
        var slot = d.SlotStart.Length >= 5 ? d.SlotStart[..5] : d.SlotStart;
        var when = DoctorExtraRules.VisitDate(d.AppointmentDate) + (slot.Length > 0 && slot != "00:00" ? " · " + slot : "");
        var top = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 16 };
        top.Add(new VerticalStackLayout { Spacing = 4, Children = { Txt(d.PatientName, "HeadlineLg"), Txt(when, "BodyMd", "OnSurfaceVariant") } }, 0);
        var pill = ClinicTheme.MakeStatusPill(d.Status.ToString());
        pill.VerticalOptions = LayoutOptions.Start;
        top.Add(pill, 1);

        var meta = new List<View>();
        if (!string.IsNullOrWhiteSpace(d.QueueNumber)) meta.Add(Txt("Queue " + d.QueueNumber, "LabelMd", "OnSurfaceVariant"));
        meta.Add(Txt("Payment: " + d.PaymentLabel, "LabelMd", "OnSurfaceVariant"));

        var fee = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 16 };
        fee.Add(Txt("Fee breakdown", "BodyMd", "OnSurfaceVariant"), 0);
        fee.Add(Txt($"{WebPeso(d.TotalFee)} total · {WebPeso(d.AmountDue)} due", "LabelLg"), 1);

        _body.Children.Add(StaffUi.Card(
            top,
            Chips(meta.ToArray()),
            Divider(),
            Chips(Txt("☎  " + d.PatientContact, "LabelMd", "OnSurfaceVariant"), Txt("✉  " + d.PatientEmail, "LabelMd", "OnSurfaceVariant")),
            new Border
            {
                Padding = new Thickness(16, 8), StrokeThickness = 0, BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Content = fee,
            }));
        //JJQR - END

        //JJQR - AI (21 Nov 2026) - actions card
        var actions = StaffUi.Column(16);
        switch (d.State)
        {
            case AppointmentState.Active:
                var open = StaffUi.Btn("Open Consultation", () => DoctorExtraUi.OpenConsultAsync(d.BookingId, "complete"));
                open.HorizontalOptions = LayoutOptions.Fill;
                actions.Children.Add(open);
                break;
            case AppointmentState.Completed:
                var pair = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 16 };
                pair.Add(StaffUi.Btn("View Consultation", () => DoctorExtraUi.OpenConsultAsync(d.BookingId, "view"), "SecondaryButton"), 0);
                pair.Add(StaffUi.Btn("Edit / Amend", () => DoctorExtraUi.OpenConsultAsync(d.BookingId, "amend")), 1);
                actions.Children.Add(pair);
                if (PrintButtons(d.BookingId, printable[d.BookingId], compact: false) is { Count: > 0 } pr)
                {
                    actions.Children.Add(Divider());
                    actions.Children.Add(StaffUi.Wrap(pr.Select(b => DoctorExtraUi.Spaced(b)).ToArray()));
                }
                break;
            case AppointmentState.Closed:
                actions.Children.Add(Txt(d.Status == ClinicApp.Domain.Enums.BookingStatus.Rescheduled
                    ? "This appointment was rescheduled — open the new booking from your appointments list."
                    : "This appointment is closed (cancelled or no-show).", "MutedBody"));
                break;
            default:
                // web prints the raw status value
                actions.Children.Add(Txt($"This booking is still {d.Status} — open the consultation after staff confirms or checks the patient in.", "MutedBody"));
                break;
        }
        _body.Children.Add(StaffUi.Card(actions));
        //JJQR - END

        //JJQR - AI (21 Nov 2026) - web: the consultation card only for a completed visit; the "no record" line only for a closed one
        if (d.State == AppointmentState.Completed && d.HasConsultation)
            _body.Children.Add(StaffUi.Card(
                Labeled("Chief Complaint", d.ChiefComplaint),
                Labeled("Assessment", d.Assessment),
                Labeled("Plan", d.Plan)));
        if (d.State == AppointmentState.Closed)
            _body.Children.Add(Txt("No consultation record for this appointment.", "MutedBody"));
        //JJQR - END

        //JJQR - AI (21 Nov 2026) - previous consultations section (web: section, not a card; bordered clickable rows)
        var prev = StaffUi.Column(16);
        prev.Children.Add(ClinicTheme.Text("Previous consultations", "HeadlineSm"));
        if (d.PreviousConsultations.Count == 0)
            prev.Children.Add(new VerticalStackLayout
            {
                Spacing = 8, Padding = new Thickness(16, 24),
                Children =
                {
                    new Label { Text = "📋", FontSize = 26, HorizontalTextAlignment = TextAlignment.Center, TextColor = ClinicTheme.Color("OnSurfaceVariant") },
                    new Label { Text = "No previous consultations for this patient.", Style = ClinicTheme.Style("MutedBody"), HorizontalTextAlignment = TextAlignment.Center },
                },
            });
        var list = StaffUi.Column(8);
        foreach (var p in d.PreviousConsultations)
        {
            var prevBooking = p.BookingId;
            var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
            head.Add(Txt(p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "LabelMd"), 0);
            head.Add(Txt(string.IsNullOrWhiteSpace(p.DoctorName) ? "—" : p.DoctorName, "LabelSm", "OnSurfaceVariant"), 1);
            var link = new VerticalStackLayout { Spacing = 4, Children = { head, Txt(string.IsNullOrWhiteSpace(p.Summary) ? "—" : p.Summary, "BodyMd", "OnSurfaceVariant") } };
            if (p.Diagnoses.Count > 0) link.Children.Add(Txt("ICD-10: " + string.Join("; ", p.Diagnoses), "LabelSm", "OnSurfaceVariant"));
            link.Children.Add(Txt("Rx: " + p.RxSummary, "LabelSm", "OnSurfaceVariant"));
            if (p.FollowUpDate is not null || !string.IsNullOrWhiteSpace(p.FollowUpNote))
                link.Children.Add(Txt("Follow-up: " + (p.FollowUpDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—")
                    + (string.IsNullOrWhiteSpace(p.FollowUpNote) ? "" : " — " + p.FollowUpNote), "LabelSm", "OnSurfaceVariant"));
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) => await DoctorExtraUi.OpenConsultAsync(prevBooking, "view");
            link.GestureRecognizers.Add(tap);

            var item = new VerticalStackLayout { Spacing = 8, Children = { link } };
            if (printable.TryGetValue(prevBooking, out var pa) && PrintButtons(prevBooking, pa, compact: true) is { Count: > 0 } pb)
            {
                item.Children.Add(Divider());
                item.Children.Add(StaffUi.Wrap(pb.Select(b => DoctorExtraUi.Spaced(b)).ToArray()));
            }
            list.Children.Add(new Border
            {
                Padding = 16, Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Content = item,
            });
        }
        if (list.Children.Count > 0) prev.Children.Add(list);
        _body.Children.Add(prev);
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - web AppointmentPrintActions: nothing when the booking has neither an Rx nor lab orders
    private List<View> PrintButtons(Guid bookingId, BookingPrintAvailability a, bool compact)
    {
        var list = new List<View>();
        if (a.HasRx)
            list.Add(StaffUi.Btn(compact ? "Print Rx" : "Print Prescription",
                () => PrintActions.RunAsync(this, () => Svc<IPrintDocumentFactory>().PrescriptionAsync(bookingId)), "SecondaryButton"));
        if (a.HasLabs)
            list.Add(StaffUi.Btn(compact ? "Print Labs" : "Print Lab Orders",
                () => PrintActions.RunAsync(this, () => Svc<IPrintDocumentFactory>().LabRequestAsync(bookingId)), "SecondaryButton"));
        return list;
    }
    //JJQR - END
}
//JJQR - END
