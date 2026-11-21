//JJQR - AI (Nov 21, 2026) - D-CHART: patient chart (web doctor/patients/[id]/page.tsx): header + tabs Timeline / Appointments / Consultations / Vitals / Prescriptions / Lab Results / Documents / Vaccinations. Read-only; prescriptions are edited from the consultation.
//JJQR - AI (21 Nov 2026) - prescriptions EDITABLE like the web: when the chart is opened for a booking (web ?bookingId=, here ReferredBookingId via OpenAsync)
// the Prescriptions tab shows "New Prescription" and per-group Edit / Copy / Delete ("Delete Prescription?"); every group has Print (the web "Print Preview"
// modal, then Print). "Print patient card" in the header prints GET /api/patients/{id}/card (no clinical notes). Data is online-first.
//JJQR - END
using ClinicNode.App.Controls;
using ClinicNode.App.Services;
using ClinicNode.App.Views.Staff;
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
    private FlexLayout? _tabs;

    /// <summary>Web ?bookingId=: the visit the doctor is working on. Enables New / Edit / Copy / Delete on the Prescriptions tab.</summary>
    public Guid? ReferredBookingId { get; init; }

    /// <summary>Open the chart, optionally for a booking and on a tab (web /doctor/patients/{id}?bookingId=&amp;tab=). Usable from any doctor screen.</summary>
    public static Task OpenAsync(Page from, Guid patientId, Guid? bookingId = null, string? tab = null)
    {
        var sp = from.Handler?.MauiContext?.Services ?? StaffUi.Services;
        var page = new DoctorPatientChartPage(sp, patientId) { ReferredBookingId = bookingId };
        if (tab is not null && ChartTabs.All.Any(t => t.Id == tab)) page.Select(tab);
        return from.Navigation.PushAsync(page);
    }
    //JJQR - END

    public DoctorPatientChartPage(IServiceProvider sp, Guid patientId) : base(sp, "Patient chart", live: false)
    {
        _patientId = patientId;
        //JJQR - AI (21 Nov 2026) - tab strip kept so Select() can re-render it; tab-level error line for prescription actions
        _tabs = DoctorExtraUi.Tabs(ChartTabs.All, () => _selected, id => { _selected = id; RenderTab(); });
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Link("← Back to Patients", () => Navigation.PopAsync()),
                LoadError, _header,
                _tabs,
                _tabError,
                StaffUi.Card(_tab),
            },
        });
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026)
    private void Select(string tab)
    {
        _selected = tab;
        if (_tabs is null) return;
        var fresh = DoctorExtraUi.Tabs(ChartTabs.All, () => _selected, id => { _selected = id; RenderTab(); });
        _tabs.Children.Clear();
        foreach (var child in fresh.Children.ToList()) { fresh.Children.Remove(child); _tabs.Children.Add(child); }
    }
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
        _header.Children.Add(Views.Doctor.Extra2.DoctorPatientHeaderCard.Build(p,
            StaffUi.Wrap(DoctorExtraUi.Spaced(StaffUi.Btn("Print patient card", () => PrintActions.RunAsync(this, async () =>
            {
                var card = await Svc<IDoctorPrintDataService>().GetPatientCardAsync(_patientId)
                    ?? throw new InvalidOperationException("The patient card could not be loaded.");
                return PatientCardDocument.Build(card);
            }), "SecondaryButton")))));
        //JJQR - END
        RenderTab();
    }

    private IEnumerable<T> Sorted<T, TKey>(IEnumerable<T> items, Func<T, TKey> key, bool newest = true) => newest ? items.OrderByDescending(key) : items.OrderBy(key);

    private void Empty(string text) => _tab.Children.Add(ClinicTheme.Text(text, "MutedBody"));

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
        return picker;
    }
    //JJQR - END

    private void RenderTab()
    {
        _tab.Children.Clear();
        StaffUi.Show(_tabError, null);
        if (_chart is not { } c) return;
        switch (_selected)
        {
            //JJQR - AI (21 Nov 2026) - web Timeline: the patient's bookings only (service names or "Consultation", date, status pill), in the order the chart returns them
            case "timeline":
                if (c.Bookings.Count == 0) { Empty("No appointments with you yet."); break; }
                foreach (var b in c.Bookings)
                    _tab.Children.Add(StaffUi.Row(
                        ClinicTheme.Text(b.ServiceNames.Count == 0 ? "Consultation" : string.Join(", ", b.ServiceNames), "BodyMd"),
                        ClinicTheme.Text(Iso(b.AppointmentDate), "MutedSm"),
                        StaffUi.Wrap(ClinicTheme.MakeStatusPill(b.Status.ToString()))));
                break;
            // web Appointments: a table Date | Service | Status | Queue # ("—" when empty); no row actions
            case "appointments":
                if (c.Bookings.Count == 0) { Empty("No appointments with you yet."); break; }
                _tab.Children.Add(Table(["Date", "Service", "Status", "Queue #"],
                    c.Bookings.Select(b => new View[]
                    {
                        Cell(Iso(b.AppointmentDate)),
                        Cell(b.ServiceNames.Count == 0 ? "—" : string.Join(", ", b.ServiceNames)),
                        StaffUi.Wrap(ClinicTheme.MakeStatusPill(b.Status.ToString())),
                        Cell(b.QueueNumber ?? "—"),
                    }).ToList()));
                break;
            // web Consultations: one-line header (chief complaint or "Consultation" + date), diagnosis chips, tap to expand the SOAP notes
            case "consultations":
                if (c.Consultations.Count == 0) { Empty("No consultations recorded yet."); break; }
                foreach (var x in c.Consultations)
                {
                    var id = x.ConsultationId;
                    var expanded = _expandedConsult == id;
                    var col = StaffUi.Column(6);
                    var head = StaffUi.Wrap(
                        DoctorExtraUi.Spaced(ClinicTheme.Text((expanded ? "▾ " : "▸ ") + (string.IsNullOrWhiteSpace(x.ChiefComplaint) ? "Consultation" : x.ChiefComplaint), "LabelLg"), 12, 0),
                        ClinicTheme.Text(Iso(x.AppointmentDate), "MutedSm"));
                    head.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { _expandedConsult = expanded ? null : id; RenderTab(); }) });
                    col.Children.Add(head);
                    if (x.Diagnoses.Count > 0)
                    {
                        var chips = StaffUi.Wrap();
                        foreach (var d in x.Diagnoses) chips.Children.Add(DoctorExtraUi.Spaced(ClinicTheme.Text(d, "MutedSm"), 8, 4));
                        col.Children.Add(chips);
                    }
                    if (expanded)
                    {
                        var hasSoap = new[] { x.Subjective, x.Objective, x.Assessment, x.Plan }.Any(f => !string.IsNullOrWhiteSpace(f));
                        if (!hasSoap) col.Children.Add(ClinicTheme.Text("No SOAP notes recorded for this visit.", "MutedBody"));
                        else
                            foreach (var (label, value) in new[] { ("Subjective", x.Subjective), ("Objective", x.Objective), ("Assessment", x.Assessment), ("Plan", x.Plan) })
                                col.Children.Add(ClinicTheme.Text($"{label}: {(string.IsNullOrWhiteSpace(value) ? "Not recorded." : value)}", "BodyMd"));
                    }
                    _tab.Children.Add(StaffUi.Row(col));
                }
                break;
            // web Vitals: List / Table toggle, Newest/Oldest select (only when there are readings), "Add Vitals" when the chart was opened for a booking;
            // only the default vital fields are shown, "—" for a missing reading
            case "vitals":
            {
                var bar = StaffUi.Wrap(
                    DoctorExtraUi.Chip("List view", !_vitalsTable, () => { _vitalsTable = false; RenderTab(); }),
                    DoctorExtraUi.Chip("Table view", _vitalsTable, () => { _vitalsTable = true; RenderTab(); }));
                if (c.VitalVisits.Count > 0) bar.Children.Add(DoctorExtraUi.Spaced(SortSelect(_vitalsNewest, v => _vitalsNewest = v)));
                if (ReferredBookingId is { } vb) bar.Children.Add(DoctorExtraUi.Spaced(StaffUi.Btn("Add Vitals", () => DoctorExtraUi.OpenConsultAsync(vb))));
                _tab.Children.Add(bar);
                if (c.VitalVisits.Count == 0) { Empty("No vital signs recorded yet."); break; }
                var defaults = c.VitalTemplates.Where(t => t.IsDefault).ToList();
                var visits = Sorted(c.VitalVisits, v => v.Date, _vitalsNewest).ToList();
                string Reading(ChartVitalVisit v, ChartVitalTemplate t) =>
                    v.Values.TryGetValue(t.TemplateId, out var val) && !string.IsNullOrWhiteSpace(val) ? val + (string.IsNullOrWhiteSpace(t.Unit) ? "" : " " + t.Unit) : "—";
                if (!_vitalsTable)
                    foreach (var v in visits)
                    {
                        var col = StaffUi.Column(6);
                        col.Children.Add(ClinicTheme.Text(Iso(v.Date), "LabelMd"));
                        var grid = StaffUi.Wrap();
                        foreach (var t in defaults)
                        {
                            var one = StaffUi.Column(0);
                            one.Children.Add(ClinicTheme.Text(t.Description, "MutedSm"));
                            one.Children.Add(ClinicTheme.Text(Reading(v, t), "BodyMd"));
                            one.Margin = new Thickness(0, 0, 20, 6);
                            grid.Children.Add(one);
                        }
                        col.Children.Add(grid);
                        var card = StaffUi.Row(col);
                        var bookingId = v.BookingId;
                        card.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await DoctorExtraUi.OpenConsultAsync(bookingId)) });
                        _tab.Children.Add(card);
                    }
                else
                {
                    var g = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
                    g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                    foreach (var _ in visits) g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    g.Add(ClinicTheme.Text("Vital", "LabelMd"), 0, 0);
                    for (var i = 0; i < visits.Count; i++) g.Add(ClinicTheme.Text(Iso(visits[i].Date), "LabelMd"), i + 1, 0);
                    for (var r = 0; r < defaults.Count; r++)
                    {
                        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                        g.Add(Cell(defaults[r].Description), 0, r + 1);
                        for (var i = 0; i < visits.Count; i++) g.Add(Cell(Reading(visits[i], defaults[r])), i + 1, r + 1);
                    }
                    _tab.Children.Add(new ScrollView { Orientation = ScrollOrientation.Horizontal, Content = g });
                }
                break;
            }
            //JJQR - END
            case "prescriptions":
                //JJQR - AI (21 Nov 2026) - web Prescriptions tab (group card, Print Preview, Edit / Copy / Delete only for a referred booking)
                if (ReferredBookingId is { } referred)
                    _tab.Children.Add(StaffUi.Wrap(DoctorExtraUi.Spaced(StaffUi.Btn("New Prescription",
                        () => OpenEditorAsync(referred, null, null)))));
                if (c.Prescriptions.Count > 0) _tab.Children.Add(StaffUi.Wrap(DoctorExtraUi.Spaced(SortSelect(_rxNewest, v => _rxNewest = v)))); //JJQR - AI (21 Nov 2026) web rxSort select // JJQR - END
                if (c.Prescriptions.Count == 0) Empty("No prescriptions recorded yet.");
                foreach (var g in Sorted(c.Prescriptions, g => g.CreatedAt, _rxNewest))
                {
                    var col = StaffUi.Column(4);
                    var created = g.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd");
                    col.Children.Add(ClinicTheme.Text($"{g.Lines.Count} {(g.Lines.Count == 1 ? "medicine" : "medicines")}", "LabelLg"));
                    col.Children.Add(ClinicTheme.Text(created, "MutedSm"));
                    var i = 0;
                    foreach (var l in g.Lines)
                    {
                        i++;
                        col.Children.Add(ClinicTheme.Text($"{i}. {l.GenericName} #{l.Quantity}", "BodyMd"));
                        col.Children.Add(ClinicTheme.Text($"Sig. {l.Dosage} {l.Instruction}", "MutedSm"));
                    }
                    var group = g;
                    var actions = StaffUi.Wrap(DoctorExtraUi.Spaced(StaffUi.Btn("Print", () => PrintPreviewAsync(c.Patient.FullName, group), "SecondaryButton")));
                    if (ReferredBookingId is { } bookingForCopy)
                    {
                        actions.Children.Add(DoctorExtraUi.Spaced(StaffUi.Btn("Edit", () => OpenEditorAsync(group.BookingId, group.GroupId, null), "GhostButton")));
                        actions.Children.Add(DoctorExtraUi.Spaced(StaffUi.Btn("Copy", () => OpenEditorAsync(bookingForCopy, null, group.GroupId), "GhostButton")));
                        actions.Children.Add(DoctorExtraUi.Spaced(StaffUi.DangerGhost("Delete", () => DeleteGroupAsync(group.GroupId))));
                    }
                    col.Children.Add(actions);
                    _tab.Children.Add(StaffUi.Row(col));
                }
                //JJQR - END
                break;
            case "labs":
                Files(c.LabResults, "No lab results yet.");
                break;
            case "documents":
                Files(c.Documents, "No documents yet.");
                break;
            //JJQR - AI (21 Nov 2026) - web Vaccinations: table Vaccine | Dose # | Date | Status ("—" when empty; Administered = success, anything else = warning)
            case "vaccinations":
                if (c.Vaccinations.Count == 0) { Empty("No vaccinations recorded yet."); break; }
                _tab.Children.Add(Table(["Vaccine", "Dose #", "Date", "Status"],
                    c.Vaccinations.Select(v => new View[]
                    {
                        Cell(v.VaccineName), Cell(v.DoseNumber?.ToString() ?? "—"), Cell(v.Date is { } d ? Iso(d) : "—"),
                        StaffUi.Wrap(ClinicTheme.MakeStatusPill(v.Status.ToString())),
                    }).ToList()));
                break;
            //JJQR - END
        }
    }

    //JJQR - AI (21 Nov 2026) - web Lab Results / Documents rows show the title only (no upload line)
    private void Files(IReadOnlyList<ChartFile> files, string empty)
    {
        if (files.Count == 0) Empty(empty);
        foreach (var f in files) _tab.Children.Add(StaffUi.Row(ClinicTheme.Text(f.Title, "BodyMd")));
    }
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
        var lines = string.Join("\n", g.Lines.Select((l, i) => $"{i + 1}. {l.GenericName} #{l.Quantity}\nSig. {l.Dosage} {l.Instruction}"));
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
