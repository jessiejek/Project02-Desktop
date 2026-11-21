//JJQR - AI (Nov 21, 2026) - read-only visit cards for the "Patient history" window inside a consultation: the same structure as the chart's Visits tab
// (header with tags, then notes / vitals / prescription / lab orders / follow-up / certificate / fee), built from the live chart so every earlier visit shows.
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra;

public static class ChartVisitCards
{
    /// <summary>The visit's diagnoses; the Diagnosis step is hidden in a consultation, so when none were entered the doctor's Assessment (first line, clipped) stands in for it.</summary>
    public static IReadOnlyList<string> DiagnosisOrAssessment(ChartConsultation? c)
    {
        if (c is null) return [];
        if (c.Diagnoses.Count > 0) return c.Diagnoses;
        var a = (c.Assessment ?? "").Trim();
        if (a.Length == 0) return [];
        var line = a.Split('\n')[0].Trim();
        return [line.Length > 90 ? line[..87].TrimEnd() + "…" : line];
    }

    static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    static View Block(string title, View body) =>
        new VerticalStackLayout { Spacing = 5, Children = { new Label { Text = title.ToUpperInvariant(), Style = ClinicTheme.Style("TableHeader") }, body } };

    static View None(string text) => new Label { Text = text, Style = ClinicTheme.Style("MutedSm") };

    static View Mini(View content) => new Border
    {
        BackgroundColor = Colors.White, Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1, Padding = new Thickness(10, 7),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Content = content,
    };

    public static List<View> Build(PatientChart c, Guid? excludeBookingId, int expandFirst = 1)
    {
        var views = new List<View>();
        var defaults = c.VitalTemplates.Where(t => t.IsDefault).ToList();
        var ordered = c.Bookings.Where(b => b.BookingId != excludeBookingId)
            .OrderByDescending(b => b.AppointmentDate).ThenByDescending(b => DoctorPatientChartPage.Seq(b.QueueNumber)).ToList();
        var n = 0;
        foreach (var b in ordered)
        {
            var consult = c.Consultations.FirstOrDefault(x => x.BookingId == b.BookingId);
            var ex = c.VisitExtras?.FirstOrDefault(e => e.BookingId == b.BookingId);
            var vit = c.VitalVisits.FirstOrDefault(v => v.BookingId == b.BookingId);
            var rx = c.Prescriptions.Where(g => g.BookingId == b.BookingId).ToList();

            var title = !string.IsNullOrWhiteSpace(consult?.ChiefComplaint) ? consult!.ChiefComplaint! : b.ServiceNames.Count == 0 ? "Consultation" : string.Join(", ", b.ServiceNames);
            var what = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { new Label { Text = title, Style = ClinicTheme.Style("LabelLg") } } };
            if (DiagnosisOrAssessment(consult) is { Count: > 0 } dxs) what.Children.Add(new Label { Text = string.Join(" · ", dxs), Style = ClinicTheme.Style("MutedSm") });
            var tags = new List<string>();
            if (rx.Count > 0) tags.Add($"Rx {rx.Sum(g => g.Lines.Count)}");
            if (ex is { LabOrders.Count: > 0 }) tags.Add($"Labs {ex.LabOrders.Count}");
            if (ex?.FollowUpDate is { } fd) tags.Add("Follow-up " + Iso(fd));
            if (ex is { HasCertificate: true }) tags.Add("Med cert issued");
            if (tags.Count > 0)
            {
                var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
                foreach (var t in tags)
                    chips.Children.Add(new Border { Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(7, 2), Margin = new Thickness(0, 2, 6, 0), Content = new Label { Text = t, Style = ClinicTheme.Style("PillLabelInfo") } });
                what.Children.Add(chips);
            }
            var toggle = new Label { FontSize = 13, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("Primary"), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center };
            var head = new Grid { ColumnSpacing = 14, ColumnDefinitions = { new ColumnDefinition(100), new ColumnDefinition(60), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(80) } };
            head.Add(new Label { Text = Iso(b.AppointmentDate), Style = ClinicTheme.Style("LabelMd"), VerticalOptions = LayoutOptions.Center }, 0, 0);
            head.Add(new Label { Text = b.QueueNumber ?? "—", Style = ClinicTheme.Style("MutedSm"), FontFamily = "Courier New", VerticalOptions = LayoutOptions.Center }, 1, 0);
            head.Add(what, 2, 0);
            head.Add(new HorizontalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { ClinicTheme.MakeStatusPill(b.Status.ToString()) } }, 3, 0);
            head.Add(toggle, 4, 0);

