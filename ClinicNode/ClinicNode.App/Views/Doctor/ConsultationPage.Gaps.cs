//JJQR - AI (21 Nov 2026) - ConsultationPage gap close, 1:1 with web /doctor/consultation2/[bookingId] and the components it uses:
//   • completed view = the web summary cards (Clinical note, Vital Signs, Diagnoses, Prescriptions, Lab Orders, Vaccinations, Follow-up, fee line + Edit)
//   • Amendment History drawer (audit_logs for the consultation; "Consultation amended" added after each saved amendment)
//   • keyboard shortcuts (Ctrl/⌘+1…9 jump, Ctrl/⌘+S save, Ctrl/⌘+Enter complete, F1 help — "?" would steal typing in fields on desktop)
//   • "Manage" links next to SOAP / diagnosis / certificate templates (opens the Templates page; the draft is saved first)
//   • Prescription: Favorites tab + "Add to Favorites", Pediatric dose helper (mg/kg), Select All + delete selected, same-generic duplicate check
//   • Vital Signs: pediatric normals, growth percentile notes, GrowthChart (weight / length-height / head circ., approx. WHO centiles 0–60 mo)
//   • Lab Orders: ANC lab presets (pregnant patients) · Vaccinations: NIP / EPI schedule with status + "Give today"
//   • live updates: LiveHubSubscription (QueueUpdated / PaymentUpdated for this booking) — view mode reloads, editing never loses typing
//   • ONLINE save → ConsultLocalWriteThrough copies the server rows into SQLite so print never lags
using System.Globalization;
using ClinicApp.Domain; //JJQR - AI (21 Nov 2026) - ClinicClock lives here (CS0103); SyncService uses this full name //JJQR - END
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Live;
using ClinicNode.App.Controls;
using ClinicNode.App.Doctor;
using ClinicNode.App.Views.Shared.Live;
using ClinicNode.Core;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Doctor;

namespace ClinicNode.App.Views.Doctor;

public partial class ConsultationPage
{
    private IServiceProvider? _gapServices;
    private LiveHubSubscription? _liveHub;
    private IReadOnlyList<FavoriteMedicineItem> _favorites = [];
    private IReadOnlyList<PatientVaccination> _vaxHistory = [];
    private bool _isPregnant;
    private List<(DateTimeOffset At, string Author, string Section)> _amendments = [];
    private readonly HashSet<RxLine> _rxSelected = [];
    private bool _rxAddToFav;
    private string _favSearch = "";
    private string _pedsMgPerKg = "", _pedsMaxMg = "", _pedsConcMgMl = "";
    private string? _pedsWeightInput;
    private string _growthTab = "weight";
    private bool _reloadLibraryOnReturn;
    private bool _shortcutsInstalled;

    // ---------- lifecycle hooks (called from ConsultationPage.xaml.cs) ----------
    void GapInit(IServiceProvider services) => _gapServices = services;

    async Task LoadGapDataAsync()
    {
        if (_workspace is null) return;
        try { _favorites = await _lib.ListFavoriteMedicinesAsync(D.DoctorId); } catch { _favorites = []; }
        try { _vaxHistory = await _lib.ListPatientVaccinationsAsync(D.PatientId); } catch { _vaxHistory = []; }
        try { _isPregnant = (await _lib.GetPatientClinicalFlagsAsync(D.PatientId))?.IsPregnant == true; } catch { _isPregnant = false; }
        await LoadAmendmentsAsync();
        //JJQR - AI (21 Nov 2026) - renal cue / drug safety / refill last visit / SOAP specialty default (ConsultationPage.Safety.cs)
        await LoadSafetyDataAsync();
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - ALL-1 banner chips (payment, pregnancy/LMP, G/P, guardian)
        await LoadBannerDataAsync();
        //JJQR - END
    }

    async Task LoadAmendmentsAsync()
    {
        _amendments = [];
        if (_workspace is null || D.ConsultationId is not { } cid || D.Status == ConsultationStatus.Draft) return;
        try
        {
            var rows = await _lib.ListAmendmentHistoryAsync(cid);
            _amendments = rows.Select(r => (r.At, _workspace.DoctorName, string.IsNullOrWhiteSpace(r.Details) ? "Consultation record" : r.Details!)).ToList();
        }
        catch { /* drawer shows "No amendments" */ }
    }

