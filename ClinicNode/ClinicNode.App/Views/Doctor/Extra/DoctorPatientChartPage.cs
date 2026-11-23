//JJQR - AI (Nov 21, 2026) - D-CHART: patient chart (web doctor/patients/[id]/page.tsx): header + tabs Timeline / Appointments / Consultations / Vitals / Prescriptions / Lab Results / Documents / Vaccinations. Read-only; prescriptions are edited from the consultation.
//JJQR - AI (21 Nov 2026) - prescriptions EDITABLE like the web: when the chart is opened for a booking (web ?bookingId=, here ReferredBookingId via OpenAsync)
// the Prescriptions tab shows "New Prescription" and per-group Edit / Copy / Delete ("Delete Prescription?"); every group has Print (the web "Print Preview"
// modal, then Print). "Print patient card" in the header prints GET /api/patients/{id}/card (no clinical notes). Data is online-first.
//JJQR - END
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.App.Controls;
using ClinicNode.App.Services;
using ClinicNode.App.Views.Staff;
using ClinicNode.Api.Http; //JJQR - AI (Nov 23, 2026) - file open errors //JJQR - END
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Print;
using ClinicNode.Core.Printing;

namespace ClinicNode.App.Views.Doctor.Extra;

public sealed class DoctorPatientChartPage : StaffLivePage
{
    private readonly Guid _patientId;
    private readonly VerticalStackLayout _header = StaffUi.Column(8);
    private readonly VerticalStackLayout _tab = StaffUi.Column(8);
    private PatientChart? _chart;
    private string _selected = "timeline";
    private bool _newestFirst = true; // legacy flag, replaced by the two web sort selects below
    //JJQR - AI (21 Nov 2026) - web: sort selects exist only on Vitals (vitalsSort) and Prescriptions (rxSort); Vitals has a list/table toggle; Consultations expand one at a time
    private bool _vitalsNewest = true, _rxNewest = true, _vitalsTable;
    private Guid? _expandedConsult;
    private bool _loadFailed;
    //JJQR - END
    //JJQR - AI (21 Nov 2026)
    private readonly Label _tabError = StaffUi.Error();
    private VerticalStackLayout? _tabs;

    /// <summary>Web ?bookingId=: the visit the doctor is working on. Enables New / Edit / Copy / Delete on the Prescriptions tab.</summary>
    public Guid? ReferredBookingId { get; init; }

    /// <summary>Open the chart, optionally for a booking and on a tab (web /doctor/patients/{id}?bookingId=&amp;tab=). Usable from any doctor screen.</summary>
    public static Task OpenAsync(Page from, Guid patientId, Guid? bookingId = null, string? tab = null)
    {
        var sp = from.Handler?.MauiContext?.Services ?? StaffUi.Services;
        var page = new DoctorPatientChartPage(sp, patientId) { ReferredBookingId = bookingId };
        if (tab is "appointments" or "consultations" or "vitals" or "prescriptions") tab = "timeline"; //JJQR - AI (Nov 21, 2026) - Appointments and Timeline were the same list: one "Visits" tab now //JJQR - END
        if (tab is not null && ChartTabs.All.Any(t => t.Id == tab)) page.Select(tab);
        return from.Navigation.PushAsync(page);
    }
    //JJQR - END