            // ----- details
            var left = new VerticalStackLayout { Spacing = 14 };
            var hasSoap = consult is not null && new[] { consult.Subjective, consult.Objective, consult.Assessment, consult.Plan }.Any(f => !string.IsNullOrWhiteSpace(f));
            if (hasSoap)
            {
                var soap = new VerticalStackLayout { Spacing = 6 };
                foreach (var (label, value) in new[] { ("Subjective", consult!.Subjective), ("Objective", consult.Objective), ("Assessment", consult.Assessment), ("Plan", consult.Plan) })
                    soap.Children.Add(new VerticalStackLayout { Spacing = 1, Children = { new Label { Text = label, Style = ClinicTheme.Style("LabelMd"), TextColor = ClinicTheme.Color("Primary") }, ClinicTheme.Text(string.IsNullOrWhiteSpace(value) ? "Not recorded." : value!, "BodyMd") } });
                left.Children.Add(Block("Notes", soap));
            }
            else left.Children.Add(Block("Notes", None("No notes recorded.")));
            if (vit is not null)
            {
                var tiles = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
                foreach (var t in defaults)
                {
                    if (!vit.Values.TryGetValue(t.TemplateId, out var val) || string.IsNullOrWhiteSpace(val)) continue;
                    var fs = new FormattedString();
                    fs.Spans.Add(new Span { Text = val, FontSize = 14, FontFamily = ClinicTheme.FontSemiBold });
                    if (!string.IsNullOrWhiteSpace(t.Unit)) fs.Spans.Add(new Span { Text = " " + t.Unit, FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
                    var tile = Mini(new VerticalStackLayout { Spacing = 1, Children = { new Label { Text = t.Description[..1] + t.Description[1..].ToLowerInvariant(), FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant"), LineBreakMode = LineBreakMode.NoWrap }, new Label { FormattedText = fs, LineBreakMode = LineBreakMode.NoWrap } } });
                    tile.Margin = new Thickness(0, 0, 8, 8);
                    FlexLayout.SetShrink(tile, 0);
                    tiles.Children.Add(tile);
                }
                left.Children.Add(Block("Vitals", tiles));
            }
            else left.Children.Add(Block("Vitals", None("No vitals recorded.")));

            var right = new VerticalStackLayout { Spacing = 14 };
            if (rx.Count > 0)
            {
                var list = new VerticalStackLayout { Spacing = 6 };
                foreach (var l in rx.SelectMany(g => g.Lines))
                    list.Children.Add(Mini(new VerticalStackLayout { Spacing = 1, Children =
                    {
                        new Label { Text = DoctorPatientChartPage.NiceMedicine(l.GenericName), FontSize = 13, FontFamily = ClinicTheme.FontSemiBold, LineBreakMode = LineBreakMode.WordWrap },
                        new Label { Text = string.Join(" · ", new[] { l.Dosage, l.Quantity }.Where(x => !string.IsNullOrWhiteSpace(x))), FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant") },
                        new Label { Text = l.Instruction ?? "", FontSize = 12, IsVisible = !string.IsNullOrWhiteSpace(l.Instruction), LineBreakMode = LineBreakMode.WordWrap },
                    } }));
                right.Children.Add(Block("Prescription", list));
            }
            else right.Children.Add(Block("Prescription", None("None prescribed.")));
            if (ex is { LabOrders.Count: > 0 })
            {
                var labs = new VerticalStackLayout { Spacing = 2 };
                foreach (var l in ex.LabOrders) labs.Children.Add(ClinicTheme.Text("• " + l, "BodyMd"));
                right.Children.Add(Block("Lab orders", labs));
            }
            else right.Children.Add(Block("Lab orders", None("None ordered.")));
            if (ex?.FollowUpDate is { } fdate)
            {
                var f = new VerticalStackLayout { Spacing = 1, Children = { ClinicTheme.Text(Iso(fdate), "LabelLg") } };
                if (!string.IsNullOrWhiteSpace(ex.FollowUpReason)) f.Children.Add(ClinicTheme.Text(ex.FollowUpReason!, "BodyMd"));
                right.Children.Add(Block("Follow-up", f));
            }
            else right.Children.Add(Block("Follow-up", None("No follow-up scheduled.")));
            if (ex is { HasCertificate: true }) right.Children.Add(Block("Medical certificate", ClinicTheme.Text("Issued" + (ex.CertificateIssued is { } ci ? " " + Iso(ci) : ""), "LabelLg")));
            else right.Children.Add(Block("Medical certificate", None(ex is { MedCertRequested: true } ? "Requested. Not issued." : "Not requested.")));
            if (ex is not null)
                right.Children.Add(Block("Visit & fee", ClinicTheme.Text($"{(ex.VisitType == "FollowUp" ? "Follow-up visit" : "New visit")}{(string.IsNullOrWhiteSpace(ex.Discount) ? "" : " · " + ex.Discount)}{(ex.Fee is { } fee ? " · " + ClinicNode.Core.FeeCalculator.FormatPeso(fee) : "")}", "BodyMd")));

            var body = new Grid { ColumnSpacing = 24, Margin = new Thickness(0, 4, 0, 0), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
            body.Add(left, 0, 0); body.Add(right, 1, 0);
            var details = new VerticalStackLayout { Spacing = 10, Children = { new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.5f) }, body } };
            var open = n < expandFirst;
            n++;
            void Apply() { details.IsVisible = open; toggle.Text = open ? "Hide ▴" : "Details ▾"; }
            Apply();
            var card = new Border { Style = ClinicTheme.Style("ListCard"), Content = new VerticalStackLayout { Spacing = 12, Children = { head, details } } };
            card.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { open = !open; Apply(); }) });
            views.Add(card);
        }
        return views;
    }
}
//JJQR - END