    void GapAppearing()
    {
        InstallShortcuts();
        if (_workspace is null || _gapServices is null) return;
        var bookingId = D.BookingId;
        _liveHub ??= new LiveHubSubscription(_gapServices,
            [ClinicHubEvents.QueueUpdated, ClinicHubEvents.PaymentUpdated],
            OnLiveChangeAsync,
            e => e.BookingId is null || e.BookingId == bookingId);
        _liveHub.Attach();
    }

    void GapDisappearing() => _liveHub?.Detach();

    /// <summary>Back from the Templates page ("Manage"): re-read the template lists.</summary>
    async Task<bool> GapReturnAsync()
    {
        if (!_reloadLibraryOnReturn || _workspace is null) return false;
        _reloadLibraryOnReturn = false;
        await LoadReferenceDataAsync();
        try { _favorites = await _lib.ListFavoriteMedicinesAsync(D.DoctorId); } catch { }
        RenderSection();
        return true;
    }

    async Task OnLiveChangeAsync()
    {
        if (_workspace is null || _busy) return;
        if (_mode == "view" && !Overlay.IsVisible)
        {
            try
            {
                var fresh = await _consults.LoadByBookingAsync(D.BookingId);
                if (fresh is null) return;
                _workspace = fresh;
                InitFormState();
                RenderAll();
            }
            catch { /* keep what is on screen */ }
            return;
        }
        // documenting / amending: never replace what the doctor is typing
        ShowToast("This visit was updated at the front desk (queue or payment). Your notes are unchanged.", null, ++_noticeSeq);
    }

    /// <summary>After any successful save: online → copy the server rows into SQLite in the background (print path awaits its own copy).</summary>
    void AfterSaved(bool amended)
    {
        if (amended) _amendments.Insert(0, (DateTimeOffset.UtcNow, _workspace?.DoctorName ?? "", "Consultation amended"));
        if (_gapServices is { } sp && _workspace is not null)
        {
            var bookingId = D.BookingId;
            _ = Task.Run(() => ConsultLocalWriteThrough.EnsureConsultationAsync(sp, bookingId));
        }
    }

    Task WriteThroughBeforePrintAsync() =>
        _gapServices is { } sp && _workspace is not null ? ConsultLocalWriteThrough.EnsureConsultationAsync(sp, D.BookingId) : Task.CompletedTask;

    // ---------- keyboard shortcuts (web Doctor.md §4) ----------
    static KeyboardAcceleratorModifiers Mod => DeviceInfo.Platform == DevicePlatform.MacCatalyst ? KeyboardAcceleratorModifiers.Cmd : KeyboardAcceleratorModifiers.Ctrl;
    static string ModName => DeviceInfo.Platform == DevicePlatform.MacCatalyst ? "⌘" : "Ctrl+";