    public DoctorPatientChartPage(IServiceProvider sp, Guid patientId) : base(sp, "Patient chart", live: false)
    {
        _patientId = patientId;
        ClinicNode.App.Views.Shared.TopBarOnPushed.Attach(this, sp); //JJQR - AI (Nov 22, 2026) - top bar on pushed pages, set BEFORE the page is shown //JJQR - END
        //JJQR - AI (21 Nov 2026) - tab strip kept so Select() can re-render it; tab-level error line for prescription actions
        _tabs = DoctorExtraUi.Tabs(LabelledTabs(), () => _selected, id => { _selected = id; RenderTab(); });
        //JJQR - AI (Nov 21, 2026) - clinical record layout: patient summary rail on the left (identity, latest vitals, diagnoses, medicines), tabs + content on the right
        var main = new VerticalStackLayout { Spacing = 12, Children = { _tabs, _tabError, _tab } };
        var split = new Grid { ColumnSpacing = 20, ColumnDefinitions = { new ColumnDefinition(300), new ColumnDefinition(GridLength.Star) } };
        split.Add(_header, 0, 0);
        split.Add(main, 1, 0);
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children = { StaffUi.Link("← Back to Patients", () => Navigation.PopAsync()), LoadError, split },
        });
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026)
    private void Select(string tab)
    {
        _selected = tab;
        if (_tabs is null) return;
        RefreshTabs();
    }

    //JJQR - AI (Nov 21, 2026) - tab labels carry the record counts ("Prescriptions 3") so the doctor sees what is on file before opening a tab
    private (string Id, string Label)[] LabelledTabs()
    {
        int N(string id) => _chart is not { } c ? 0 : id switch
        {
            "timeline" => c.Bookings.Count, "labs" => c.LabResults.Count, "documents" => c.Documents.Count, "vaccinations" => c.Vaccinations.Count, _ => 0,
        };
        return ChartTabs.All.Select(t => (t.Id, N(t.Id) > 0 ? $"{t.Label}  {N(t.Id)}" : t.Label)).ToArray();
    }

    private void RefreshTabs()
    {
        var fresh = DoctorExtraUi.Tabs(LabelledTabs(), () => _selected, id => { _selected = id; RenderTab(); });
        _tabs.Children.Clear();
        foreach (var child in fresh.Children.ToList()) { fresh.Children.Remove(child); _tabs.Children.Add(child); }
    }

    //JJQR - AI (Nov 21, 2026) - medicine names: ALL CAPS -> readable case, keeping units (mg, ml) and acronyms (ORS) right
    internal static string NiceMedicine(string name)
    {
        if (name != name.ToUpperInvariant()) return name;
        var t = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        t = System.Text.RegularExpressions.Regex.Replace(t, @"(?<=\d)(Mg|Ml|Mcg|G|Iu)\b", m => m.Value.ToLowerInvariant());
        return t.Replace("(Ors)", "(ORS)").Replace(" Tab", " tab").Replace(" Cap", " cap");
    }
    //JJQR - END

    private View Rail(PatientChart c)
    {
        var p = c.Patient;
        View Panel(string title, View body) => new Border
        {
            Style = ClinicTheme.Style("Card"), Padding = new Thickness(16, 14),
            Content = new VerticalStackLayout { Spacing = 10, Children = { new Label { Text = title.ToUpperInvariant(), Style = ClinicTheme.Style("TableHeader") }, body } },
        };
        View Line(string left, string right) { var g = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            g.Add(new Label { Text = left, Style = ClinicTheme.Style("BodyMd"), LineBreakMode = LineBreakMode.WordWrap }, 0, 0);
            g.Add(new Label { Text = right, Style = ClinicTheme.Style("LabelMd"), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start }, 1, 0); return g; }

        // identity
        var avatar = new Border
        {
            WidthRequest = 64, HeightRequest = 64, StrokeThickness = 0, HorizontalOptions = LayoutOptions.Start,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 32 }, BackgroundColor = ClinicTheme.Color("Primary").WithAlpha(0.12f),
            Content = new Label { Text = DoctorPatientCardRules.Initials(p.FullName), FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = ClinicTheme.Color("Primary"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        var identity = new VerticalStackLayout { Spacing = 4 };
        identity.Children.Add(avatar);
        identity.Children.Add(new Label { Text = p.FullName, Style = ClinicTheme.Style("HeadlineMd"), Margin = new Thickness(0, 6, 0, 0) });
        identity.Children.Add(new Label { Text = p.PatientCode, Style = ClinicTheme.Style("MutedSm"), FontFamily = "Courier New" });
        identity.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.5f), Margin = new Thickness(0, 8) });
        identity.Children.Add(Line("Sex", p.Sex.ToString()));
        identity.Children.Add(Line("Age", $"{p.Age} years"));
        identity.Children.Add(Line("Born", Iso(p.DateOfBirth)));
        if (!string.IsNullOrWhiteSpace(p.ContactNumber)) identity.Children.Add(Line("Phone", p.ContactNumber!));
        if (c.Bookings.Count > 0) identity.Children.Add(Line("Last visit", Iso(c.Bookings.Max(b => b.AppointmentDate))));
        identity.Children.Add(Line("Visits with you", c.Bookings.Count.ToString()));
        var print = StaffUi.Btn("Print patient card", () => PrintActions.RunAsync(this, async () =>
        {
            var card = await Svc<IDoctorPrintDataService>().GetPatientCardAsync(_patientId)
                ?? throw new InvalidOperationException("The patient card could not be loaded.");
            return PatientCardDocument.Build(card);
        }), "SecondaryButton");
        print.HorizontalOptions = LayoutOptions.Fill;
        identity.Children.Add(new BoxView { HeightRequest = 1, Color = Colors.Transparent });
        identity.Children.Add(print);
        var rail = new VerticalStackLayout { Spacing = 12, Children = { new Border { Style = ClinicTheme.Style("Card"), Padding = new Thickness(16, 16), Content = identity } } };

        // latest record: a highlighted box so the most recent visit stands out from the history below it
        //JJQR - AI (Nov 21, 2026) - "latest" = newest booking that has vitals; several visits on one day are ordered by queue number
        static int Seq(string? q) => int.TryParse(new string((q ?? "").Where(char.IsDigit).ToArray()), out var n) ? n : 0;
        var last = c.Bookings.OrderByDescending(b => b.AppointmentDate)
            .Select(b => c.VitalVisits.FirstOrDefault(v => v.BookingId == b.BookingId)).FirstOrDefault(v => v is not null)
            ?? c.VitalVisits.OrderByDescending(v => v.Date).FirstOrDefault();
        //JJQR - END
        if (last is not null)
        {
            var tiles = new Grid { ColumnSpacing = 8, RowSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
            var n = 0;
            foreach (var t in c.VitalTemplates.Where(t => t.IsDefault))
            {
                if (!last.Values.TryGetValue(t.TemplateId, out var val) || string.IsNullOrWhiteSpace(val)) continue;
                var value = new FormattedString();
                value.Spans.Add(new Span { Text = val, FontSize = 16, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("OnSurface") });
                if (!string.IsNullOrWhiteSpace(t.Unit)) value.Spans.Add(new Span { Text = " " + t.Unit, FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
                var tile = new Border
                {
                    BackgroundColor = Colors.White, Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1, Padding = new Thickness(10, 7),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                    Content = new VerticalStackLayout { Spacing = 1, Children = { new Label { Text = t.Description[..1] + t.Description[1..].ToLowerInvariant(), FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant") }, new Label { FormattedText = value } } },
                };
                if (n % 2 == 0) tiles.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                tiles.Add(tile, n % 2, n / 2);
                n++;
            }
            if (n > 0)
            {
                var consult = c.Consultations.FirstOrDefault(x => x.BookingId == last.BookingId);
                var booking = c.Bookings.FirstOrDefault(x => x.BookingId == last.BookingId);
                var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                head.Add(new Label { Text = "LATEST RECORD", FontSize = 11, FontAttributes = FontAttributes.Bold, CharacterSpacing = 1, TextColor = ClinicTheme.Color("Primary"), VerticalOptions = LayoutOptions.Center }, 0, 0);
                head.Add(new Border { BackgroundColor = ClinicTheme.Color("Primary"), StrokeThickness = 0, Padding = new Thickness(8, 3), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                    Content = new Label { Text = Iso(last.Date), FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Colors.White } }, 1, 0);
                var box = new VerticalStackLayout { Spacing = 10, Children = { head } };
                var what = !string.IsNullOrWhiteSpace(consult?.ChiefComplaint) ? consult!.ChiefComplaint! : "Consultation";
                var sub = string.Join(" · ", new[] { booking?.QueueNumber, ChartVisitCards.DiagnosisOrAssessment(consult) is { Count: > 0 } dxl ? string.Join(", ", dxl) : null }.Where(x => !string.IsNullOrWhiteSpace(x)));
                box.Children.Add(new VerticalStackLayout { Spacing = 1, Children = { new Label { Text = what, Style = ClinicTheme.Style("LabelLg") }, new Label { Text = sub, Style = ClinicTheme.Style("MutedSm"), IsVisible = sub.Length > 0 } } });
                box.Children.Add(new Label { Text = "VITALS", Style = ClinicTheme.Style("TableHeader"), Margin = new Thickness(0, 4, 0, -4) });
                box.Children.Add(tiles);
                // diagnosis + medicines of THIS visit
                if (ChartVisitCards.DiagnosisOrAssessment(consult) is { Count: > 0 } dxAll)
                {
                    box.Children.Add(new Label { Text = "DIAGNOSIS", Style = ClinicTheme.Style("TableHeader"), Margin = new Thickness(0, 4, 0, -4) });
                    var dxPills = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
                    foreach (var d in dxAll)
                        dxPills.Children.Add(new Border { BackgroundColor = Colors.White, Stroke = ClinicTheme.Color("Primary").WithAlpha(0.5f), StrokeThickness = 1, Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 6, 6),
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Content = new Label { Text = d, FontSize = 12, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("Primary"), LineBreakMode = LineBreakMode.WordWrap } });
                    box.Children.Add(dxPills);
                }
                var visitRx = c.Prescriptions.Where(g => g.BookingId == last.BookingId).SelectMany(g => g.Lines).ToList();
                if (visitRx.Count > 0)
                {
                    box.Children.Add(new Label { Text = "PRESCRIBED", Style = ClinicTheme.Style("TableHeader"), Margin = new Thickness(0, 4, 0, -4) });
                    foreach (var l in visitRx)
                        box.Children.Add(new Border { BackgroundColor = Colors.White, Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1, Padding = new Thickness(10, 7),
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                            Content = new VerticalStackLayout { Spacing = 1, Children = { new Label { Text = NiceMedicine(l.GenericName), FontSize = 13, FontFamily = ClinicTheme.FontSemiBold, LineBreakMode = LineBreakMode.WordWrap },
                                new Label { Text = string.Join(" · ", new[] { l.Dosage, l.Quantity }.Where(x => !string.IsNullOrWhiteSpace(x))), FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant") },
                                new Label { Text = l.Instruction ?? "", FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant"), IsVisible = !string.IsNullOrWhiteSpace(l.Instruction), LineBreakMode = LineBreakMode.WordWrap } } } });
                }
                rail.Children.Add(new Border
                {
                    BackgroundColor = ClinicTheme.Color("Primary").WithAlpha(0.07f), Stroke = ClinicTheme.Color("Primary"), StrokeThickness = 1.5, Padding = new Thickness(14),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Content = box,
                });
            }
        }
        //JJQR - AI (Nov 21, 2026) - the old "all diagnoses / all medicines" history panels were removed: they only repeated the Consultations and Prescriptions tabs
        return rail;
    }

    //JJQR - AI (Nov 21, 2026) - ONE Visits list. A visit opens to show everything about it: notes, vitals, prescriptions, lab orders, follow-up, certificate and fee
    // (this replaces the separate Timeline / Appointments / Consultations / Vitals / Prescriptions tabs).
    internal static int Seq(string? q) => int.TryParse(new string((q ?? "").Where(char.IsDigit).ToArray()), out var n) ? n : 0;

    private static View Block(string title, View body) =>
        new VerticalStackLayout { Spacing = 6, Children = { new Label { Text = title.ToUpperInvariant(), Style = ClinicTheme.Style("TableHeader") }, body } };

    private static View NoneText(string text) => new Label { Text = text, Style = ClinicTheme.Style("MutedSm") };

    private static View MiniBox(View content) => new Border
    {
        BackgroundColor = Colors.White, Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1, Padding = new Thickness(10, 7),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Content = content,
    };

    private void RenderVisits(PatientChart c)
    {
        if (c.Bookings.Count == 0) { Empty("No visits with you yet."); return; }
        if (ReferredBookingId is { } vb)
        {
            var bar = new HorizontalStackLayout { Spacing = 8 };
            bar.Children.Add(StaffUi.Btn("New Prescription", () => OpenEditorAsync(vb, null, null)));
            bar.Children.Add(StaffUi.Btn("Add Vitals", () => DoctorExtraUi.OpenConsultAsync(vb), "SecondaryButton"));
            _tab.Children.Add(bar);
        }
        var defaults = c.VitalTemplates.Where(t => t.IsDefault).ToList();
        foreach (var b in c.Bookings.OrderByDescending(x => x.AppointmentDate))
        {
            var consult = c.Consultations.FirstOrDefault(x => x.BookingId == b.BookingId);
            var ex = c.VisitExtras?.FirstOrDefault(e => e.BookingId == b.BookingId);
            var vit = c.VitalVisits.FirstOrDefault(v => v.BookingId == b.BookingId);
            var rx = c.Prescriptions.Where(g => g.BookingId == b.BookingId).ToList();
            var open = _expandedConsult == b.BookingId;
            var bookingId = b.BookingId;

            // ----- header row
            var title = !string.IsNullOrWhiteSpace(consult?.ChiefComplaint) ? consult!.ChiefComplaint! : b.ServiceNames.Count == 0 ? "Consultation" : string.Join(", ", b.ServiceNames);
            var tags = new List<string>();
            if (rx.Count > 0) tags.Add($"Rx {rx.Sum(g => g.Lines.Count)}");
            if (ex is { LabOrders.Count: > 0 }) tags.Add($"Labs {ex.LabOrders.Count}");
            if (ex?.FollowUpDate is { } fd) tags.Add("Follow-up " + Iso(fd));
            if (ex is { HasCertificate: true }) tags.Add("Med cert issued"); else if (ex is { MedCertRequested: true }) tags.Add("Med cert requested");
            var what = new VerticalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center, Children = { new Label { Text = title, Style = ClinicTheme.Style("LabelLg") } } };
            if (ChartVisitCards.DiagnosisOrAssessment(consult) is { Count: > 0 } dxs) what.Children.Add(new Label { Text = string.Join(" · ", dxs), Style = ClinicTheme.Style("MutedSm") });
            if (tags.Count > 0)
            {
                var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
                foreach (var t in tags)
                    chips.Children.Add(new Border { Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(7, 2), Margin = new Thickness(0, 2, 6, 0), Content = new Label { Text = t, Style = ClinicTheme.Style("PillLabelInfo") } });
                what.Children.Add(chips);
            }
            var head = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(105), new ColumnDefinition(64), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(96) } };
            head.Add(new Label { Text = Iso(b.AppointmentDate), Style = ClinicTheme.Style("LabelMd"), VerticalOptions = LayoutOptions.Center }, 0, 0);
            head.Add(new Label { Text = b.QueueNumber ?? "—", Style = ClinicTheme.Style("MutedSm"), FontFamily = "Courier New", VerticalOptions = LayoutOptions.Center }, 1, 0);
            head.Add(what, 2, 0);
            head.Add(new HorizontalStackLayout { VerticalOptions = LayoutOptions.Center, Children = { ClinicTheme.MakeStatusPill(b.Status.ToString()) } }, 3, 0);
            head.Add(new Label { Text = open ? "Hide ▴" : "Details ▾", FontSize = 13, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("Primary"), HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center }, 4, 0);
            var col = new VerticalStackLayout { Spacing = 12, Children = { head } };

            if (open)
            {
                col.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.5f) });
                // ----- left column: notes + vitals
                var left = new VerticalStackLayout { Spacing = 16 };
                var hasSoap = consult is not null && new[] { consult.Subjective, consult.Objective, consult.Assessment, consult.Plan }.Any(f => !string.IsNullOrWhiteSpace(f));
                if (hasSoap)
                {
                    var soap = new VerticalStackLayout { Spacing = 8 };
                    foreach (var (label, value) in new[] { ("Subjective", consult!.Subjective), ("Objective", consult.Objective), ("Assessment", consult.Assessment), ("Plan", consult.Plan) })
                        soap.Children.Add(new VerticalStackLayout { Spacing = 1, Children = { new Label { Text = label, Style = ClinicTheme.Style("LabelMd"), TextColor = ClinicTheme.Color("Primary") }, ClinicTheme.Text(string.IsNullOrWhiteSpace(value) ? "Not recorded." : value!, "BodyMd") } });
                    left.Children.Add(Block("Notes", soap));
                }
                else left.Children.Add(Block("Notes", NoneText("No notes recorded for this visit.")));
                if (vit is not null)
                {
                    var tiles = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
                    foreach (var t in defaults)
                    {
                        if (!vit.Values.TryGetValue(t.TemplateId, out var val) || string.IsNullOrWhiteSpace(val)) continue;
                        var fs = new FormattedString();
                        fs.Spans.Add(new Span { Text = val, FontSize = 15, FontFamily = ClinicTheme.FontSemiBold });
                        if (!string.IsNullOrWhiteSpace(t.Unit)) fs.Spans.Add(new Span { Text = " " + t.Unit, FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
                        var tile = MiniBox(new VerticalStackLayout { Spacing = 1, Children = { new Label { Text = t.Description[..1] + t.Description[1..].ToLowerInvariant(), FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant"), LineBreakMode = LineBreakMode.NoWrap }, new Label { FormattedText = fs, LineBreakMode = LineBreakMode.NoWrap } } });
                        tile.Margin = new Thickness(0, 0, 8, 8);
                        FlexLayout.SetShrink(tile, 0);
                        tiles.Children.Add(tile);
                    }
                    left.Children.Add(Block("Vitals", tiles));
                }
                else left.Children.Add(Block("Vitals", NoneText("No vitals recorded.")));

                // ----- right column: prescriptions, labs, follow-up, certificate, fee
                var right = new VerticalStackLayout { Spacing = 16 };
                if (rx.Count > 0)
                {
                    var list = new VerticalStackLayout { Spacing = 8 };
                    foreach (var g in rx)
                    {
                        var group = g;
                        foreach (var l in g.Lines)
                            list.Children.Add(MiniBox(new VerticalStackLayout { Spacing = 1, Children =
                            {
                                new Label { Text = NiceMedicine(l.GenericName), FontSize = 13, FontFamily = ClinicTheme.FontSemiBold, LineBreakMode = LineBreakMode.WordWrap },
                                new Label { Text = string.Join(" · ", new[] { l.Dosage, l.Quantity }.Where(x => !string.IsNullOrWhiteSpace(x))), FontSize = 11, TextColor = ClinicTheme.Color("OnSurfaceVariant") },
                                new Label { Text = l.Instruction ?? "", FontSize = 12, IsVisible = !string.IsNullOrWhiteSpace(l.Instruction), LineBreakMode = LineBreakMode.WordWrap },
                            } }));
                        var actions = new HorizontalStackLayout { Spacing = 8 };
                        actions.Children.Add(StaffUi.Btn("Print prescription", () => PrintPreviewAsync(c.Patient.FullName, group), "SecondaryButton"));
                        if (ReferredBookingId is { } copyTo)
                        {
                            actions.Children.Add(StaffUi.Btn("Edit", () => OpenEditorAsync(group.BookingId, group.GroupId, null), "GhostButton"));
                            actions.Children.Add(StaffUi.Btn("Copy", () => OpenEditorAsync(copyTo, null, group.GroupId), "GhostButton"));
                            actions.Children.Add(StaffUi.DangerGhost("Delete", () => DeleteGroupAsync(group.GroupId)));
                        }
                        list.Children.Add(actions);
                    }
                    right.Children.Add(Block("Prescription", list));
                }
                else right.Children.Add(Block("Prescription", NoneText("None prescribed.")));

                if (ex is { LabOrders.Count: > 0 })
                {
                    var labs = new VerticalStackLayout { Spacing = 3 };
                    foreach (var n in ex.LabOrders) labs.Children.Add(ClinicTheme.Text("• " + n, "BodyMd"));
                    right.Children.Add(Block("Lab orders", labs));
                }
                else right.Children.Add(Block("Lab orders", NoneText("None ordered.")));

                if (ex?.FollowUpDate is { } fdate)
                {
                    var f = new VerticalStackLayout { Spacing = 2, Children = { ClinicTheme.Text(Iso(fdate), "LabelLg") } };
                    if (!string.IsNullOrWhiteSpace(ex.FollowUpReason)) f.Children.Add(ClinicTheme.Text(ex.FollowUpReason!, "BodyMd"));
                    if (!string.IsNullOrWhiteSpace(ex.FollowUpInstructions)) f.Children.Add(ClinicTheme.Text(ex.FollowUpInstructions!, "MutedSm"));
                    right.Children.Add(Block("Follow-up", f));
                }
                else right.Children.Add(Block("Follow-up", NoneText("No follow-up scheduled.")));

                if (ex is { HasCertificate: true })
                    right.Children.Add(Block("Medical certificate", new VerticalStackLayout { Spacing = 2, Children =
                        { ClinicTheme.Text("Issued" + (ex.CertificateIssued is { } ci ? " " + Iso(ci) : ""), "LabelLg"), ClinicTheme.Text(ex.CertificateDiagnosis ?? "", "MutedSm") } }));
                else if (ex is { MedCertRequested: true }) right.Children.Add(Block("Medical certificate", NoneText("Requested by the patient. Not issued yet.")));
                else right.Children.Add(Block("Medical certificate", NoneText("Not requested.")));

                if (ex is not null)
                {
                    var fee = $"{(ex.VisitType == "FollowUp" ? "Follow-up visit" : "New visit")}{(string.IsNullOrWhiteSpace(ex.Discount) ? "" : " · " + ex.Discount + " discount")}{(ex.Fee is { } f2 ? " · " + ClinicNode.Core.FeeCalculator.FormatPeso(f2) : "")}";
                    right.Children.Add(Block("Visit & fee", ClinicTheme.Text(fee, "BodyMd")));
                }

                var body = new Grid { ColumnSpacing = 28, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
                body.Add(left, 0, 0); body.Add(right, 1, 0);
                col.Children.Add(body);
            }
            var card = StaffUi.Row(col);
            card.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { _expandedConsult = open ? null : bookingId; RenderTab(); }) });
            _tab.Children.Add(card);
        }
    }
    //JJQR - END

    private static View Snapshot(PatientChart c)
    {
        View Tile(string value, string label, bool emphasise = false)
        {
            var v = new Label { Text = value, FontSize = 22, FontFamily = ClinicTheme.FontSemiBold, TextColor = emphasise ? ClinicTheme.Color("Primary") : ClinicTheme.Color("OnSurface") };
            var l = new Label { Text = label, FontSize = 12, TextColor = ClinicTheme.Color("OnSurfaceVariant") };
            return new Border { Style = ClinicTheme.Style("Card"), Padding = new Thickness(16, 12), Content = new VerticalStackLayout { Spacing = 2, Children = { v, l } } };
        }
        var last = c.Bookings.Count == 0 ? "—" : Iso(c.Bookings.Max(b => b.AppointmentDate));
        var tiles = new (string, string, bool)[]
        {
            (c.Bookings.Count.ToString(), "Visits with you", false), (last, "Last visit", true), (c.Consultations.Count.ToString(), "Consultations", false),
            (c.Prescriptions.Count.ToString(), "Prescriptions", false), (c.Vaccinations.Count.ToString(), "Vaccinations", false),
        };
        var g = new Grid { ColumnSpacing = 12 };
        for (var i = 0; i < tiles.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            g.Add(Tile(tiles[i].Item1, tiles[i].Item2, tiles[i].Item3), i, 0);
        }
        return g;
    }
    //JJQR - END
    //JJQR - END

    protected override async Task LoadAsync()
    {
        _header.Children.Clear();
        var doctorId = DoctorExtraUi.DoctorId(Sp);
        if (doctorId is null) { _header.Children.Add(DoctorExtraUi.NotLinked()); _chart = null; RenderTab(); return; }
        //JJQR - AI (21 Nov 2026) - web ErrorState: "Patient chart unavailable" / "This chart could not be loaded. Please try again." + retry
        try { _chart = await Svc<IDoctorPatientsService>().GetChartAsync(doctorId.Value, _patientId); _loadFailed = false; }
        catch
        {
            _chart = null; _loadFailed = true;
            _header.Children.Add(StaffUi.Card(
                ClinicTheme.Text("Patient chart unavailable", "HeadlineSm"),
                ClinicTheme.Text("This chart could not be loaded. Please try again.", "MutedBody"),
                StaffUi.Btn("Try again", () => ReloadAsync(), "SecondaryButton")));
            RenderTab();
            return;
        }
        //JJQR - END
        if (_chart is null) { _header.Children.Add(ClinicTheme.Text("Patient not found.", "HeadlineSm")); RenderTab(); return; }
        var p = _chart.Patient;
        //JJQR - AI (21 Nov 2026) - header card = web layout (initials avatar, name + code, sex · age · Born ISO, phone chip) via Extra2/DoctorPatientHeaderCard; the "Print patient card" button
        // (GET /api/patients/{id}/card; identity, blood type, allergies, emergency contact only) stays under it.
        _header.Children.Add(Rail(_chart));
        _expandedConsult ??= _chart.Bookings.OrderByDescending(x => x.AppointmentDate).Select(x => (Guid?)x.BookingId).FirstOrDefault();
        RefreshTabs();
        //JJQR - END
        RenderTab();
    }

    private IEnumerable<T> Sorted<T, TKey>(IEnumerable<T> items, Func<T, TKey> key, bool newest = true) => newest ? items.OrderByDescending(key) : items.OrderBy(key);

    private void Empty(string text) => _tab.Children.Add(StaffUi.Card(ClinicTheme.Text(text, "MutedBody")));

    //JJQR - AI (21 Nov 2026) - helpers for the web tables (Appointments, Vitals, Vaccinations) and the sort select
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static View Table(string[] headers, IReadOnlyList<View[]> rows)
    {
        var g = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        for (var i = 0; i < headers.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition(i == 0 ? GridLength.Star : GridLength.Auto));
        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var i = 0; i < headers.Length; i++) g.Add(ClinicTheme.Text(headers[i], "LabelMd"), i, 0);
        for (var r = 0; r < rows.Count; r++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (var c = 0; c < rows[r].Length; c++) g.Add(rows[r][c], c, r + 1);
        }
        return g;
    }

    private static View Cell(string text) => ClinicTheme.Text(text, "BodyMd");

    private View SortSelect(bool newest, Action<bool> set)
    {
        var picker = new Picker { ItemsSource = new[] { "Newest first", "Oldest first" }, SelectedIndex = newest ? 0 : 1, WidthRequest = 160 };
        picker.SelectedIndexChanged += (_, _) => { set(picker.SelectedIndex != 1); RenderTab(); };
        return ClinicPickers.PickerBox(null, picker, 170);
    }
    //JJQR - END

    private void RenderTab()
    {
        _tab.Children.Clear();
        StaffUi.Show(_tabError, null);
        if (_chart is not { } c) return;
        switch (_selected)
        {
            case "timeline":
                RenderVisits(c);
                break;
            case "labs":
                Files(c.LabResults, "No lab results yet", "Results uploaded for this patient (from the lab or the front desk) will be listed here.");
                break;
            case "documents":
                Files(c.Documents, "No documents yet", "Referral letters, scans and other files added to this patient's record will be listed here.");
                break;
            //JJQR - AI (Nov 21, 2026) - Vaccinations as the shared aligned table (Vaccine | Dose | Date | Status)
            case "vaccinations":
                if (c.Vaccinations.Count == 0) { EmptyCard("No vaccinations recorded yet", "Vaccines given or scheduled for this patient will be listed here with their dose and date."); break; }
                _tab.Children.Add(ClinicUi.Table(
                    [
                        new TableColumn("Vaccine", GridLength.Star, o => ClinicTheme.Text(((ChartVaccination)o).VaccineName, "LabelLg")),
                        new TableColumn("Dose", new GridLength(90), o => ClinicTheme.Text(((ChartVaccination)o).DoseNumber?.ToString() ?? "—", "BodyMd")),
                        new TableColumn("Date", new GridLength(140), o => ClinicTheme.Text(((ChartVaccination)o).Date is { } d ? Iso(d) : "—", "BodyMd")),
                        new TableColumn("Status", new GridLength(140), o => new HorizontalStackLayout { Children = { ClinicTheme.MakeStatusPill(((ChartVaccination)o).Status.ToString()) } }),
                    ],
                    c.Vaccinations.Cast<object>()));
                break;
            //JJQR - END
        }
    }

    //JJQR - AI (21 Nov 2026) - web Lab Results / Documents rows show the title only (no upload line)
    //JJQR - AI (Nov 21, 2026) - empty state: a card with a heading and a hint about what will appear; files as an aligned table (name | uploaded)
    private void EmptyCard(string title, string hint) =>
        _tab.Children.Add(StaffUi.Card(new VerticalStackLayout { Spacing = 4, Padding = new Thickness(0, 14), Children = { ClinicTheme.Text(title, "HeadlineSm"), ClinicTheme.Text(hint, "MutedBody") } }));

    private void Files(IReadOnlyList<ChartFile> files, string emptyTitle, string emptyHint)
    {
        if (files.Count == 0) { EmptyCard(emptyTitle, emptyHint); return; }
        _tab.Children.Add(ClinicUi.Table(
            [
                new TableColumn("Name", GridLength.Star, o => ClinicTheme.Text(((ChartFile)o).Title, "LabelLg")),
                new TableColumn("Uploaded", new GridLength(160), o => ClinicTheme.Text(((ChartFile)o).UploadedAt.ToLocalTime().ToString("yyyy-MM-dd"), "BodyMd")),
                //JJQR - AI (Nov 23, 2026) - web parity: the web opens the file; the desktop only listed names
                new TableColumn("", new GridLength(110), o => StaffUi.Btn("Open", () => OpenFileAsync((ChartFile)o), "GhostButton")),
                //JJQR - END
            ],
            files.Cast<object>()));
    }

    //JJQR - AI (Nov 23, 2026) - download the file with the sign-in and open it in the computer's own viewer. Needs the clinic server (offline: say so).
    private async Task OpenFileAsync(ChartFile file)
    {
        try
        {
            var path = await Svc<IDoctorPatientsService>().DownloadFileAsync(file);
            if (path is null) { await DisplayAlertAsync("Open file", "Files are stored on the clinic server. Connect to the internet (Online mode) to open this file.", "OK"); return; }
            await Launcher.Default.OpenAsync(new OpenFileRequest(file.Title, new ReadOnlyFile(path)));
        }
        catch (ApiNotFoundException) { await DisplayAlertAsync("Open file", "This file is no longer on the server.", "OK"); }
        catch (ApiOfflineException) { await DisplayAlertAsync("Open file", "The clinic server could not be reached. Check the internet and try again.", "OK"); }
        catch (Exception) { await DisplayAlertAsync("Open file", "The file could not be opened. Please try again.", "OK"); }
    }
    //JJQR - END
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - prescription actions
    private async Task OpenEditorAsync(Guid bookingId, Guid? editGroupId, Guid? copyFromGroupId)
    {
        if (_chart is not { } c) return;
        var page = new DoctorPrescriptionFormPage(Sp, _patientId, c.Patient.FullName, bookingId, editGroupId, copyFromGroupId);
        // web: router.push(/doctor/patients/{id}?bookingId={booking}&tab=prescriptions); the chart reloads itself when it appears again
        page.Saved += () => Select("prescriptions");
        await Navigation.PushAsync(page);
    }

    /// <summary>Web "Print Preview" modal: the group's lines + file name, then Print.</summary>
    private async Task PrintPreviewAsync(string patientName, ChartRxGroup g)
    {
        var lines = string.Join("\n", g.Lines.Select((l, i) => $"{i + 1}. {l.GenericName} {l.Dosage} #{l.Quantity}" + (string.IsNullOrWhiteSpace(l.Instruction) ? "" : $"\n    {l.Instruction}"))); //JJQR - AI (Nov 22, 2026) - the dose was printed as "Sig." //JJQR - END
        var message = $"{patientName}\n{g.CreatedAt.UtcDateTime:yyyy-MM-dd}\n\n{lines}\n\nFile name: {System.Text.RegularExpressions.Regex.Replace(patientName, @"\s+", "_")}_{g.BookingId}";
        var bookingId = g.BookingId;
        // print after the preview closes, so the print sheet opens over the chart and not over the modal
        if (await StaffDialog.ShowAsync(this, "Print Preview", message, null, null, "Close", "Print", _ => Task.FromResult<string?>(null)))
            await PrintActions.RunAsync(this, () => Svc<IPrintDocumentFactory>().PrescriptionAsync(bookingId));
    }

    private async Task DeleteGroupAsync(Guid groupId)
    {
        if (!await DoctorExtraUi.ConfirmAsync(this, "Delete Prescription?", "Are you sure you want to delete prescription/s?", "Delete", danger: true)) return;
        var r = await Svc<IDoctorPrescriptionsService>().DeleteGroupAsync(groupId);
        if (!r.Succeeded) { StaffUi.Show(_tabError, r.Error); return; }
        await ReloadAsync();
    }
    //JJQR - END
}
//JJQR - END