    void InstallShortcuts()
    {
        if (_shortcutsInstalled) return;
        _shortcutsInstalled = true;
        var menu = new MenuBarItem { Text = "Consultation" };
        MenuFlyoutItem Item(string text, string key, KeyboardAcceleratorModifiers mods, Action act)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = mods, Key = key });
            item.Clicked += (_, _) => act();
            return item;
        }
        for (var n = 1; n <= 9; n++)
        {
            var k = n;
            menu.Add(Item($"Jump to section {k}", k.ToString(CultureInfo.InvariantCulture), Mod, () => JumpToVisible(k)));
        }
        menu.Add(Item("Save", "S", Mod, () => { if (_mode != "view") _ = SaveAsync(goNext: false); }));
        menu.Add(Item("Complete Consultation", "Enter", Mod, () => { if (_mode == "complete" && CanComplete) OnComplete(null, EventArgs.Empty); }));
        menu.Add(Item("Keyboard Shortcuts", "F1", KeyboardAcceleratorModifiers.None, ShowShortcutHelp));
        MenuBarItems.Add(menu);
    }

    /// <summary>Numbers follow the visible section pills (1 = first visible, fee last), not the raw section index.</summary>
    void JumpToVisible(int number)
    {
        if (_workspace is null || _mode == "view") return;
        var order = ConsultRules.VisibleOrder(_showMore, _section);
        var nav = order.Where(i => i != ConsultSteps.FeeSectionIndex).ToList();
        if (order.Contains(ConsultSteps.FeeSectionIndex)) nav.Add(ConsultSteps.FeeSectionIndex);
        if (number - 1 < nav.Count) GoTo(nav[number - 1]);
    }

    void ShowShortcutHelp()
    {
        var rows = new (string Keys, string Description)[]
        {
            ($"{ModName}1 – {ModName}9", "Jump to that numbered visible section"),
            ($"{ModName}S", "Save Draft (complete mode) or save progress in amend (stays in amend)"),
            ($"{ModName}Enter", "Complete Consultation (when required fields are valid)"),
            ("F1", "Show this help"),
        };
        ShowOverlay("Keyboard Shortcuts", rows.Select(r => (View)Row(ClinicTheme.Text(r.Keys, "LabelMd"), ClinicTheme.Text(r.Description, "BodyMd"))),
            ("Close", HideOverlay, "PrimaryButton"));
    }

    // ---------- "Manage" template links ----------
    View ManageLink()
    {
        return Btn("Manage", async () =>
        {
            if (_mode != "view" && Dirty) await SaveAsync(goNext: false, silent: true);
            _reloadLibraryOnReturn = true;
            try { await Shell.Current.GoToAsync("//templates"); }
            catch (Exception ex) { _reloadLibraryOnReturn = false; Error(ex.Message); }
        }, "GhostButton");
    }

    // ---------- completed view: the web summary cards ----------
    void RenderRecordView()
    {
        var w = _workspace!;
        StepBar.IsVisible = false;
        SectionHost.Children.Clear();
        SectionHost.IsEnabled = true;
        var ph = CultureInfo.GetCultureInfo("en-PH");
        string Date(DateOnly d) => d.ToString("MMM d, yyyy", ph);

        var ident = new List<string>();
        if (!string.IsNullOrWhiteSpace(w.PatientCode)) ident.Add(w.PatientCode);
        if (!string.IsNullOrWhiteSpace(w.PatientSex)) ident.Add(w.PatientSex!);
        if (w.PatientDateOfBirth is { } dob) ident.Add($"{Date(dob)} ({AgeOn(dob, ClinicClock.Today)}y)");
        var head = new VerticalStackLayout { Spacing = 2 };
        head.Children.Add(ClinicTheme.Text("Consultation record", "HeadlineMd"));
        if (ident.Count > 0) head.Children.Add(ClinicTheme.Text(string.Join(" · ", ident), "LabelSm"));
        head.Children.Add(ClinicTheme.Text($"Completed {Date(w.AppointmentDate)} by {w.DoctorName}", "LabelMd"));
        var actions = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, JustifyContent = Microsoft.Maui.Layouts.FlexJustify.End };
        actions.Children.Add(Btn("History", ShowAmendmentHistory));
        //JJQR - AI (21 Nov 2026) - a draft opened with &mode=view edits as a normal draft (complete), never as an amendment
        actions.Children.Add(Btn("Edit Consultation", () =>
        {
            if (IsClosed) { OnAmend(null, EventArgs.Empty); return; }
            _mode = "complete";
            StartAutosave();
            RenderAll();
        }, "PrimaryButton"));
        //JJQR - END
        SectionHost.Children.Add(Row(head, actions));

        string Or(string? v) => string.IsNullOrWhiteSpace(v) ? "Not recorded." : v.Trim();
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - bold "Label:" lines; vitals as a label/value grid with "Out of range"
        SectionHost.Children.Add(Card(
            ClinicTheme.Text("Clinical note", "HeadlineMd"),
            Strong("Chief Complaint", Or(D.ChiefComplaint)),
            Strong("Subjective", Or(D.Subjective)),
            Strong("Objective", Or(D.Objective)),
            Strong("Assessment", Or(D.Assessment)),
            Strong("Plan", Or(D.Plan))));

        var vit = new List<View> { ClinicTheme.Text("Vital Signs", "HeadlineSm") };
        if (D.Vitals.Any(v => !string.IsNullOrWhiteSpace(v.Value)))
        {
            var grid = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var v in D.Vitals)
            {
                var cell = new VerticalStackLayout { Spacing = 2, WidthRequest = 200, Margin = new Thickness(0, 0, 12, 8) };
                cell.Children.Add(ClinicTheme.Text(v.Description, "MutedSm"));
                cell.Children.Add(ClinicTheme.Text(string.IsNullOrWhiteSpace(v.Value) ? "Not recorded" : v.Value + (string.IsNullOrEmpty(v.Unit) ? "" : " " + v.Unit), "BodyMd"));
                if (VitalRanges.OutOfRange(v.FormKey, v.Value))
                {
                    var warn = ClinicTheme.Text("⚠ Out of range", "LabelSm");
                    warn.TextColor = ClinicTheme.Color("StatusWarning");
                    cell.Children.Add(warn);
                }
                grid.Children.Add(cell);
            }
            vit.Add(grid);
        }
        else vit.Add(ClinicTheme.Text("No vitals recorded.", "MutedBody"));
        SectionHost.Children.Add(Card(vit.ToArray()));
        //JJQR - END

        SectionHost.Children.Add(Card(ClinicTheme.Text("Diagnoses", "HeadlineSm"), ClinicTheme.Text(D.Diagnoses.Count > 0
            ? string.Join(", ", D.Diagnoses.Select(d => $"{DxText(d)} ({d.Type})"))
            : "No diagnoses recorded.", "MutedBody")));

        var rx = new List<View> { ClinicTheme.Text("Prescriptions", "HeadlineSm") };
        if (D.Prescriptions.Count > 0)
            rx.AddRange(D.Prescriptions.Select((r, i) => (View)ClinicTheme.Text($"{i + 1}. {r.GenericName} #{r.Quantity} — Sig. {r.Dosage} {r.Instruction}".TrimEnd(), "MutedBody")));
        else rx.Add(ClinicTheme.Text("No prescriptions recorded.", "MutedBody"));
        SectionHost.Children.Add(Card(rx.ToArray()));

        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web lab line uses reason first //JJQR - END
        var labs = new List<View> { ClinicTheme.Text("Lab Orders", "HeadlineSm") };
        if (D.LabOrders.Count > 0)
            labs.AddRange(D.LabOrders.Select(l => (View)ClinicTheme.Text($"{l.TestName} — {l.Reason ?? l.ClinicalIndication}{(string.IsNullOrWhiteSpace(l.SpecimenType) ? "" : $" ({l.SpecimenType})")}", "MutedBody")));
        else labs.Add(ClinicTheme.Text("No lab orders recorded.", "MutedBody"));
        SectionHost.Children.Add(Card(labs.ToArray()));

        var vax = new List<View> { ClinicTheme.Text("Vaccinations", "HeadlineSm") };
        if (D.Vaccinations.Count > 0)
            vax.AddRange(D.Vaccinations.Select(v => (View)ClinicTheme.Text($"{v.VaccineName} — Dose #{v.DoseNumber}", "MutedBody")));
        else vax.Add(ClinicTheme.Text("No vaccinations recorded.", "MutedBody"));
        SectionHost.Children.Add(Card(vax.ToArray()));

        SectionHost.Children.Add(Card(ClinicTheme.Text("Follow-up", "HeadlineSm"), ClinicTheme.Text(ConsultClinicalAids.FollowUpViewLine(D, Date), "MutedBody")));

        // stored decision; no typed amount = the computed total that was charged
        var amount = D.PfDecision == "Charge" ? D.PfAmount ?? (w.ComputedFee > 0 ? w.ComputedFee : null) : null;
        var feeLine = ConsultClinicalAids.ProfessionalFeeLine(D.PfDecision, amount, D.PfWaiveReason, PreviewFee().Total);
        SectionHost.Children.Add(Row(ClinicTheme.Text(feeLine, "BodyMd"), Btn("Edit", OpenProfessionalFeeForEdit, "GhostButton")));

        FeePreview.Text = PreviewFee().Summary;
        UpdateFooter();
        PrevButton.IsVisible = false;
        NextButton.IsVisible = false;
    }

    /// <summary>web openProfessionalFeeForEdit: completed summary "Edit" opens the fee step in amend.</summary>
    void OpenProfessionalFeeForEdit()
    {
        HideOverlay();
        _mode = "amend";
        _autosave?.Stop();
        _section = ConsultSteps.FeeSectionIndex;
        RenderAll();
    }

    void ShowAmendmentHistory()
    {
        var zone = ManilaZone();
        var body = new List<View>();
        if (_amendments.Count == 0) body.Add(ClinicTheme.Text("No amendments have been made to this record.", "MutedBody"));
        foreach (var a in _amendments)
            body.Add(Card(
                ClinicTheme.Text($"{TimeZoneInfo.ConvertTime(a.At, zone).ToString("MMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-PH"))} PHT — {a.Author}", "LabelLg"),
                ClinicTheme.Text($"Changed: {a.Section}", "BodyMd")));
        ShowOverlay("Amendment History", body, ("Close", HideOverlay, "SecondaryButton"));
    }

    // ---------- Vital Signs: pediatric aids ----------
    int? AgeYears() => _workspace?.PatientDateOfBirth is { } dob ? AgeOn(dob, ClinicClock.Today) : null;
    int? AgeMonthsNow() => ConsultClinicalAids.AgeMonths(_workspace?.PatientDateOfBirth, ClinicClock.Today);

    void RenderPedsVitals()
    {
        if (ConsultClinicalAids.PedsVitalRangeNote(AgeYears()) is { } note) SectionHost.Children.Add(ClinicTheme.Text(note, "MutedSm"));
        var notes = ConsultClinicalAids.GrowthPercentileNotes(_workspace!.PatientSex, AgeMonthsNow(), D.Vitals);
        if (notes.Count > 0)
        {
            var l = ClinicTheme.Text(string.Join(" · ", notes), "LabelSm");
            l.TextColor = ClinicTheme.Color("Primary");
            SectionHost.Children.Add(l);
        }
        if (AgeYears() is < 18 && AgeMonthsNow() is { } mo) SectionHost.Children.Add(GrowthChartView(mo));
    }

    View GrowthChartView(int ageMonths)
    {
        var tabs = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (id, label) in new[] { ("weight", "Weight"), ("height", "Length/Height"), ("hc", "Head circ.") })
        {
            var tab = id;
            tabs.Children.Add(Pill(label, _growthTab == tab, () => { _growthTab = tab; RenderSection(); }));
        }
        var curves = ConsultClinicalAids.GetCurvePoints(_workspace!.PatientSex, _growthTab, 60);
        if (curves.Count == 0) return ClinicTheme.Text("Growth chart not available for this measure.", "LabelSm");
        var key = _growthTab == "hc" ? new[] { "head", "hc" } : new[] { _growthTab };
        var value = ConsultClinicalAids.ParseFloat(ConsultClinicalAids.VitalValue(D.Vitals, key));
        var drawable = new GrowthChartDrawable(curves, ageMonths, double.IsFinite(value) ? value : null, ClinicTheme.Color("Primary"), ClinicTheme.Color("OnSurfaceVariant"));
        var chart = new GraphicsView { Drawable = drawable, HeightRequest = 220, MaximumWidthRequest = 640, HorizontalOptions = LayoutOptions.Fill };
        return Card(tabs, chart, ClinicTheme.Text("Approx. WHO centiles (0–60 mo). Amber dots = patient measurements.", "LabelSm"));
    }

    /// <summary>web GrowthChart.tsx (420×220 viewBox): p3/p15/p50/p85/p97 lines, P50 label, x labels, amber patient dot.</summary>
    private sealed class GrowthChartDrawable(IReadOnlyList<GrowthCurvePoint> curves, int ageMonths, double? patientValue, Color ink, Color muted) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF rect)
        {
            const float W = 420, H = 220, L = 44, R = 12, T = 16, B = 32;
            var scale = Math.Min(rect.Width / W, rect.Height / H);
            canvas.SaveState();
            canvas.Translate(rect.X, rect.Y);
            canvas.Scale(scale, scale);
            var vals = curves.SelectMany(c => new[] { c.P3, c.P97 }).ToList();
            if (patientValue is { } pv) vals.Add(pv);
            double minAge = 0, maxAge = Math.Max(60, ageMonths);
            var minY = vals.Min() * 0.92; var maxY = vals.Max() * 1.08;
            float innerW = W - L - R, innerH = H - T - B;
            float X(double mo) => (float)(L + (mo - minAge) / (maxAge - minAge == 0 ? 1 : maxAge - minAge) * innerW);
            float Y(double v) => (float)(T + innerH - (v - minY) / (maxY - minY == 0 ? 1 : maxY - minY) * innerH);
            canvas.FontColor = muted;
            canvas.FontSize = 10;
            foreach (var mo in new[] { minAge, maxAge / 2, maxAge })
                canvas.DrawString($"{Math.Round(mo, MidpointRounding.AwayFromZero)} mo", X(mo) - 20, H - 18, 40, 12, HorizontalAlignment.Center, VerticalAlignment.Center);
            foreach (var pick in new Func<GrowthCurvePoint, double>[] { c => c.P3, c => c.P15, c => c.P50, c => c.P85, c => c.P97 })
            {
                var path = new PathF();
                for (var i = 0; i < curves.Count; i++)
                {
                    var p = new PointF(X(curves[i].AgeMonths), Y(pick(curves[i])));
                    if (i == 0) path.MoveTo(p); else path.LineTo(p);
                }
                canvas.StrokeColor = ink.WithAlpha(0.35f);
                canvas.StrokeSize = 0.75f;
                canvas.DrawPath(path);
            }
            // p50 on top, stronger
            var mid = new PathF();
            for (var i = 0; i < curves.Count; i++)
            {
                var p = new PointF(X(curves[i].AgeMonths), Y(curves[i].P50));
                if (i == 0) mid.MoveTo(p); else mid.LineTo(p);
            }
            canvas.StrokeColor = ink.WithAlpha(0.85f);
            canvas.StrokeSize = 1.5f;
            canvas.DrawPath(mid);
            canvas.FontColor = ink;
            canvas.DrawString("P50", L + 4, T + 2, 30, 12, HorizontalAlignment.Left, VerticalAlignment.Center);
            if (patientValue is { } v)
            {
                canvas.FillColor = Color.FromArgb("#D97706"); // amber-600
                canvas.FillCircle(X(ageMonths), Y(v), 4);
                canvas.StrokeColor = Colors.White;
                canvas.StrokeSize = 1.5f;
                canvas.DrawCircle(X(ageMonths), Y(v), 4);
            }
            canvas.RestoreState();
        }
    }

    // ---------- Prescription: pediatric dose helper, bulk delete, Favorites ----------
    void RenderPedsDoseHelper()
    {
        if (AgeYears() is not < 18) return;
        var vitalsWeight = ConsultClinicalAids.ParseFloat(ConsultClinicalAids.VitalValue(D.Vitals, "weight"));
        string EffectiveWeight() => _pedsWeightInput is { Length: > 0 } typed ? typed
            : double.IsFinite(vitalsWeight) ? vitalsWeight.ToString(CultureInfo.InvariantCulture) : "";
        var result = ClinicTheme.Text("", "LabelSm");
        void Recalc()
        {
            var weight = ConsultClinicalAids.ParseFloat(new string(EffectiveWeight().Where(c => char.IsDigit(c) || c == '.').ToArray()));
            var mgPerKg = ConsultClinicalAids.ParseFloat(_pedsMgPerKg);
            double? max = string.IsNullOrWhiteSpace(_pedsMaxMg) ? null : ConsultClinicalAids.ParseFloat(_pedsMaxMg);
            var conc = ConsultClinicalAids.ParseFloat(_pedsConcMgMl);
            if (!(double.IsFinite(mgPerKg) && double.IsFinite(weight) && weight > 0)) { result.IsVisible = false; return; }
            var (mg, capped) = ConsultClinicalAids.MgPerKgDose(mgPerKg, weight, max);
            double? ml = double.IsFinite(conc) && conc > 0 ? ConsultClinicalAids.MlFromConcentration(mg, conc) : null;
            result.Text = $"≈ {mg.ToString(CultureInfo.InvariantCulture)} mg{(capped ? " (max cap applied)" : "")}{(ml is { } m ? $" · ≈ {m.ToString(CultureInfo.InvariantCulture)} mL" : "")}";
            result.IsVisible = true;
        }
        Entry Field(string label, string placeholder, string value, Action<string> set, List<View> target)
        {
            var e = new Entry { Text = value, Placeholder = placeholder, WidthRequest = 150 };
            e.TextChanged += (_, a) => { set(a.NewTextValue ?? ""); Recalc(); };
            target.Add(new VerticalStackLayout { Spacing = 2, Margin = new Thickness(0, 0, 12, 4), Children = { ClinicTheme.Text(label, "LabelMd"), e } });
            return e;
        }
        var fields = new List<View>();
        Field("Dose (mg/kg)", "mg/kg", _pedsMgPerKg, v => _pedsMgPerKg = v, fields);
        Field("Weight (kg)", "Weight kg", EffectiveWeight(), v => _pedsWeightInput = v, fields);
        Field("Maximum dose (mg, optional)", "Max mg (opt)", _pedsMaxMg, v => _pedsMaxMg = v, fields);
        Field("Concentration (mg/mL, optional)", "mg/mL (opt)", _pedsConcMgMl, v => _pedsConcMgMl = v, fields);
        var flex = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var f in fields) flex.Children.Add(f);
        Recalc();
        var box = Card(ClinicTheme.Text("Pediatric dose helper (mg/kg)", "LabelLg"), flex, result);
        if (box is Border b) b.BackgroundColor = ClinicTheme.Color("SurfaceContainerLow");
        SectionHost.Children.Add(box);
    }

    void RenderRxBulkBar()
    {
        _rxSelected.RemoveWhere(r => !D.Prescriptions.Contains(r));
        if (D.Prescriptions.Count == 0) return;
        var all = new CheckBox { IsChecked = _rxSelected.Count == D.Prescriptions.Count };
        all.CheckedChanged += (_, e) =>
        {
            if (e.Value) foreach (var r in D.Prescriptions) _rxSelected.Add(r);
            else _rxSelected.Clear();
            RenderSection();
        };
        var right = _rxSelected.Count > 0
            ? Btn($"Delete selected ({_rxSelected.Count})", () =>
            {
                D.Prescriptions.RemoveAll(_rxSelected.Contains);
                _rxSelected.Clear();
                RenderSection(); Changed();
            }, "DangerButton")
            : (View)new ContentView();
        SectionHost.Children.Add(Row(WrapCheck(all, "Select All"), right));
    }

    View RxSelectable(RxLine line, View content)
    {
        var box = new CheckBox { IsChecked = _rxSelected.Contains(line), VerticalOptions = LayoutOptions.Start };
        box.CheckedChanged += (_, e) => { if (e.Value) _rxSelected.Add(line); else _rxSelected.Remove(line); RenderSection(); };
        //JJQR - AI (Nov 21, 2026) - clicking the medicine text selects it too, not just the circle
        box.InputTransparent = true;
        var row = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } }; // Grid (not a stack) so long text wraps inside the column
        box.VerticalOptions = LayoutOptions.Start;
        row.Add(box, 0, 0);
        row.Add(content, 1, 0);
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => box.IsChecked = !box.IsChecked) });
        return row;
        //JJQR - END
    }

    /// <summary>web isDuplicate (F-019): same generic regardless of dose.</summary>
    bool RxIsDuplicate(string genericName) =>
        D.Prescriptions.Any(i => string.Equals(i.GenericName.Trim(), genericName.Trim(), StringComparison.OrdinalIgnoreCase));

    void RenderRxFavorites()
    {
        var search = new Entry { Text = _favSearch, Placeholder = "Search Favorite" };
        var list = new VerticalStackLayout { Spacing = 6 };
        var err = new Label { Style = ClinicTheme.Style("ErrorText"), IsVisible = false };
        void Fill()
        {
            list.Children.Clear();
            var hits = _favorites.Where(f => f.GenericName.Contains(_favSearch, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count == 0) { list.Children.Add(ClinicTheme.Text("No favorites yet.", "MutedBody")); return; }
            foreach (var f in hits)
            {
                var fav = f;
                list.Children.Add(Btn(fav.GenericName, () =>
                {
                    //JJQR - AI (21 Nov 2026) - web handleFavoriteAdd order: allergy → drug safety → duplicate
                    if (!RxSafeToAdd(fav.GenericName)) return;
                    //JJQR - END
                    if (RxIsDuplicate(fav.GenericName)) { err.Text = "Item already exist."; err.IsVisible = true; return; }
                    D.Prescriptions.Add(new RxLine { MedicineId = fav.MedicineId, GenericName = fav.GenericName, Dosage = fav.Dosage, Quantity = fav.Quantity, Instruction = fav.Instruction, Route = "PO" });
                    D.NoMedsConfirmed = false;
                    RenderSection(); Changed();
                }));
            }
        }
        search.TextChanged += (_, e) => { _favSearch = e.NewTextValue ?? ""; Fill(); };
        SectionHost.Children.Add(search);
        SectionHost.Children.Add(err);
        SectionHost.Children.Add(list);
        Fill();
    }

    View RxAddToFavoritesCheck()
    {
        var box = new CheckBox { IsChecked = _rxAddToFav };
        box.CheckedChanged += (_, e) => _rxAddToFav = e.Value;
        return WrapCheck(box, "Add to Favorites");
    }

    async Task AddFavoriteAsync(RxLine line)
    {
        _rxAddToFav = false;
        try
        {
            var saved = await _lib.AddFavoriteMedicineAsync(D.DoctorId, new FavoriteMedicineItem(Guid.Empty, line.MedicineId, line.GenericName, line.Dosage, line.Quantity, line.Instruction));
            _favorites = _favorites.Append(saved).ToList();
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    // ---------- Lab Orders: ANC presets ----------
    void RenderAncPresets()
    {
        if (!_isPregnant || _labTests.Count == 0) return;
        SectionHost.Children.Add(ClinicTheme.Text("ANC lab presets", "LabelSm"));
        var row = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var p in ConsultClinicalAids.AncPresets)
        {
            var preset = p;
            row.Children.Add(Btn(preset.Label, () => ApplyAncPreset(preset.Id)));
        }
        SectionHost.Children.Add(row);
    }

    void ApplyAncPreset(string presetId)
    {
        var (matches, missing) = ConsultClinicalAids.MatchLabTests(_labTests, presetId);
        foreach (var t in matches)
            if (!D.LabOrders.Any(l => l.LabTestId == t.Id)) D.LabOrders.Add(new LabLine { LabTestId = t.Id, TestName = t.Name });
        foreach (var name in missing)
            if (!D.LabOrders.Any(l => l.TestName.Trim().Equals(name, StringComparison.OrdinalIgnoreCase))) D.LabOrders.Add(new LabLine { TestName = name });
        RenderSection(); Changed();
    }

    // ---------- Vaccinations: NIP / EPI schedule ----------
    void RenderNipBlock()
    {
        if (AgeMonthsNow() is not { } mo) return;
        var given = _vaxHistory.Select(v => v.VaccineName).Concat(D.Vaccinations.Select(v => v.VaccineName)).ToList();
        var status = ConsultClinicalAids.ComputeNipStatus(mo, given);
        var due = ConsultClinicalAids.NipDueForAgeMonths(mo);
        if (status.Count > 0)
        {
            var items = new List<View> { ClinicTheme.Text("NIP / EPI schedule", "LabelMd") };
            foreach (var r in status)
            {
                var row = r;
                var pill = ClinicTheme.Text(row.Status, "LabelSm");
                pill.TextColor = ClinicTheme.Color(row.Status switch { "given" or "due" => "Primary", "upcoming" => "OnSurfaceVariant", _ => "StatusWarning" });
                var right = new HorizontalStackLayout { Spacing = 8, Children = { pill } };
                pill.VerticalOptions = LayoutOptions.Center;
                if (row.Status is "due" or "overdue" or "catch-up") right.Children.Add(Btn("Give today", () => GiveNipVaccineToday(row.Label)));
                items.Add(Row(ClinicTheme.Text($"{row.Label}  (due ~{row.AgeMonthsDue.ToString("0.##", CultureInfo.InvariantCulture)} mo)", "LabelSm"), right));
            }
            var card = Card(items.ToArray());
            if (card is Border b) b.BackgroundColor = ClinicTheme.Color("SurfaceContainerLow");
            SectionHost.Children.Add(card);
        }
        else if (due.Count > 0)
        {
            var items = new List<View> { ClinicTheme.Text("NIP / EPI due by age (checklist)", "LabelMd") };
            items.AddRange(due.Select(r => (View)ClinicTheme.Text($"{r.AgeLabel}: {string.Join("; ", r.Vaccines)}", "LabelSm")));
            SectionHost.Children.Add(Card(items.ToArray()));
        }
    }

    void GiveNipVaccineToday(string label)
    {
        if (D.Vaccinations.Any(v => v.VaccineName.Trim().Equals(label, StringComparison.OrdinalIgnoreCase))) return;
        D.Vaccinations.Add(new VaccinationLine { VaccineName = label, DoseNumber = ConsultClinicalAids.InferDoseNumberFromLabel(label) });
        RenderSection(); Changed();
    }
}
//JJQR - END
