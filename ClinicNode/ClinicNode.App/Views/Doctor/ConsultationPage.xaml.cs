// JJQR - AI (Nov 21, 2026) - consultation workspace (functional port of consultation2).
//JJQR - AI (21 Nov 2026) - rewritten 1:1 with web /doctor/consultation2/[bookingId] (page.tsx + PrescriptionForm + RxTimingFields + SoapFieldToolbar):
//   steps in web order [Clinical notes, Vital Signs, Prescription, Lab Orders, Follow-up, Medical Certificate, (More → Vaccinations), Professional Fee Decision];
//   SOAP "Use template…" with Append/Replace preview + 15 s Undo, "Save as Template", per-field "Insert phrase…" (+ save current text as phrase);
//   built-in SOAP/phrase/certificate templates + the medicine catalogue via IConsultationLibraryService (online-first, local fallback merges BuiltInTemplateCatalog);
//   vitals locked in view/amend; allergy editor wired (NKDA / add / remove); Rx pad (name + quantity required, ≥3-letter catalogue search, timing pills, duration, PRN);
//   labs (catalogue checkboxes + additional tests), vaccinations (route/site/lot/expiry/manufacturer), follow-up radios (Schedule date / PRN / No follow-up);
//   medical certificate (§16.8 Form 2 fields, templates, Issue & Print / Save without printing / Remove, come-back-date checks) which ticks the med-cert fee by itself;
//   Professional Fee Decision LAST: Charge follows the computed total unless the doctor types an amount ("Use ₱x" to go back), Waive needs ≥ 5 characters;
//   footer Prev / Next (navigate only) / Save & next / Review & complete → Completion Checklist with what was entered; auto-save every 30 s while documenting.
using System.Globalization;
using System.Text.Json;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.Core;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Doctor;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Print;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Views.Doctor;

[QueryProperty(nameof(BookingIdText), "bookingId")]
[QueryProperty(nameof(ModeText), "mode")]
public partial class ConsultationPage : ContentPage
{
    private readonly IConsultationService _consults;
    private readonly IPrintDocumentFactory _printDocs;
    private readonly IPrinter _printer;
    private readonly IDoctorCatalogService _catalog;
    private readonly IConsultationLibraryService _lib;

    private ConsultationWorkspace? _workspace;
    private ConsultationDraft D => _workspace!.Draft;
    private string _mode = "complete"; // complete | view | amend
    private int _section;
    private bool _showMore;
    private bool _completeAttempted;
    private bool _busy;
    private VisitType _bookedVisitType;

    // reference data
    private IReadOnlyList<PastVisit> _history = [];
    private IReadOnlyList<SoapTemplateItem> _soapTemplates = [];
    private IReadOnlyList<SoapPhraseItem> _phrases = [];
    private IReadOnlyList<DiagnosisTemplateItem> _dxTemplates = [];
    private IReadOnlyList<RxTemplateItem> _rxTemplates = [];
    private IReadOnlyList<CertTemplateItem> _certTemplates = [];
    private IReadOnlyList<MedicineHit> _medicines = [];
    private IReadOnlyList<LabTestItem> _labTests = [];
    private IReadOnlyList<PatientAllergy> _allergies = [];
    private ClinicSetting _settings = new();
    private DoctorCredentials? _doctor;
    private bool _patientAddressMissing;
    private readonly Dictionary<string, string> _icdNames = new();

    // professional fee (web pfAmount / pfAmountEdited / pfOverrideBase / medCertManual)
    private string _pfTyped = "";
    private bool _pfEdited;
    private decimal? _pfOverrideBase;
    private bool? _medCertManual;

    // SOAP template undo (15 s) and certificate helpers
    private (int Id, string Title, SoapValues Before, SoapValues Applied)? _undo;
    private int _noticeSeq;
    private bool _mcDxDirty;
    private bool _mcComeBackStale;

    // auto-save (web: every 30 s while documenting)
    private string _savedSnapshot = "";
    private string? _draftSavedAt;
    private IDispatcherTimer? _autosave;

    public string BookingIdText { get; set; } = "";
    public string ModeText { get; set; } = "";
    private bool IsClosed => _workspace is { } w && w.Draft.Status != ConsultationStatus.Draft;
    private bool ReadOnly => _mode == "view";

    readonly IServiceProvider _serviceProvider;

    public ConsultationPage(IConsultationService consults, IPrintDocumentFactory printDocs, IPrinter printer, IDoctorCatalogService catalog, IServiceProvider services)
    {
        InitializeComponent();
        ClinicNode.App.Views.Shared.TopBarOnPushed.Attach(this, services); //JJQR - AI (Nov 22, 2026) - top bar (ping) on the consultation, set BEFORE the page is shown //JJQR - END
        Overlay.SizeChanged += (_, _) => { if (Overlay.Width > 0) OverlayCard.WidthRequest = Math.Max(420, Math.Min(880, Overlay.Width - 56)); }; //JJQR - AI (Nov 21, 2026) - dialogs get a proper width //JJQR - END
        _consults = consults;
        _printDocs = printDocs;
        _printer = printer;
        _catalog = catalog;
        _serviceProvider = services; //JJQR - AI (Nov 21, 2026) - for the live Patient history //JJQR - END
        //JJQR - AI (Nov 22, 2026) - QA high #5: the top-left Back arrow threw away unsaved amend edits without asking (the web asks "Discard changes?"). Amend mode is manual-save only, so Back asks first when something changed.
        Shell.SetBackButtonBehavior(this, new BackButtonBehavior { Command = new Command(async () => await OnBackRequestedAsync()) });
        //JJQR - END
        // registered by DoctorModule.AddDoctorModule(); until that line is added the local implementation is used directly
        _lib = services.GetService<IConsultationLibraryService>() ?? ActivatorUtilities.CreateInstance<ConsultationLibraryService>(services);
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        GapInit(services);
        //JJQR - END
        var ink = ClinicTheme.Color("OnSurface");
        foreach (var btn in new[] { PrintRxButton, PrintLabsButton, PrintCertButton, PrintSlipButton })
        {
            btn.ImageSource = ClinicIcons.FontImage("print", ClinicIcons.SizeSm, ink);
            btn.ContentLayout = new Button.ButtonContentLayout(Button.ButtonContentLayout.ImagePosition.Left, 8);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        //JJQR - AI (Nov 25, 2026) - code review: back from another page (e.g. the patient chart, where files may have been uploaded): reload the Lab Orders "Uploaded results" panel
        // (only after the chart: a closed pop-up also raises OnAppearing, and redrawing then would wipe what is being typed)
        if (_workspace is not null)
        {
            var fromChart = _backFromChart;
            _backFromChart = false;
            if (fromChart) ForgetVisitFiles();
            var redrawn = await GapReturnAsync();
            if (fromChart && !redrawn && _section == 4) RenderSection();
            GapAppearing(); StartAutosave(); return;
        }
        //JJQR - END
        //JJQR - END
        if (!Guid.TryParse(BookingIdText, out var bookingId)) { Error("Missing booking id."); return; }
        //JJQR - AI (Nov 22, 2026) - while the consultation loads (~1 s) the screen showed empty panels and every Print button; hide them and say what is happening //JJQR - END
        foreach (var b in new[] { PrintRxButton, PrintLabsButton, PrintCertButton, PrintSlipButton }) b.IsVisible = false;
        SectionHost.Children.Clear();
        //JJQR - AI (Nov 22, 2026) - spinner next to the loading text
        SectionHost.Children.Add(new HorizontalStackLayout { Spacing = 10, Margin = new Thickness(8, 24), Children = { new ActivityIndicator { IsRunning = true, WidthRequest = 22, HeightRequest = 22, Color = ClinicTheme.Color("Primary") }, new Label { Text = "Loading consultation…", VerticalOptions = LayoutOptions.Center, TextColor = ClinicTheme.Color("OnSurfaceVariant") } } });
        //JJQR - END
        try
        {
            _workspace = await _consults.LoadByBookingAsync(bookingId);
            //JJQR - AI (21 Nov 2026) - honor &mode=complete|view|amend from Appointment detail (web initialMode = searchParams "mode"):
            // draft → complete, or view when asked; finished (Completed/Amended) → amend when asked, otherwise read-only view
            // (no ?mode → view, as on the web; mode=complete on a finished record also opens view so it is never re-completed)
            var requested = (ModeText ?? "").Trim().ToLowerInvariant();
            if (D.Status == ConsultationStatus.Draft) _mode = requested == "view" ? "view" : "complete";
            else _mode = requested == "amend" ? "amend" : "view";
            //JJQR - END
            //JJQR - AI (Nov 22, 2026) - owner: the screen took ~10 s to open. About 20 independent lists were fetched ONE AFTER ANOTHER (each a full round trip to a far server). Now they are all requested at the same time.
            await Task.WhenAll(LoadReferenceDataAsync(), LoadGapDataAsync());
            //JJQR - END
            InitFormState();
            _section = ConsultRules.VisibleOrder(false, 0)[0];
            RenderAll();
            StartAutosave();
            //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
            GapAppearing();
            //JJQR - END
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _autosave?.Stop();
        //JJQR - AI (Nov 22, 2026) - leaving (Back / sidebar) with unsaved typing silently threw it away unless the 30 s autosave had fired: save the draft on the way out //JJQR - END
        var leaving = _mode == "complete" && !_busy && Dirty ? SaveAsync(goNext: false, silent: true) : Task.CompletedTask;
        //JJQR - AI (Nov 22, 2026) - stale-form protection: the visit session ends once that last save is done (and only if the visit was not opened again meanwhile)
        var visitId = D.BookingId; var visitGen = ClinicNode.Api.Http.VisitVersions.GenerationOf(visitId);
        _ = leaving.ContinueWith(_ => ClinicNode.Api.Http.VisitVersions.End(visitId, visitGen), TaskScheduler.Default);
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        GapDisappearing();
        //JJQR - END
    }

    async Task LoadReferenceDataAsync()
    {
        var doctorId = D.DoctorId;
        async Task Try(Func<Task> load) { try { await load(); } catch (Exception ex) { StatusLabel.Text = "Some reference lists did not load: " + ex.Message; } }
        //JJQR - AI (Nov 22, 2026) - all at once (see OnAppearing)
        var tasks = new List<Task>
        {
            Try(async () => _settings = await _lib.GetClinicSettingsAsync()),
            Try(async () => _soapTemplates = await _lib.ListSoapTemplatesAsync(doctorId)),
            Try(async () => _phrases = await _lib.ListSoapPhrasesAsync(doctorId)),
            Try(async () => _certTemplates = await _lib.ListCertTemplatesAsync(doctorId)),
            Try(async () => _dxTemplates = await _lib.ListDiagnosisTemplatesAsync(doctorId)),
            Try(async () => _rxTemplates = await _lib.ListRxTemplatesAsync(doctorId)),
            Try(async () => _medicines = await _lib.ListMedicinesAsync()),
            Try(async () => _labTests = await _lib.ListLabTestsAsync()),
            Try(async () => _doctor = await _lib.GetDoctorCredentialsAsync(doctorId)),
            Try(async () => _allergies = await _lib.ListAllergiesAsync(D.PatientId)),
            Try(async () => _history = await LoadLiveHistoryAsync()),
            Try(async () =>
            {
                var addr = await _lib.GetPatientAddressAsync(D.PatientId);
                var text = ConsultRules.CleanMedCertAddress(ConsultRules.PatientAddressFromProfile(addr?.Address, addr?.City));
                _patientAddressMissing = text.Length == 0;
                if (!D.CertificateExists && string.IsNullOrWhiteSpace(D.McAddress)) D.McAddress = text;
            }),
        };
        await Task.WhenAll(tasks);
        //JJQR - END
    }

    void InitFormState()
    {
        var w = _workspace!;
        _bookedVisitType = D.VisitType;
        _medCertManual = D.MedCertRequested ? true : null; // a certificate the front desk requested at check-in stays ticked
        D.PfDecision ??= "Charge";                          // web initialPfDecision: Charge unless Waive was stored
        _pfEdited = D.FeeState().PfAmountEdited || (D.PfDecision == "Charge" && D.PfAmount is { } a && a != w.ComputedFee);
        _pfTyped = _pfEdited && D.PfAmount is { } t ? t.ToString("0.##", CultureInfo.InvariantCulture) : "";
        // RA 9994: a patient aged 60+ gets the Senior discount when nothing was chosen yet (documenting only)
        if (_mode == "complete" && D.DiscountCategory is null && w.PatientDateOfBirth is { } dob && AgeOn(dob, ClinicClock.Today) >= 60)
            D.DiscountCategory = "Senior";
        if (!D.CertificateExists)
        {
            if (string.IsNullOrWhiteSpace(D.McExaminedAt)) D.McExaminedAt = ConsultRules.McExaminedAtDefault;
            D.McExamFrom ??= ClinicClock.Today;
            D.McExamTo ??= ClinicClock.Today;
        }
        else _mcDxDirty = !string.IsNullOrWhiteSpace(D.McDiagnosis);
        _savedSnapshot = Snapshot();
    }

    static int AgeOn(DateOnly dob, DateOnly today)
    {
        var age = today.Year - dob.Year;
        if (dob.AddYears(age) > today) age--;
        return Math.Max(0, age);
    }

    // ---------- derived (web previewFee / medCertRequested / pfAmountShown) ----------
    bool MedCertRequested() => ConsultRules.MedCertRequested(_medCertManual,
        ConsultRules.MedCertFeeAddOn(D.CertificateExists, D.McRecommendations, D.McPurposeException, D.McComeBackOn));

    //JJQR - AI (Nov 21, 2026) - the fee strip shows the TOTAL as a clear amount and the breakdown quietly underneath
    void SetFeePreview()
    {
        var f = PreviewFee();
        //JJQR - AI (Nov 21, 2026) - a waived visit shows "Waived" in the header, not the computed fee
        if (D.PfDecision == "Waive")
        {
            FeeTotal.Text = "Waived";
            FeePreview.Text = (D.PfWaiveReason ?? "").Trim() is { Length: > 0 } wr ? ConsultRules.ClipText(wr, 40) : "Add a reason";
            return;
        }
        //JJQR - END
        FeeTotal.Text = FeeCalculator.FormatPeso(f.Total);
        //JJQR - AI (Nov 21, 2026) - plain-language breakdown: "Follow-up ₱350 + PWD rate adjustment ₱50" instead of "− PWD discount ₱-50" when the Senior/PWD rate is above the visit fee
        FeePreview.Text = string.Concat(f.Parts.Select((part, i) =>
        {
            var amount = FeeCalculator.FormatPeso(Math.Abs(part.Amount));
            if (i == 0) return $"{part.Label} {amount}";
            if (!part.Subtract) return $" + {part.Label} {amount}";
            return part.Amount >= 0 ? $" − {part.Label} {amount}" : $" + {part.Label.Replace("discount", "rate")} adjustment {amount}";
        }));
        //JJQR - END
    }
    //JJQR - END

    ClinicFeePreview PreviewFee() => ConsultRules.PreviewFee(_settings, D.VisitType, D.DiscountCategory, MedCertRequested());

    decimal? TypedAmount() => decimal.TryParse(_pfTyped.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    decimal? PfShown() => ConsultRules.PfAmountShown(D.PfDecision, _pfEdited, TypedAmount(), PreviewFee().Total);

    /// <summary>web followTotal(): the amount goes back to following the computed total.</summary>
    void FollowTotal() { _pfTyped = ""; _pfEdited = false; _pfOverrideBase = null; }

    /// <summary>web: an override typed against an older total reverts once the total changes.</summary>
    void CheckOverrideBase()
    {
        var total = PreviewFee().Total;
        if (_pfEdited && _pfOverrideBase is { } b && total != b) FollowTotal();
    }

    /// <summary>Write the fee state onto the draft right before any save.</summary>
    void SyncFeeToDraft()
    {
        CheckOverrideBase();
        D.MedCertRequested = MedCertRequested();
        D.FeeState().PfAmountEdited = D.PfDecision == "Charge" && _pfEdited && TypedAmount() is not null;
        D.PfAmount = D.FeeState().PfAmountEdited ? TypedAmount() : null;
        if (!D.CertificateExists || _mcDxDirty) return;
        D.McDiagnosis = string.IsNullOrWhiteSpace(D.McDiagnosis) ? McDiagnosisDefault() : D.McDiagnosis;
    }

    string McDiagnosisDefault() => ConsultRules.DiagnosisTextFromConsult(D.Diagnoses.Select(DxText), D.Assessment);
    string DxText(DiagnosisLine d) => d.CustomDescription ?? (d.Icd10Code is { } c && _icdNames.TryGetValue(c, out var n) ? n : d.Icd10Code) ?? "";

    IReadOnlyList<int> Missing() => ConsultRules.MissingRequired(D, PfShown());
    bool CanComplete => Missing().Count == 0;

    string Snapshot()
    {
        try { return JsonSerializer.Serialize(new { D, _pfTyped, _pfEdited, _medCertManual }); }
        catch { return Guid.NewGuid().ToString(); }
    }

    bool _savedLocalOnly; //JJQR - AI (Nov 22, 2026) - QA high #3: the last save did not reach the clinic server //JJQR - END
    bool Dirty => _workspace is not null && Snapshot() != _savedSnapshot;

    void StartAutosave()
    {
        if (_mode != "complete") return;
        _autosave ??= Dispatcher.CreateTimer();
        _autosave.Interval = TimeSpan.FromSeconds(30);
        _autosave.IsRepeating = true;
        _autosave.Tick -= OnAutosaveTick;
        _autosave.Tick += OnAutosaveTick;
        _autosave.Start();
    }

    async void OnAutosaveTick(object? sender, EventArgs e)
    {
        if (_mode != "complete" || _busy || Overlay.IsVisible || !Dirty) return;
        await SaveAsync(goNext: false, silent: true);
    }

    void Changed()
    {
        CheckOverrideBase();
        SetFeePreview();
        RebuildSteps();
        UpdateFooter();
    }

    // ---------- header ----------
    void RenderAll()
    {
        RenderHeader();
        RenderAllergyBar();
        RebuildSteps();
        RenderSection();
    }

    void RenderHeader()
    {
        var w = _workspace!;
        PageTitle.Text = _mode switch { "amend" => "Amend consultation", "view" => "Consultation record", _ => "Document consultation" };
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - ConsultPatientBanner: one "name · sex · age" line (RenderBanner in ConsultationPage.Ui.cs)
        RenderBanner();
        //JJQR - END
        SetFeePreview();
        CancelAmendButton.IsVisible = _mode == "amend";
        CancelAmendButton.Text = Dirty ? "Cancel amendment" : "Done"; //JJQR - AI (Nov 21, 2026) - nothing unsaved: it is just "Done" //JJQR - END
        RenderVisitTypeBar();
    }

    void RenderVisitTypeBar()
    {
        VisitTypeBar.Children.Clear();
        if (ReadOnly)
        {
            VisitTypeBar.Children.Add(new Label { Text = D.VisitType == VisitType.FollowUp ? "Follow-up visit" : "New visit", FontFamily = ClinicTheme.FontSemiBold, FontSize = 14 });
            return;
        }
        //JJQR - AI (Nov 21, 2026) - one-row segmented switch instead of two buttons that wrapped
        VisitTypeBar.Children.Add(ClinicNode.App.Views.Doctor.Extra.DoctorExtraUi.Segmented(["New visit", "Follow-up visit"], D.VisitType == VisitType.FollowUp ? 1 : 0, i =>
        {
            D.VisitType = i == 1 ? VisitType.FollowUp : VisitType.New;
            FollowTotal();
            RenderVisitTypeBar();
            if (_section == ConsultSteps.FeeSectionIndex) RenderSection(); else Changed();
        }));
        //JJQR - END
        if (D.VisitType != _bookedVisitType)
            VisitTypeBar.Children.Add(ClinicTheme.Text($"(staff tagged this {(_bookedVisitType == VisitType.FollowUp ? "Follow-up" : "New")} at check-in)", "MutedSm")); //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web wording //JJQR - END
    }

    // ---------- allergies (web ConsultPatientBanner: NKDA or a real allergen list, never both) ----------
    void RenderAllergyBar()
    {
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - the banner chip + inline editor replace the old bar
        if (_workspace is not null) { RenderBanner(); return; }
        //JJQR - END
        AllergyBar.Children.Clear();
        var real = _allergies.Where(a => !a.IsNkda).ToList();
        var nkda = _allergies.Any(a => a.IsNkda);
        var summary = nkda ? "NKDA (no known drug allergies)"
            : real.Count == 0 ? "No allergies recorded"
            : "Allergies: " + string.Join(", ", real.Select(a => a.Allergen + (string.IsNullOrWhiteSpace(a.Reaction) ? "" : $" ({a.Reaction})")));
        var label = ClinicTheme.Text(summary, "BodyMd");
        if (real.Count > 0) label.TextColor = ClinicTheme.Color("StatusDanger");
        AllergyBar.Children.Add(label);
        if (ReadOnly) return;
        var row = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        row.Children.Add(Btn(nkda ? "Clear NKDA" : "Mark NKDA", async () =>
            await SaveAllergiesAsync(nkda ? Array.Empty<AllergyInput>() : new[] { new AllergyInput("NKDA", IsNkda: true) })));
        row.Children.Add(Btn("Add allergy", async () => await AddAllergyAsync()));
        foreach (var a in real)
        {
            var keep = real.Where(x => x.Id != a.Id).Select(ToInput).ToList();
            row.Children.Add(Btn("Remove " + a.Allergen, async () => await SaveAllergiesAsync(keep)));
        }
        AllergyBar.Children.Add(row);
    }

    static AllergyInput ToInput(PatientAllergy a) => new(a.Allergen, a.Reaction, a.Severity, false, a.Id);

    async Task AddAllergyAsync()
    {
        var name = await DisplayPromptAsync("Add allergy", "Allergen", "Next", "Cancel", "e.g. Penicillin", 200);
        if (string.IsNullOrWhiteSpace(name)) return;
        var reaction = await DisplayPromptAsync("Add allergy", "Reaction (optional)", "Save", "Skip", "e.g. Rash", 200);
        var list = _allergies.Where(a => !a.IsNkda).Select(ToInput).ToList();
        list.Add(new AllergyInput(name.Trim(), string.IsNullOrWhiteSpace(reaction) ? null : reaction.Trim()));
        await SaveAllergiesAsync(list);
    }

    async Task SaveAllergiesAsync(IReadOnlyList<AllergyInput> list)
    {
        if (_workspace is null) return;
        try
        {
            var saved = await _lib.ReplaceAllergiesAsync(D.PatientId, list);
            if (saved is not null) { _allergies = saved; RenderAllergyBar(); }
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    // ---------- step rail: "{n}. {name}" pills, More/Less, then the fee pill last ----------
    void RebuildSteps()
    {
        if (_workspace is null) return;
        StepBar.Children.Clear();
        //JJQR - AI (Nov 22, 2026) - owner request: every step is always shown (no "More steps" / "Show fewer steps" toggle)
        var order = ConsultRules.VisibleOrder(true, _section);
        var shown = order.Where(i => i != ConsultSteps.FeeSectionIndex).ToList();
        var n = 0;
        foreach (var i in shown) StepBar.Children.Add(StepPill(i, ++n));
        //JJQR - END
        if (order.Contains(ConsultSteps.FeeSectionIndex)) StepBar.Children.Add(StepPill(ConsultSteps.FeeSectionIndex, ++n));
    }

    //JJQR - AI (Nov 21, 2026) - left-aligned step row: coloured status mark, number + name, hover and active states
    View StepRow(string mark, string text, bool active, Action tap, bool muted = false)
    {
        var markColor = mark.StartsWith("✓") ? ClinicTheme.Color("StatusSuccess") : mark.StartsWith("⚠") ? ClinicTheme.Color("StatusWarning") : ClinicTheme.Color("Outline");
        var markLbl = new Label { Text = mark.Trim(), FontSize = 13, MinimumWidthRequest = 22, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center, TextColor = active ? Colors.White : markColor };
        var lbl = new Label
        {
            Text = text, FontSize = 14, LineBreakMode = LineBreakMode.TailTruncation, VerticalOptions = LayoutOptions.Center,
            FontFamily = active ? ClinicTheme.FontSemiBold : ClinicTheme.FontMedium,
            TextColor = active ? Colors.White : muted ? ClinicTheme.Color("OnSurfaceVariant") : ClinicTheme.Color("OnSurface"),
        };
        var row = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12, 10),
            BackgroundColor = active ? ClinicTheme.Color("Primary") : Colors.Transparent,
            Content = new HorizontalStackLayout { Spacing = 10, Children = { markLbl, lbl } },
        };
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(tap) });
        if (!active)
        {
            var hover = new PointerGestureRecognizer();
            hover.PointerEntered += (_, _) => row.BackgroundColor = ClinicTheme.Color("SurfaceContainer");
            hover.PointerExited += (_, _) => row.BackgroundColor = Colors.Transparent;
            row.GestureRecognizers.Add(hover);
        }
        return row;
    }
    //JJQR - END

    View StepPill(int i, int number)
    {
        var required = ConsultRules.RequiredSections.Contains(i);
        var satisfied = ConsultRules.SectionSatisfied(i, D, PfShown());
        var showWarnings = _mode != "view" || _completeAttempted;
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web renderSectionIndicator (✓ / ○ / ⚠ / filled-of-total)
        var mark = StepMark(i) + " ";
        //JJQR - END
        return StepRow(mark, $"{number}. {ConsultSteps.Sections[i]}", i == _section, () => GoTo(i)); //JJQR - AI (Nov 21, 2026) - custom left-aligned row //JJQR - END
    }

    bool HasContent(int section) => section switch
    {
        2 => D.Diagnoses.Count > 0,
        4 => D.LabOrders.Count > 0,
        5 => D.Vaccinations.Count > 0,
        7 => D.CertificateExists,
        _ => true,
    };

    bool HasDraft(int section) => section switch
    {
        6 => !string.IsNullOrWhiteSpace(D.FollowUpReason) || !string.IsNullOrWhiteSpace(D.FollowUpInstructions) || D.FollowUpMode != FollowUpMode.Dated,
        7 => !string.IsNullOrWhiteSpace(D.McRecommendations) || !string.IsNullOrWhiteSpace(D.McPurposeException) || D.McComeBackOn is not null,
        8 => D.PfDecision is not null,
        _ => false,
    };

    void GoTo(int section)
    {
        _section = section;
        if (section == ConsultSteps.VaccinationsSectionIndex) _showMore = true;
        HideOverlay();
        RebuildSteps();
        RenderSection();
    }

    int? Neighbor(int direction) => ConsultSteps.AdjacentSection(ConsultRules.VisibleOrder(_showMore, _section), _section, direction);

    // ---------- sections ----------
    void RenderSection()
    {
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs) - completed view uses the web summary cards, not a disabled editor
        if (_mode == "view" && _workspace is not null) { RenderRecordView(); return; }
        StepBar.IsVisible = true;
        PrevButton.IsVisible = true;
        NextButton.IsVisible = true;
        //JJQR - END
        if (_section != 3 && _rxEditOriginal is not null) { RestoreRxEdit(); Changed(); } //JJQR - AI (Nov 21, 2026) - left the step mid-edit: the medicine comes back //JJQR - END
        _soapTagSpans.Clear();
        SectionHost.Children.Clear();
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - card title "{indicator} {n}. {Section}"
        SectionHost.Children.Add(SectionTitle(_section));
        //JJQR - END
        switch (_section)
        {
            case 0: RenderNotes(); break;
            case 1: RenderVitals(); break;
            case 2: RenderDiagnosis(); break;
            case 3: RenderPrescription(); break;
            case 4: RenderLabs(); break;
            case 5: RenderVaccinations(); break;
            case 6: RenderFollowUp(); break;
            case 7: RenderCertificate(); break;
            case 8: RenderFee(); break;
        }
        SectionHost.IsEnabled = !ReadOnly;
        SetFeePreview();
        UpdateFooter();
    }

    // Clinical notes: SOAP template picker (Append / Replace preview), Save as Template, View Last Appointment SOAP, per-field phrases
    void RenderNotes()
    {
        var bar = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        //JJQR - AI (21 Nov 2026) - SOAP specialty filter (web filteredSoapTemplates)
        var soapList = FilteredSoapTemplates();
        if (_soapTemplates.Count > 0 && soapList.Count == 0)
            bar.Children.Add(new Label { Text = "No templates for this specialty", Style = ClinicTheme.Style("MutedSm"), Margin = new Thickness(0, 0, 8, 8), VerticalOptions = LayoutOptions.Center });
        if (soapList.Count > 0)
        {
            //JJQR - AI (Nov 21, 2026) - button + list instead of the Mac wheel picker
            bar.Children.Add(PickButton("Use template…", soapList.Select(t => t.Title).ToList(), i => { RequestApplyTemplate(soapList[i]); return Task.CompletedTask; }));
            //JJQR - END
        }
        bar.Children.Add(Btn("Save as Template", async () => await SaveSoapTemplateAsync()));
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        bar.Children.Add(ManageLink());
        //JJQR - END
        if (_history.FirstOrDefault() is { } last) bar.Children.Add(Btn("View Last Appointment SOAP", () => ShowLastSoap(last)));
        //JJQR - AI (Nov 21, 2026) - templates toolbar + specialty chips grouped in one tinted panel
        SectionHost.Children.Add(new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"),
            Stroke = ClinicTheme.Color("OutlineVariant"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(14, 12, 14, 4),
            Content = new VerticalStackLayout { Spacing = 8, Children = { ClinicTheme.Text("TEMPLATES", "LabelSm"), bar, BuildSoapSpecialtyChips() } },
        });
        //JJQR - END

        AddSoapField(0, D.ChiefComplaint, v => D.ChiefComplaint = v, required: true);
        AddSoapField(1, D.Subjective, v => D.Subjective = v);
        AddSoapField(2, D.Objective, v => D.Objective = v);
        AddSoapField(3, D.Assessment, v => D.Assessment = v);
        var primary = D.Diagnoses.FirstOrDefault(x => x.Type == DiagnosisType.Primary);
        if (primary is not null && ConsultRules.AssessmentMissesPrimaryDx(D.Assessment, DxText(primary)))
            SectionHost.Children.Add(ClinicTheme.Text("Assessment does not mention the primary diagnosis. Review the diagnosis if the visit reason changed.", "MutedSm"));
        AddSoapField(4, D.Plan, v => D.Plan = v);
    }

    //JJQR - AI (Nov 21, 2026)
    readonly HashSet<int> _flashSoap = new();
    readonly Dictionary<int, Border> _soapFrames = new();
    readonly List<Span> _soapTagSpans = new();
    //JJQR - AI (Nov 21, 2026) - one fade routine: removes the tint AND the "added from template" tag (also used by Undo)
    void FadeSoapFlash()
    {
        _flashSoap.Clear();
        foreach (var f in _soapFrames.Values) ApplyFlash(f, false);
        foreach (var sp in _soapTagSpans) sp.Text = "";
        _soapTagSpans.Clear();
    }
    //JJQR - END
    static void ApplyFlash(Border f, bool on)
    {
        f.BackgroundColor = on ? Color.FromArgb("#E7F6F2") : Colors.Transparent;
        f.Stroke = on ? ClinicTheme.Color("Primary") : Colors.Transparent;
        f.StrokeThickness = on ? 1.5 : 0;
    }
    //JJQR - END

    SoapValues CurrentSoap() => SoapValues.From(D.ChiefComplaint, D.Subjective, D.Objective, D.Assessment, D.Plan);

    void WriteSoap(SoapValues v)
    {
        D.ChiefComplaint = v.ChiefComplaint; D.Subjective = v.Subjective; D.Objective = v.Objective; D.Assessment = v.Assessment; D.Plan = v.Plan;
    }

    void RequestApplyTemplate(SoapTemplateItem t)
    {
        var tpl = SoapValues.From(t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan);
        if (!CurrentSoap().HasContent) { DoApplyTemplate(t.Title, tpl, SoapApplyMode.Append); return; } // all five empty: apply directly, Undo still offered
        ShowApplyTemplate(t.Title, tpl, SoapApplyMode.Append);
    }

    void ShowApplyTemplate(string title, SoapValues tpl, SoapApplyMode mode)
    {
        var current = CurrentSoap();
        var (values, outcomes) = ConsultRules.MergeSoapTemplate(current, tpl, mode);
        var body = new List<View> { ClinicTheme.Text($"Template: “{title}”", "BodyMd") };
        if (!tpl.HasContent) body.Add(ClinicTheme.Text("This template has no text in any field, so there is nothing to apply.", "ErrorText"));
        var modes = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        modes.Children.Add(Pill("Append (keep my text)", mode == SoapApplyMode.Append, () => ShowApplyTemplate(title, tpl, SoapApplyMode.Append)));
        modes.Children.Add(Pill("Replace (use template text)", mode == SoapApplyMode.Replace, () => ShowApplyTemplate(title, tpl, SoapApplyMode.Replace)));
        body.Add(modes);
        //JJQR - AI (Nov 21, 2026) - the preview shows what STAYS (grey) and what will be ADDED (green highlight); unchanged fields collapse to one line
        for (var i = 0; i < 5; i++) body.Add(SoapPreviewCard(i, current[i] ?? "", values[i] ?? "", outcomes[i]));
        //JJQR - END
        var applyStyle = mode == SoapApplyMode.Replace ? "DangerButton" : "PrimaryButton";
        var actions = new List<(string, Action, string)> { ("Cancel", HideOverlay, "SecondaryButton") };
        if (tpl.HasContent) actions.Add(("Apply template", () => { HideOverlay(); DoApplyTemplate(title, tpl, mode); }, applyStyle));
        ShowOverlay("Apply SOAP template", body, actions.ToArray());
    }

    //JJQR - AI (Nov 21, 2026)
    View SoapPreviewCard(int i, string cur, string nx, SoapFieldOutcome outcome)
    {
        var name = ConsultRules.SoapLabels[i];
        if (outcome is not (SoapFieldOutcome.Filled or SoapFieldOutcome.Replaced or SoapFieldOutcome.Appended))
        {
            // nothing changes in this field: still show the CURRENT text (grey) so the doctor always sees what is there, tagged "No change"
            var noChip = new Border
            {
                StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, BackgroundColor = ClinicTheme.Color("SurfaceContainer"), Padding = new Thickness(8, 3), VerticalOptions = LayoutOptions.Center,
                Content = new Label { Text = outcome == SoapFieldOutcome.KeptTemplateBlank ? "TEMPLATE BLANK · YOURS KEPT" : "NO CHANGE", FontSize = 11, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("OnSurfaceVariant") },
            };
            var qhead = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            qhead.Add(new Label { Text = name, FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, VerticalOptions = LayoutOptions.Center }, 0, 0);
            qhead.Add(noChip, 1, 0);
            var qcol = new VerticalStackLayout { Spacing = 8, Children = { qhead } };
            qcol.Children.Add(new Label { Text = string.IsNullOrWhiteSpace(cur) ? "(empty)" : cur, FontSize = 13, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
            return new Border
            {
                StrokeThickness = 1, Stroke = ClinicTheme.Color("OutlineVariant"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(14, 12), BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"), Content = qcol,
            };
        }
        var replaced = outcome == SoapFieldOutcome.Replaced;
        var kept = "";
        var added = nx;
        if (outcome == SoapFieldOutcome.Appended && cur.Length > 0 && nx.StartsWith(cur.TrimEnd(), StringComparison.Ordinal))
        {
            kept = cur.TrimEnd();
            added = nx.Substring(kept.Length).Trim('\r', '\n', ' ');
        }
        var chipText = replaced ? "REPLACES YOUR TEXT" : "+ ADDED";
        var chipBg = replaced ? ClinicTheme.Color("StatusWarningSurface") : ClinicTheme.Color("StatusSuccessSurface");
        var chipFg = replaced ? ClinicTheme.Color("StatusWarning") : ClinicTheme.Color("StatusSuccess");
        var chip = new Border
        {
            StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, BackgroundColor = chipBg, Padding = new Thickness(8, 3), VerticalOptions = LayoutOptions.Center,
            Content = new Label { Text = chipText, FontSize = 11, FontFamily = ClinicTheme.FontSemiBold, TextColor = chipFg },
        };
        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        head.Add(new Label { Text = name, FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, VerticalOptions = LayoutOptions.Center }, 0, 0);
        head.Add(chip, 1, 0);
        var col = new VerticalStackLayout { Spacing = 8, Children = { head } };
        if (replaced && !string.IsNullOrWhiteSpace(cur))
            col.Children.Add(new Label { Text = cur, FontSize = 13, TextColor = ClinicTheme.Color("OnSurfaceVariant"), TextDecorations = TextDecorations.Strikethrough });
        if (kept.Length > 0)
            col.Children.Add(new Label { Text = kept, FontSize = 13, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
        if (!string.IsNullOrWhiteSpace(added))
            col.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#E7F6F2"),
                Stroke = ClinicTheme.Color("Primary"), StrokeThickness = 1.5,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(12, 9),
                Content = new Label { Text = added, FontSize = 14, TextColor = ClinicTheme.Color("OnSurface") },
            });
        return new Border
        {
            StrokeThickness = 1, Stroke = ClinicTheme.Color("OutlineVariant"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(14, 12), BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"), Content = col,
        };
    }
    //JJQR - END

    void DoApplyTemplate(string title, SoapValues tpl, SoapApplyMode mode)
    {
        var before = CurrentSoap();
        var (values, _) = ConsultRules.MergeSoapTemplate(before, tpl, mode);
        WriteSoap(values);
        _undo = (++_noticeSeq, title, before, values);
        ShowToast($"Applied “{title}”.", "Undo", _noticeSeq);
        //JJQR - AI (Nov 21, 2026) - which of the five fields did the template change?
        _flashSoap.Clear();
        if (before.ChiefComplaint != values.ChiefComplaint) _flashSoap.Add(0);
        if (before.Subjective != values.Subjective) _flashSoap.Add(1);
        if (before.Objective != values.Objective) _flashSoap.Add(2);
        if (before.Assessment != values.Assessment) _flashSoap.Add(3);
        if (before.Plan != values.Plan) _flashSoap.Add(4);
        //JJQR - END
        RenderSection();
        Changed();
        //JJQR - AI (Nov 21, 2026) - the highlight fades after 10 seconds
        var flashSeq = _noticeSeq;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(10), () =>
        {
            if (flashSeq == _noticeSeq) FadeSoapFlash();
            return false;
        });
        //JJQR - END
    }

    void OnToastAction(object? sender, EventArgs e)
    {
        // Undo only while the notes are exactly what the template produced — never overwrites later typing.
        if (_undo is { } u && CurrentSoap() == u.Applied) { FadeSoapFlash(); WriteSoap(u.Before); RenderSection(); Changed(); }
        _undo = null;
        ToastBar.IsVisible = false;
    }

    void ShowToast(string text, string? action, int id)
    {
        ToastLabel.Text = text;
        ToastAction.IsVisible = action is not null;
        ToastAction.Text = action ?? "";
        ToastBar.IsVisible = true;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () =>
        {
            if (_noticeSeq == id) { ToastBar.IsVisible = false; _undo = null; }
            return false;
        });
    }

    async Task SaveSoapTemplateAsync()
    {
        if (!CurrentSoap().HasContent) { Error("Nothing to save — all five SOAP fields are empty. Type something first."); return; }
        var title = await DisplayPromptAsync("Save as Template", "Title (e.g. \"Annual Physical — Normal\")", "Save", "Cancel", "Title", 200);
        if (string.IsNullOrWhiteSpace(title)) return;
        try
        {
            var saved = await _lib.SaveSoapTemplateAsync(D.DoctorId, title, CurrentSoap());
            _soapTemplates = _soapTemplates.Append(saved).OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToList();
            ShowToast($"Saved template “{saved.Title}”.", null, ++_noticeSeq);
            RenderSection();
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    void ShowLastSoap(PastVisit last)
    {
        //JJQR - AI (21 Nov 2026) - web modal: Date, Subjective, Objective, Assessment, Plan, then a "Vitals" box when the visit had readings
        var body = new List<View>();
        void Add(string label, string? v)
        {
            var l = new Label { Style = ClinicTheme.Style("BodyMd") };
            var fs = new FormattedString();
            fs.Spans.Add(new Span { Text = label + ": ", FontAttributes = FontAttributes.Bold });
            fs.Spans.Add(new Span { Text = v ?? "" });
            l.FormattedText = fs;
            body.Add(l);
        }
        Add("Date", last.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("Subjective", last.Subjective); Add("Objective", last.Objective); Add("Assessment", last.Assessment); Add("Plan", last.Plan);
        if (last.Vitals.Length > 0)
            body.Add(new Border
            {
                StrokeThickness = 0, BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"), Padding = 8,
                Content = new VerticalStackLayout { Spacing = 2, Children = { ClinicTheme.Text("Vitals", "LabelMd"), ClinicTheme.Text(last.Vitals, "BodyMd") } },
            });
        //JJQR - END
        ShowOverlay("Last Appointment SOAP", body, ("Close", HideOverlay, "SecondaryButton"));
    }

    //JJQR - AI (Nov 21, 2026) - one clearly grouped block per SOAP field: bold label + hint on the left, small "Insert phrase" on the right, bordered text box below, divider between fields
    void AddSoapField(int index, string? value, Action<string?> set, bool required = false)
    {
        var field = ConsultRules.SoapFields[index];
        var label = new Label { FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, TextColor = ClinicTheme.Color("OnSurface"), VerticalOptions = LayoutOptions.Center };
        label.FormattedText = new FormattedString();
        label.FormattedText.Spans.Add(new Span { Text = ConsultRules.SoapLabels[index] });
        if (required) label.FormattedText.Spans.Add(new Span { Text = "  required", FontSize = 11, FontFamily = ClinicTheme.FontMedium, TextColor = ClinicTheme.Color("Error") });
        var hint = ClinicTheme.Text(ConsultRules.SoapHints[index], "MutedSm");
        var editor = new Editor { Text = value, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 104, FontSize = 14 };
        editor.TextChanged += (_, e) => { set(e.NewTextValue); Changed(); };
        var phrases = _phrases.Where(p => p.Field == field).ToList();
        var items = phrases.Select(p => p.Label).Append("＋ Save current text as phrase…").ToList();
        var picker = PickButton("Insert phrase…", items, async i =>
        {
            if (i < phrases.Count) { editor.Text = ConsultRules.InsertPhrase(editor.Text, phrases[i].Body); editor.Focus(); return; }
            await SavePhraseAsync(field, editor.Text);
        }, "GhostButton");
        picker.HorizontalOptions = LayoutOptions.End;
        picker.VerticalOptions = LayoutOptions.Start;
        var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
        head.Add(new VerticalStackLayout { Spacing = 2, Children = { label, hint } }, 0, 0);
        head.Add(picker, 1, 0);
        var block = new VerticalStackLayout { Spacing = 8, Children = { head, editor } };
        //JJQR - AI (Nov 21, 2026) - fields a template just changed are tinted and tagged so the doctor sees exactly what was added
        var frame = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 }, Padding = new Thickness(10, 8), Content = block, BackgroundColor = Colors.Transparent };
        _soapFrames[index] = frame;
        if (_flashSoap.Contains(index))
        {
            ApplyFlash(frame, true);
            var tagSpan = new Span { Text = "   ✓ added from template", FontSize = 11, FontFamily = ClinicTheme.FontMedium, TextColor = ClinicTheme.Color("StatusSuccess") };
            label.FormattedText.Spans.Add(tagSpan);
            _soapTagSpans.Add(tagSpan);
        }
        SectionHost.Children.Add(frame);
        //JJQR - END
        SectionHost.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant"), Margin = new Thickness(0, 8, 0, 4) });
    }
    //JJQR - END

    async Task SavePhraseAsync(SoapField field, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) { Error("Type the phrase in the box first, then save it."); return; }
        var label = await DisplayPromptAsync("Save phrase", "Short label for this phrase", "Save", "Cancel", "Label", 100,
            initialValue: ConsultRules.ClipText(text, 40));
        if (string.IsNullOrWhiteSpace(label)) return;
        try
        {
            var saved = await _lib.SaveSoapPhraseAsync(D.DoctorId, field, label, text);
            _phrases = _phrases.Append(saved).ToList();
            ShowToast($"Saved phrase “{saved.Label}”.", null, ++_noticeSeq);
            RenderSection();
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    void RenderVitals()
    {
        SectionHost.Children.Add(ClinicTheme.Text("Blood pressure and heart rate are required when those templates exist.", "MutedSm"));
        //JJQR - AI (21 Nov 2026) - web: the most recent prior appointment only, "Last appointment (yyyy-MM-dd): …" on a tinted strip
        var last = _history.FirstOrDefault();
        if (last is not null && last.Vitals.Length > 0)
            SectionHost.Children.Add(new Border
            {
                StrokeThickness = 0, BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"), Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 0, 8),
                Content = ClinicTheme.Text($"Last appointment ({last.Date:yyyy-MM-dd}): {last.Vitals}", "MutedSm"),
            });
        //JJQR - END
        // vitals are locked during view and amend (cloud §5.5 append-only; they were recorded at the visit)
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        RenderPedsVitals();
        //JJQR - END
        var locked = _mode is "amend" or "view";
        if (locked)
            SectionHost.Children.Add(ClinicTheme.Text(_mode == "amend"
                ? "Vital signs were recorded at the visit and cannot be changed in an amendment."
                : "Vital signs recorded at this visit.", "MutedSm"));
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - VitalsEditor.tsx: one card per template (label, input with "Enter …" placeholder, unit beside it, "Out of range")
        var vitalGrid = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var v in D.Vitals)
        {
            var vital = v;
            var card = new VerticalStackLayout { Spacing = 6 };
            card.Children.Add(ClinicTheme.Text(v.Description, "LabelMd").Also(l => l.TextColor = ClinicTheme.Color("OnSurfaceVariant")));
            var warn = ClinicTheme.Text("⚠ Out of range", "LabelSm");
            warn.TextColor = ClinicTheme.Color("StatusWarning");
            warn.IsVisible = VitalRanges.OutOfRange(v.FormKey, v.Value, AgeYears());
            if (locked)
                card.Children.Add(ClinicTheme.Text(string.IsNullOrWhiteSpace(v.Value) ? "Not recorded" : v.Value + (string.IsNullOrEmpty(v.Unit) ? "" : " " + v.Unit), "BodyMd"));
            else
            {
                var entry = new Entry
                {
                    Text = v.Value, HorizontalOptions = LayoutOptions.Fill,
                    Placeholder = string.IsNullOrEmpty(v.Unit) ? "Enter value" : $"Enter {v.Description.ToLowerInvariant()}",
                };
                entry.TextChanged += (_, e) => { vital.Value = e.NewTextValue; warn.IsVisible = VitalRanges.OutOfRange(vital.FormKey, vital.Value, AgeYears()); Changed(); };
                var line = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
                line.Add(entry, 0);
                if (!string.IsNullOrEmpty(v.Unit)) line.Add(ClinicTheme.Text(v.Unit, "LabelSm").Also(l => { l.VerticalOptions = LayoutOptions.Center; l.TextColor = ClinicTheme.Color("OnSurfaceVariant"); }), 1);
                card.Children.Add(line);
            }
            card.Children.Add(warn);
            vitalGrid.Children.Add(new Border
            {
                Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1, BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Padding = 12, Margin = new Thickness(0, 0, 8, 8), WidthRequest = 260, Content = card,
            });
        }
        SectionHost.Children.Add(vitalGrid);
        //JJQR - END
    }

    // Diagnosis stays hidden (ConsultSteps.ShowDiagnosisStep = false, as on the web) but keeps working if the step is turned back on.
    void RenderDiagnosis()
    {
        SectionHost.Children.Add(ClinicTheme.Text("Search ICD-10 by code or description, or type a free-text diagnosis.", "MutedSm"));
        if (D.Diagnoses.Count == 0) SectionHost.Children.Add(ClinicTheme.Text("No diagnoses yet.", "MutedBody"));
        foreach (var dx in D.Diagnoses.ToList())
        {
            var line = dx;
            var text = $"{dx.Type} · {(string.IsNullOrWhiteSpace(dx.Icd10Code) ? "" : dx.Icd10Code + " ")}{DxText(dx)}".Trim();
            var actions = new HorizontalStackLayout();
            if (dx.Type != DiagnosisType.Primary)
                actions.Children.Add(Btn("Mark Primary", () =>
                {
                    foreach (var o in D.Diagnoses) if (o.Type == DiagnosisType.Primary) o.Type = DiagnosisType.Secondary;
                    line.Type = DiagnosisType.Primary; RenderSection(); Changed();
                }));
            actions.Children.Add(Btn("Remove", () => { D.Diagnoses.Remove(line); RenderSection(); Changed(); }));
            SectionHost.Children.Add(Row(ClinicTheme.Text(text, "BodyMd"), actions));
        }
        DiagnosisType NextType() => D.Diagnoses.Any(x => x.Type == DiagnosisType.Primary) ? DiagnosisType.Secondary : DiagnosisType.Primary;
        var search = new Entry { Placeholder = "Search ICD-10 (e.g. J06 or pharyngitis)" };
        var results = new VerticalStackLayout { Spacing = 4 };
        CancellationTokenSource? cts = null;
        search.TextChanged += async (_, e) =>
        {
            cts?.Cancel();
            var my = cts = new CancellationTokenSource();
            try { await Task.Delay(250, my.Token); } catch (TaskCanceledException) { return; }
            var hits = await _catalog.SearchIcd10Async(e.NewTextValue, 15);
            results.Children.Clear();
            if (hits.Count == 0 && (e.NewTextValue?.Trim().Length ?? 0) >= 2) results.Children.Add(ClinicTheme.Text("No ICD-10 match.", "MutedSm"));
            foreach (var h in hits)
            {
                var hit = h;
                _icdNames[hit.Code] = hit.Description;
                results.Children.Add(Btn($"{hit.Code}  {hit.Description}", () =>
                {
                    D.Diagnoses.Add(new DiagnosisLine { Icd10Code = hit.Code, CustomDescription = hit.Description, Type = NextType() });
                    RenderSection(); Changed();
                }));
            }
        };
        SectionHost.Children.Add(search);
        SectionHost.Children.Add(results);
        var free = AddEntry("Free text", "");
        SectionHost.Children.Add(Btn("Add free-text diagnosis", () =>
        {
            if (string.IsNullOrWhiteSpace(free.Text)) return;
            D.Diagnoses.Add(new DiagnosisLine { CustomDescription = free.Text.Trim(), Type = NextType() });
            RenderSection(); Changed();
        }));
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        SectionHost.Children.Add(ManageLink());
        //JJQR - END
        if (_dxTemplates.Count > 0)
        {
            //JJQR - AI (Nov 21, 2026) - button + list instead of the Mac wheel picker
            var tpl = PickButton("Add from my diagnosis templates…", _dxTemplates.Select(t => t.Label).ToList(), i =>
            {
                var t = _dxTemplates[i];
                D.Diagnoses.Add(new DiagnosisLine { Icd10Code = t.Icd10Code, CustomDescription = t.Body, Type = NextType() });
                RenderSection(); Changed();
                return Task.CompletedTask;
            });
            //JJQR - END
            SectionHost.Children.Add(tpl);
        }
    }

    // Prescription (web PrescriptionForm, New Prescription + Templates tabs)
    private RxLine _rxNew = NewRx();
    //JJQR - AI (Nov 21, 2026) - editing a medicine: the original is remembered so Cancel (or leaving the step) puts it back exactly where it was
    private RxLine? _rxEditOriginal;
    private int _rxEditIndex;
    void RestoreRxEdit()
    {
        if (_rxEditOriginal is null) return;
        D.Prescriptions.Insert(Math.Min(Math.Max(0, _rxEditIndex), D.Prescriptions.Count), _rxEditOriginal);
        _rxEditOriginal = null; _rxNew = NewRx();
    }
    //JJQR - END
    private string _rxTab = "new";
    static RxLine NewRx() => new() { Route = "PO" };

    //JJQR - AI (Nov 21, 2026) - Prescription as two panes: LEFT = what is prescribed, RIGHT = "Add medicine" form
    int _rxMarkA = -1, _rxMarkB = -1;
    //JJQR - AI (Nov 24, 2026) - owner: "Include PTR in print", OFF by default, on the Prescription, Lab Orders and Medical Certificate steps. Kept per visit and per document on this computer; no redraw when ticked.
    // compact tick-box that sits in the step's title bar: [ ] Include PTR in print
    View PtrIncludeInline(int section)
    {
        var kind = section switch { 3 => ClinicNode.Core.Printing.ClinicPrintKind.Prescription, 4 => ClinicNode.Core.Printing.ClinicPrintKind.LabRequest, _ => ClinicNode.Core.Printing.ClinicPrintKind.MedicalCertificate };
        var choice = _gapServices?.GetService<ClinicNode.Core.Printing.IPtrPrintChoice>();
        var bookingId = D.BookingId;
        return PtrInlineControl(isOn: () => choice?.IsOn(bookingId, kind) == true, set: on => choice?.Set(bookingId, kind, on));
    }

    View PtrInlineControl(Func<bool> isOn, Action<bool> set)
    {
        var box = new CheckBox { IsChecked = isOn(), VerticalOptions = LayoutOptions.Center, Color = ClinicTheme.Color("Primary") };
        box.CheckedChanged += (_, e) => set(e.Value);   // no redraw: nothing on screen changes
        var label = new Label { Text = "Include PTR in print", FontSize = 13, VerticalOptions = LayoutOptions.Center, TextColor = ClinicTheme.Color("OnSurface") };
        label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => box.IsChecked = !box.IsChecked) });
        var row = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center, Children = { box, label } };
        if (string.IsNullOrWhiteSpace(_doctor?.PtrNumber))
            row.Children.Add(new Label { Text = "(PTR No. is empty: set it under Profile)", FontSize = 11, VerticalOptions = LayoutOptions.Center, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
        return row;
    }
    //JJQR - END

    void RenderPrescription()
    {
        _rxMarkA = _rxMarkB = -1;
        RenderPrescriptionCore();
        ArrangeRxPanes();
    }

    void ArrangeRxPanes()
    {
        if (_rxMarkA < 0 || _rxMarkB < _rxMarkA) return;
        var all = SectionHost.Children.Cast<View>().ToList();
        var top = all.Take(_rxMarkA).ToList();
        var list = all.Skip(_rxMarkA).Take(_rxMarkB - _rxMarkA).ToList();
        var form = all.Skip(_rxMarkB).ToList();
        SectionHost.Children.Clear();
        foreach (var v in top) SectionHost.Children.Add(v);

        var left = new VerticalStackLayout { Spacing = 10 };
        left.Children.Add(new Label { Text = $"Prescribed this visit ({D.Prescriptions.Count})", FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, TextColor = ClinicTheme.Color("OnSurface") });
        foreach (var v in list) left.Children.Add(v);

        var right = new VerticalStackLayout { Spacing = 10 };
        right.Children.Add(new Label { Text = "Add a medicine", FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, TextColor = ClinicTheme.Color("OnSurface") });
        foreach (var v in form) right.Children.Add(v);
        var rightPanel = new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"),
            Stroke = ClinicTheme.Color("OutlineVariant"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(16),
            VerticalOptions = LayoutOptions.Start,
            Content = right,
        };
        var cols = new Grid { ColumnSpacing = 20, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        cols.Add(left, 0, 0);
        cols.Add(rightPanel, 1, 0);
        SectionHost.Children.Add(cols);
    }

    void RenderPrescriptionCore()
    {
        var noMeds = new CheckBox { IsChecked = D.NoMedsConfirmed && D.Prescriptions.Count == 0, IsEnabled = D.Prescriptions.Count == 0 };
        noMeds.CheckedChanged += (_, e) => { D.NoMedsConfirmed = e.Value; Changed(); };
        SectionHost.Children.Add(WrapCheck(noMeds, "No medicines this visit"));
        SectionHost.Children.Add(ClinicTheme.Text("Only check if you are intentionally not prescribing. Required for Complete when the Rx list is empty.", "MutedSm"));
        //JJQR - AI (21 Nov 2026) - renal cue + drug-safety alerts (ConsultationPage.Safety.cs)
        RenderRenalCue();
        RenderSafetyAlerts();
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        RenderPedsDoseHelper();
        _rxMarkA = SectionHost.Children.Count; //JJQR - AI (Nov 21, 2026) //JJQR - END
        RenderRxBulkBar();
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - Refill last visit Rx (ConsultationPage.Safety.cs)
        RenderRefillLastVisit();
        //JJQR - END

        if (D.Prescriptions.Count == 0) SectionHost.Children.Add(ClinicTheme.Text("No medicines added yet.", "MutedBody"));
        foreach (var rx in D.Prescriptions.ToList())
        {
            var line = rx;
            var head = $"{rx.GenericName}{(string.IsNullOrWhiteSpace(rx.Dosage) ? "" : " " + rx.Dosage)}  #{rx.Quantity}";
            var detail = string.Join(" · ", new[] { ConsultRules.DirectionsLine(rx), rx.Route, rx.Indication, rx.Instruction }.Where(s => !string.IsNullOrWhiteSpace(s)));
            var actions = new HorizontalStackLayout();
            actions.Children.Add(Btn("Edit", () => { RestoreRxEdit(); _rxEditIndex = D.Prescriptions.IndexOf(line); _rxEditOriginal = line; _rxNew = Clone(line); D.Prescriptions.Remove(line); _rxTab = "new"; RenderSection(); Changed(); }));
            actions.Children.Add(Btn("Remove", () => { D.Prescriptions.Remove(line); ToastBar.IsVisible = false; RenderSection(); Changed(); }));
            //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs) - Select / Select All for bulk delete
            //JJQR - AI (Nov 21, 2026) - each medicine is its own small card: bold name line, muted directions, Edit / Remove underneath (the old side-by-side row overlapped in a narrow pane)
            var titleLbl = new Label { Text = head, FontFamily = ClinicTheme.FontSemiBold, FontSize = 14, TextColor = ClinicTheme.Color("OnSurface"), LineBreakMode = LineBreakMode.WordWrap };
            var textCol = new VerticalStackLayout { Spacing = 2, Children = { titleLbl } };
            if (detail.Length > 0) textCol.Children.Add(ClinicTheme.Text(detail, "MutedSm"));
            actions.Margin = new Thickness(30, 0, 0, 0);
            actions.Spacing = 6;
            SectionHost.Children.Add(new Border
            {
                BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
                Stroke = ClinicTheme.Color("OutlineVariant"),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(12, 10),
                Content = new VerticalStackLayout { Spacing = 8, Children = { RxSelectable(line, textCol), actions } },
            });
            //JJQR - END
            //JJQR - END
        }

        _rxMarkB = SectionHost.Children.Count; //JJQR - AI (Nov 21, 2026) //JJQR - END
        var tabs = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        tabs.Children.Add(Pill("New Prescription", _rxTab == "new", () => { _rxTab = "new"; RenderSection(); }));
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        tabs.Children.Add(Pill("Favorites", _rxTab == "favorites", () => { _rxTab = "favorites"; RenderSection(); }));
        //JJQR - END
        tabs.Children.Add(Pill("Templates", _rxTab == "templates", () => { _rxTab = "templates"; RenderSection(); }));
        SectionHost.Children.Add(tabs);
        if (_rxTab == "templates") { RenderRxTemplates(); return; }
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        if (_rxTab == "favorites") { RenderRxFavorites(); return; }
        //JJQR - END

        var rxNew = _rxNew;
        SectionHost.Children.Add(ClinicTheme.Text("Medication — pick from the list or just type it", "FieldLabel"));
        var name = new Entry { Text = rxNew.GenericName, Placeholder = "Medication" };
        var suggest = new VerticalStackLayout { Spacing = 4 };
        var typedHint = ClinicTheme.Text("", "MutedSm");
        void RefreshSuggest(string? text)
        {
            suggest.Children.Clear();
            var q = (text ?? "").Trim();
            typedHint.Text = "";
            if (q.Length < 3) return;
            var hits = _medicines.Where(m => m.GenericName.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(8).ToList();
            var exact = hits.FirstOrDefault(m => m.GenericName.Equals(q, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) { rxNew.MedicineId = exact.MedicineId; return; }
            foreach (var h in hits)
            {
                var hit = h;
                suggest.Children.Add(Btn(hit.GenericName, () => { rxNew.MedicineId = hit.MedicineId; rxNew.GenericName = hit.GenericName; name.Text = hit.GenericName; suggest.Children.Clear(); }));
            }
            typedHint.Text = "Not in catalog — prefer picking a generic from the list (RA 6675).\nWill be added as typed: " + q;
        }
        name.TextChanged += (_, e) =>
        {
            if (!string.Equals(e.NewTextValue, rxNew.GenericName, StringComparison.Ordinal)) rxNew.MedicineId = null;
            rxNew.GenericName = e.NewTextValue ?? "";
            RefreshSuggest(e.NewTextValue);
        };
        SectionHost.Children.Add(name);
        SectionHost.Children.Add(suggest);
        SectionHost.Children.Add(typedHint);
        Bind(AddEntry("Dosage / strength", rxNew.Dosage), v => rxNew.Dosage = v ?? "");
        Bind(AddEntry("# Quantity (e.g. 30, 1 box)", rxNew.Quantity), v => rxNew.Quantity = v ?? "");
        Bind(AddEntry("Route", rxNew.Route ?? "PO"), v => rxNew.Route = v);

        // RxTimingFields: Meal timing, Times, As needed
        SectionHost.Children.Add(ClinicTheme.Text("Meal timing", "FieldLabel"));
        var meals = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var m in new[] { "Before", "After" })
        {
            var meal = m;
            meals.Children.Add(Pill(meal, rxNew.MealRelation == meal, () => { rxNew.MealRelation = rxNew.MealRelation == meal ? null : meal; RenderSection(); }));
        }
        SectionHost.Children.Add(meals);
        SectionHost.Children.Add(ClinicTheme.Text("Times", "FieldLabel"));
        var slots = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        var current = ConsultRules.TimingToSlots(rxNew.Timing).ToList();
        foreach (var s in ConsultRules.RxSlots)
        {
            var slot = s;
            slots.Children.Add(Pill(slot, current.Contains(slot), () =>
            {
                var next = current.Contains(slot) ? current.Where(x => x != slot) : current.Append(slot);
                rxNew.Timing = ConsultRules.SlotsToTiming(next);
                RenderSection();
            }));
        }
        slots.Children.Add(Pill("As needed", rxNew.AsNeeded == true, () => { rxNew.AsNeeded = rxNew.AsNeeded == true ? null : true; RenderSection(); }));
        SectionHost.Children.Add(slots);

        SectionHost.Children.Add(ClinicTheme.Text("Duration", "FieldLabel"));
        var dur = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        foreach (var k in new[] { "Maintain", "Days", "Weeks" })
        {
            var kind = k;
            dur.Children.Add(Pill(kind, string.Equals(rxNew.DurationKind, kind, StringComparison.OrdinalIgnoreCase), () =>
            {
                rxNew.DurationKind = string.Equals(rxNew.DurationKind, kind, StringComparison.OrdinalIgnoreCase) ? null : kind;
                if (kind == "Maintain") rxNew.DurationValue = null;
                RenderSection();
            }));
        }
        if (rxNew.DurationKind is "Days" or "Weeks")
        {
            var val = new Entry { Text = rxNew.DurationValue?.ToString(CultureInfo.InvariantCulture), Placeholder = "How many", Keyboard = Keyboard.Numeric, WidthRequest = 110 };
            val.TextChanged += (_, e) => rxNew.DurationValue = int.TryParse(e.NewTextValue, out var n) && n > 0 ? n : null;
            dur.Children.Add(val);
        }
        SectionHost.Children.Add(dur);
        Bind(AddEntry("Indication (e.g. for fever, for cough)", rxNew.Indication), v => rxNew.Indication = string.IsNullOrWhiteSpace(v) ? null : v);
        var sig = AddEntry("Sig. / extra instructions (optional)", rxNew.Instruction);
        sig.MaxLength = ConsultRules.RxInstructionMax;
        Bind(sig, v => rxNew.Instruction = string.IsNullOrWhiteSpace(v) ? null : v);
        var directions = ConsultRules.DirectionsLine(rxNew);
        if (directions.Length > 0) SectionHost.Children.Add(ClinicTheme.Text(directions, "MutedSm"));

        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        SectionHost.Children.Add(RxAddToFavoritesCheck());
        //JJQR - END
        var err = new Label { Style = ClinicTheme.Style("ErrorText"), IsVisible = false };
        SectionHost.Children.Add(err);
        var editing = _rxEditOriginal is not null;
        //JJQR - AI (Nov 21, 2026) - while editing: a clear notice and a Cancel that restores the original
        if (editing)
        {
            SectionHost.Children.Add(Warning($"Editing “{_rxEditOriginal!.GenericName}”. Press Update item to save your changes, or Cancel to keep the original."));
            SectionHost.Children.Add(Btn("Cancel edit", () => { RestoreRxEdit(); RenderSection(); Changed(); return; }, "SecondaryButton"));
        }
        //JJQR - END
        SectionHost.Children.Add(Btn(editing ? "Update item" : "Add Item", () =>
        {
            if (!ConsultRules.RxCanAdd(rxNew.GenericName, rxNew.Quantity)) { err.Text = "Enter the medication and the quantity."; err.IsVisible = true; return; }
            //JJQR - AI (Nov 22, 2026) - QA high #6
            if (ConsultRules.RxLineProblem(rxNew.Dosage, rxNew.Quantity, rxNew.DurationKind, rxNew.DurationValue) is { } lineProblem) { err.Text = lineProblem; err.IsVisible = true; return; }
            //JJQR - END
            //JJQR - AI (21 Nov 2026) - web handleNewRxAdd order: allergy → drug safety → duplicate (ConsultationPage.Safety.cs)
            if (!RxSafeToAdd(rxNew.GenericName.Trim())) return;
            //JJQR - END
            //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
            if (RxIsDuplicate(rxNew.GenericName)) { err.Text = "Item already exist."; err.IsVisible = true; return; }
            //JJQR - END
            rxNew.GenericName = rxNew.GenericName.Trim();
            rxNew.Quantity = rxNew.Quantity.Trim();
            rxNew.Dosage = (rxNew.Dosage ?? "").Trim();
            if (string.IsNullOrWhiteSpace(rxNew.Route)) rxNew.Route = "PO";
            if (rxNew.MedicineId is null && _medicines.FirstOrDefault(m => m.GenericName.Equals(rxNew.GenericName, StringComparison.OrdinalIgnoreCase)) is { } m) rxNew.MedicineId = m.MedicineId;
            if (_rxEditOriginal is not null) { D.Prescriptions.Insert(Math.Min(Math.Max(0, _rxEditIndex), D.Prescriptions.Count), rxNew); _rxEditOriginal = null; } else D.Prescriptions.Add(rxNew); //JJQR - AI (Nov 21, 2026) - an edited medicine goes back where it was //JJQR - END
            //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
            if (_rxAddToFav) _ = AddFavoriteAsync(rxNew);
            //JJQR - END
            D.NoMedsConfirmed = false; // auto-unchecks once there are items
            _rxNew = NewRx();
            RenderSection(); Changed();
        }, "PrimaryButton"));
    }

    //JJQR - AI (Nov 21, 2026) - search box on the prescription Templates tab: filters live by template name or any medicine in it
    string _tplSearch = "";
    void RenderRxTemplates()
    {
        if (_rxTemplates.Count == 0) { SectionHost.Children.Add(ClinicTheme.Text("No prescription templates yet.", "MutedBody")); return; }
        var search = new Entry { Text = _tplSearch, Placeholder = "Search templates or medicines…", HorizontalOptions = LayoutOptions.Fill };
        var count = ClinicTheme.Text("", "MutedSm");
        var list = new VerticalStackLayout { Spacing = 8 };
        void Fill()
        {
            list.Children.Clear();
            var q = (_tplSearch ?? "").Trim();
            var hits = _rxTemplates.Where(t => q.Length == 0
                || t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || t.Lines.Any(l => l.GenericName.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            count.Text = q.Length == 0 ? $"{_rxTemplates.Count} templates" : $"{hits.Count} of {_rxTemplates.Count} templates";
            if (hits.Count == 0) { list.Children.Add(ClinicTheme.Text("No templates match your search.", "MutedBody")); return; }
            foreach (var t in hits)
            {
                var tpl = t;
                var lines = string.Join("\n", t.Lines.Select(l => $"{l.GenericName} {l.Dosage} #{l.Quantity}".Trim()));
                list.Children.Add(Card(ClinicTheme.Text(t.Title, "LabelLg"), ClinicTheme.Text(lines, "MutedSm"),
                    Btn("Use template", () =>
                    {
                        //JJQR - AI (21 Nov 2026) - web handleTemplateAddAll: skip allergy / safety-block / duplicate lines and report the result
                        ApplyRxTemplateSafely(tpl.Title, tpl.Lines.ToList());
                        //JJQR - END
                        _rxTab = "new";
                        RenderSection(); Changed();
                    })));
            }
        }
        search.TextChanged += (_, e) => { _tplSearch = e.NewTextValue ?? ""; Fill(); };
        SectionHost.Children.Add(search);
        SectionHost.Children.Add(count);
        SectionHost.Children.Add(list);
        Fill();
    }
    //JJQR - END

    static RxLine Clone(RxLine l) => new()
    {
        Id = l.Id, MedicineId = l.MedicineId, GenericName = l.GenericName, Dosage = l.Dosage, Quantity = l.Quantity, Instruction = l.Instruction,
        Timing = l.Timing, MealRelation = l.MealRelation, DurationKind = l.DurationKind, DurationValue = l.DurationValue,
        Indication = l.Indication, Route = l.Route, AsNeeded = l.AsNeeded, IsControlledSubstance = l.IsControlledSubstance,
    };

    // Lab Orders: catalogue checkboxes (lab_test_id) + additional tests
    void RenderLabs()
    {
        var labStart = SectionHost.Children.Count; //JJQR - AI (Nov 25, 2026) - owner: split the step 75% tests / 25% uploads (see SplitLabsWithUploads, end of this method) //JJQR - END
        //JJQR - AI (21 Nov 2026) - renal cue (ConsultationPage.Safety.cs)
        RenderRenalCue();
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        RenderAncPresets();
        //JJQR - END
        if (_labTests.Count > 0)
        {
            SectionHost.Children.Add(new Label { Text = "Standard tests", FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, TextColor = ClinicTheme.Color("OnSurface"), Margin = new Thickness(0, 6, 0, 0) }); //JJQR - AI (Nov 21, 2026) - group title //JJQR - END
            //JJQR - AI (Nov 21, 2026) - lab tests in an ALIGNED grid: 4 equal columns, each test one cell that is the whole click target; ticked cells are highlighted
            const int labColumns = 4;
            var labGrid = new Grid { ColumnSpacing = 10, RowSpacing = 10 };
            for (var ci = 0; ci < labColumns; ci++) labGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var labIndex = 0;
            foreach (var t in _labTests)
            {
                var test = t;
                var box = new CheckBox { IsChecked = D.LabOrders.Any(l => l.LabTestId == test.Id || l.TestName.Equals(test.Name, StringComparison.OrdinalIgnoreCase)), InputTransparent = true, VerticalOptions = LayoutOptions.Center };
                var name = new Label { Text = test.Name, FontSize = 14, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
                var inner = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
                inner.Add(box, 0, 0);
                inner.Add(name, 1, 0);
                var cell = new Border { StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Padding = new Thickness(10, 6), Content = inner };
                void Paint(bool on, bool hover = false)
                {
                    cell.BackgroundColor = on ? ClinicTheme.Color("PrimarySubtle") : hover ? ClinicTheme.Color("SurfaceContainerLow") : ClinicTheme.Color("SurfaceContainerLowest");
                    cell.Stroke = on ? ClinicTheme.Color("Primary") : ClinicTheme.Color("OutlineVariant");
                    name.FontFamily = on ? ClinicTheme.FontSemiBold : ClinicTheme.Font;
                }
                Paint(box.IsChecked);
                box.CheckedChanged += (_, e) =>
                {
                    Paint(e.Value);
                    if (e.Value) { if (!D.LabOrders.Any(l => l.LabTestId == test.Id)) D.LabOrders.Add(new LabLine { LabTestId = test.Id, TestName = test.Name }); }
                    else D.LabOrders.RemoveAll(l => l.LabTestId == test.Id || (l.LabTestId is null && l.TestName.Equals(test.Name, StringComparison.OrdinalIgnoreCase)));
                    Changed();
                };
                cell.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => box.IsChecked = !box.IsChecked) });
                var hv = new PointerGestureRecognizer();
                hv.PointerEntered += (_, _) => { if (!box.IsChecked) Paint(false, true); };
                hv.PointerExited += (_, _) => Paint(box.IsChecked);
                cell.GestureRecognizers.Add(hv);
                labGrid.Add(cell, labIndex % labColumns, labIndex / labColumns);
                labIndex++;
            }
            SectionHost.Children.Add(labGrid);
            //JJQR - END
        }
        var catalogIds = _labTests.Select(t => t.Id).ToHashSet();
        var extra = D.LabOrders.Where(l => l.LabTestId is null || !catalogIds.Contains(l.LabTestId.Value)).ToList();
        SectionHost.Children.Add(new Label { Text = "Additional tests", FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, TextColor = ClinicTheme.Color("OnSurface"), Margin = new Thickness(0, 6, 0, 0) }); //JJQR - AI (Nov 21, 2026) - group title //JJQR - END
        foreach (var l in extra)
        {
            var line = l;
            var detail = string.Join(" · ", new[] { l.ClinicalIndication ?? l.Reason, l.SpecimenType, l.Notes }.Where(s => !string.IsNullOrWhiteSpace(s)));
            SectionHost.Children.Add(Row(ClinicTheme.Text(l.TestName + (detail.Length > 0 ? "\n" + detail : ""), "BodyMd"),
                Btn("Remove", () => { D.LabOrders.Remove(line); RenderSection(); Changed(); })));
        }
        if (D.LabOrders.Count == 0) SectionHost.Children.Add(ClinicTheme.Text("No tests selected (optional). Saved with the consultation.", "MutedSm"));
        var other = AddEntry("Add another test", "");
        var reason = AddEntry("Reason / clinical indication", "");
        var specimen = AddEntry("Specimen type", "");
        var notes = AddEntry("Notes", "");
        SectionHost.Children.Add(Btn("Add test", () =>
        {
            if (string.IsNullOrWhiteSpace(other.Text)) return;
            D.LabOrders.Add(new LabLine
            {
                TestName = other.Text.Trim(),
                ClinicalIndication = string.IsNullOrWhiteSpace(reason.Text) ? null : reason.Text.Trim(),
                SpecimenType = string.IsNullOrWhiteSpace(specimen.Text) ? null : specimen.Text.Trim(),
                Notes = string.IsNullOrWhiteSpace(notes.Text) ? null : notes.Text.Trim(),
            });
            RenderSection(); Changed();
        }));
        var printRow = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - both open the web "Confirm Lab Orders" dialog first
        printRow.Children.Add(Btn("Save & Print Lab Request", () => ConfirmLabOrders(print: true), "PrimaryButton"));
        printRow.Children.Add(Btn("Save without printing", () => ConfirmLabOrders(print: false)));
        //JJQR - END
        SectionHost.Children.Add(printRow);
        SplitLabsWithUploads(labStart); //JJQR - AI (Nov 25, 2026) - owner: 75% the selection, 25% the uploads //JJQR - END
    }

    void RenderVaccinations()
    {
        //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
        RenderNipBlock();
        //JJQR - END
        foreach (var v in D.Vaccinations.ToList())
        {
            var line = v;
            var x = v.Details();
            //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - "{name} — Dose #{n} · route · site · Lot x" with a × button
            var text = $"{v.VaccineName} — Dose #{v.DoseNumber}"
                + (string.IsNullOrWhiteSpace(x.Route) ? "" : $" · {x.Route}")
                + (string.IsNullOrWhiteSpace(x.Site) ? "" : $" · {x.Site}")
                + (string.IsNullOrWhiteSpace(x.LotNumber) ? "" : $" · Lot {x.LotNumber}");
            SectionHost.Children.Add(new Border
            {
                StrokeThickness = 0, BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"), Padding = new Thickness(12, 4), Margin = new Thickness(0, 0, 0, 4),
                Content = Row(ClinicTheme.Text(text, "BodyMd"), Btn("×", () => { D.Vaccinations.Remove(line); RenderSection(); Changed(); }, "GhostButton")),
            });
            //JJQR - END
        }
        if (D.Vaccinations.Count == 0) SectionHost.Children.Add(ClinicTheme.Text("No vaccinations staged (optional).", "MutedSm"));
        var name = AddEntry("Vaccine Name", "");
        var dose = AddEntry("Dose #", "");
        dose.Keyboard = Keyboard.Numeric;
        var route = AddEntry("Route", "");
        var site = AddEntry("Site", "");
        var lot = AddEntry("Lot #", "");
        SectionHost.Children.Add(ClinicTheme.Text("Expiry", "FieldLabel"));
        var expiry = new DatePicker { Date = null };
        SectionHost.Children.Add(DateBox(expiry)); //JJQR - AI (Nov 21, 2026) //JJQR - END
        var maker = AddEntry("Manufacturer", "");
        SectionHost.Children.Add(Btn("Stage Vaccination", () =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) return;
            var line = new VaccinationLine { VaccineName = name.Text.Trim(), DoseNumber = short.TryParse(dose.Text, out var n) ? n : null };
            var x = line.Details();
            x.Route = Nb(route.Text); x.Site = Nb(site.Text); x.LotNumber = Nb(lot.Text); x.Manufacturer = Nb(maker.Text);
            x.ExpiryDate = expiry.Date is { } dt ? DateOnly.FromDateTime(dt) : null;
            D.Vaccinations.Add(line);
            RenderSection(); Changed();
        }));
    }

    // Follow-up: Schedule date / PRN / No follow-up (web applyFollowUpMode)
    void RenderFollowUp()
    {
        var radios = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
        foreach (var (mode, label) in new[] { (FollowUpMode.Dated, "Schedule date"), (FollowUpMode.Prn, "PRN / return if needed"), (FollowUpMode.None, "No follow-up") })
        {
            var m = mode;
            radios.Children.Add(Pill(label, D.FollowUpMode == m, () => { ApplyFollowUpMode(m); RenderSection(); Changed(); }));
        }
        SectionHost.Children.Add(radios);
        switch (D.FollowUpMode)
        {
            case FollowUpMode.Dated:
                var date = new DatePicker { Date = D.FollowUpDate?.ToDateTime(TimeOnly.MinValue), MinimumDate = DateTime.Today };
                date.DateSelected += (_, e) => { D.FollowUpDate = e.NewDate is { } nd ? DateOnly.FromDateTime(nd) : null; Changed(); };
                SectionHost.Children.Add(DateBox(date)); //JJQR - AI (Nov 21, 2026) //JJQR - END
                if (D.FollowUpDate is null) SectionHost.Children.Add(ClinicTheme.Text("Choose the follow-up date.", "MutedSm"));
                Bind(AddEntry("Reason", D.FollowUpReason), v => D.FollowUpReason = v);
                Bind(AddEditor("Instructions", D.FollowUpInstructions), v => D.FollowUpInstructions = v);
                var remind = new CheckBox { IsChecked = D.FollowUpReminder };
                remind.CheckedChanged += (_, e) => { D.FollowUpReminder = e.Value; Changed(); };
                SectionHost.Children.Add(WrapCheck(remind, "Send reminder"));
                break;
            case FollowUpMode.Prn:
                SectionHost.Children.Add(ClinicTheme.Text("Saves today's date with PRN instructions — patient may return if symptoms recur.", "MutedSm"));
                Bind(AddEditor("Instructions", D.FollowUpInstructions), v => D.FollowUpInstructions = v);
                break;
            default:
                SectionHost.Children.Add(ClinicTheme.Text("No follow-up will be scheduled for this visit.", "MutedSm"));
                break;
        }
    }

    void ApplyFollowUpMode(FollowUpMode mode)
    {
        var wasPrn = D.FollowUpMode == FollowUpMode.Prn;
        D.FollowUpMode = mode;
        if (mode == FollowUpMode.Prn)
        {
            D.FollowUpDate = ClinicClock.Today;
            D.FollowUpReason = ConsultRules.PrnReason;
            D.FollowUpInstructions = ConsultRules.PrnInstructions;
            D.FollowUpReminder = false;
        }
        else if (mode == FollowUpMode.Dated && wasPrn)
        {
            D.FollowUpDate = null;
            if (D.FollowUpReason == ConsultRules.PrnReason) D.FollowUpReason = null;
            if (D.FollowUpInstructions == ConsultRules.PrnInstructions) D.FollowUpInstructions = null;
            D.FollowUpReminder = true;
        }
        else if (mode == FollowUpMode.Dated) D.FollowUpReminder = true;
    }

    // Medical Certificate (§16.8 Form 2)
    void RenderCertificate()
    {
        var w = _workspace!;
        var age = w.PatientDateOfBirth is { } dob ? $", {AgeOn(dob, ClinicClock.Today)}y" : "";
        var docName = string.IsNullOrWhiteSpace(w.DoctorName) || w.DoctorName == "Doctor" ? (_doctor?.FullName is { Length: > 0 } f ? f : "Doctor") : w.DoctorName;
        //JJQR - AI (Nov 21, 2026) - top action bar: who the certificate is for (left) and the main actions (right), so it is obvious what to press
        var who = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = $"{w.PatientName}{age}{(string.IsNullOrWhiteSpace(w.PatientSex) ? "" : ", " + w.PatientSex)}", FontFamily = ClinicTheme.FontSemiBold, FontSize = 15 },
                ClinicTheme.Text($"{docName} · License No. {(_doctor?.LicenseNumber?.Trim() is { Length: > 0 } l ? l : "—")} · PTR No. {(_doctor?.PtrNumber?.Trim() is { Length: > 0 } p ? p : "—")}", "MutedSm"),
            },
        };
        var bar = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        bar.Add(who, 0, 0);
        var barActions = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center };
        // Issue & print / Save certificate are in the top bar; here only the quiet "Remove certificate" when one exists
        if (MedCertRequested())
        {
            var rm = Btn("Remove certificate", async () => await RemoveCertificateAsync(), "GhostButton");
            rm.Margin = new Thickness(0);
            barActions.Children.Add(rm);
        }
        bar.Add(barActions, 1, 0);
        //JJQR - AI (Nov 22, 2026) - a certificate without the doctor's License / PTR number is not valid when printed: say so before it is issued //JJQR - END
        var missingLicense = string.IsNullOrWhiteSpace(_doctor?.LicenseNumber);
        var missingPtr = string.IsNullOrWhiteSpace(_doctor?.PtrNumber) && (_gapServices?.GetService<ClinicNode.Core.Printing.IPtrPrintChoice>()?.IsOn(D.BookingId, ClinicNode.Core.Printing.ClinicPrintKind.MedicalCertificate) ?? false); //JJQR - AI (Nov 24, 2026) - the PTR only matters when "Include PTR in print" is ticked //JJQR - END
        if (missingLicense || missingPtr)
            SectionHost.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#FFF4D6"),
                StrokeThickness = 0,
                Padding = new Thickness(12, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Content = ClinicTheme.Text($"Your {(missingLicense && missingPtr ? "License No. and PTR No. are" : missingLicense ? "License No. is" : "PTR No. is")} missing, so the printed certificate will show a blank. Enter {(missingLicense && missingPtr ? "them" : "it")} under Profile before issuing.", "MutedSm"),
            });
        SectionHost.Children.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#E7F6F2"),
            Stroke = ClinicTheme.Color("Primary").WithAlpha(0.45f),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(16, 12),
            Content = bar,
        });
        SectionHost.Children.Add(ClinicTheme.Text($"§16.8 Form 2. Blank fields fall back to the SOAP Assessment / Plan and the follow-up date. Issuing marks “medical certificate” on the fee (+{FeeCalculator.FormatPeso(_settings.FeeMedCert)}).", "MutedSm"));
        //JJQR - END

        //JJQR - AI (Nov 21, 2026) - template tools in their own quiet panel
        var tools = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
        if (_certTemplates.Count > 0)
            tools.Children.Add(PickButton("Use template…", _certTemplates.Select(t => t.Title).ToList(), async i => await ApplyCertTemplateAsync(_certTemplates[i])));
        var saveTpl = Btn("Save as template", async () => await SaveCertTemplateAsync());
        saveTpl.IsEnabled = !string.IsNullOrWhiteSpace(D.McDiagnosis) || !string.IsNullOrWhiteSpace(D.McRecommendations) || !string.IsNullOrWhiteSpace(D.McPurposeException);
        tools.Children.Add(saveTpl);
        tools.Children.Add(ManageLink());
        SectionHost.Children.Add(new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"),
            Stroke = ClinicTheme.Color("OutlineVariant"),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(14, 10, 14, 2),
            Content = new VerticalStackLayout { Spacing = 6, Children = { ClinicTheme.Text("TEMPLATES", "LabelSm"), tools } },
        });
        //JJQR - END

        var gAddr = SectionHost.Children.Count; //JJQR - AI (Nov 21, 2026) //JJQR - END
        var addr = AddEntry("Patient address", D.McAddress);
        if (string.IsNullOrWhiteSpace(D.McAddress)) addr.Placeholder = "No address on file";
        Bind(addr, v => D.McAddress = v);
        Bind(AddEntry("Examined at", D.McExaminedAt), v => D.McExaminedAt = v);
        GroupColumns(gAddr, 2, 2); //JJQR - AI (Nov 21, 2026) //JJQR - END
        var gDates = SectionHost.Children.Count; //JJQR - AI (Nov 21, 2026) //JJQR - END
        SectionHost.Children.Add(ClinicTheme.Text("Examination from", "FieldLabel"));
        var from = new DatePicker { Date = D.McExamFrom?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today };
        from.DateSelected += (_, e) => { D.McExamFrom = e.NewDate is { } d ? DateOnly.FromDateTime(d) : null; _mcComeBackStale = true; ClinicNode.App.Controls.UiTurn.Next(RenderSection); Changed(); };
        SectionHost.Children.Add(DateBox(from)); //JJQR - AI (Nov 21, 2026) //JJQR - END
        SectionHost.Children.Add(ClinicTheme.Text("Examination until", "FieldLabel"));
        var until = new DatePicker { Date = D.McExamTo?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today };
        until.DateSelected += (_, e) => { D.McExamTo = e.NewDate is { } d ? DateOnly.FromDateTime(d) : null; _mcComeBackStale = true; ClinicNode.App.Controls.UiTurn.Next(RenderSection); Changed(); };
        SectionHost.Children.Add(DateBox(until)); //JJQR - AI (Nov 21, 2026) //JJQR - END
        GroupColumns(gDates, 2, 2); //JJQR - AI (Nov 21, 2026) //JJQR - END
        var gDx = SectionHost.Children.Count; //JJQR - AI (Nov 21, 2026) //JJQR - END

        var dxField = AddEditor("Diagnosis / Impressions", _mcDxDirty || !string.IsNullOrWhiteSpace(D.McDiagnosis) ? D.McDiagnosis : McDiagnosisDefault());
        dxField.Placeholder = string.IsNullOrWhiteSpace(D.Assessment) ? "Diagnosis / Impressions" : $"Diagnosis / Impressions (default: “{D.Assessment}”)";
        dxField.TextChanged += (_, e) => { _mcDxDirty = true; D.McDiagnosis = e.NewTextValue; Changed(); };
        var recs = AddEditor("Recommendations", D.McRecommendations);
        recs.Placeholder = string.IsNullOrWhiteSpace(D.Plan) ? "Recommendations" : $"Recommendations (default: “{D.Plan}”)";
        Bind(recs, v => D.McRecommendations = v);
        GroupColumns(gDx, 2, 2); //JJQR - AI (Nov 21, 2026) //JJQR - END
        Bind(AddEntry("Purpose exception (the “except ___” blank)", D.McPurposeException), v => D.McPurposeException = v);

        SectionHost.Children.Add(ClinicTheme.Text("Please come back on", "FieldLabel"));
        var comeBack = new DatePicker { Date = D.McComeBackOn?.ToDateTime(TimeOnly.MinValue) };
        comeBack.DateSelected += (_, e) => { D.McComeBackOn = e.NewDate is { } d ? DateOnly.FromDateTime(d) : null; _mcComeBackStale = false; ClinicNode.App.Controls.UiTurn.Next(RenderSection); Changed(); };
        var before = ConsultRules.ComeBackBeforeUntil(D.McComeBackOn, D.McExamTo);
        if (before || _mcComeBackStale) comeBack.BackgroundColor = ClinicTheme.Color("StatusWarningSurface");
        SectionHost.Children.Add(DateBox(comeBack)); //JJQR - AI (Nov 21, 2026) //JJQR - END
        if (D.McComeBackOn is not null)
            SectionHost.Children.Add(Btn("Clear date", () => { D.McComeBackOn = null; _mcComeBackStale = false; RenderSection(); Changed(); }, "GhostButton"));
        if (before || _mcComeBackStale)
        {
            var hint = before ? "This date is before the last examination day — please check it."
                : D.McComeBackOn is not null ? "You changed the examination dates — please check or update this date."
                : "You changed the examination dates — please choose when the patient should come back.";
            var lbl = ClinicTheme.Text(hint, "BodyMd");
            lbl.TextColor = ClinicTheme.Color("StatusWarning");
            SectionHost.Children.Add(lbl);
            if (!before && D.McComeBackOn is { } keep)
                SectionHost.Children.Add(Btn($"Keep {keep.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}", () => { _mcComeBackStale = false; RenderSection(); }, "GhostButton"));
        }
    }

    async Task ApplyCertTemplateAsync(CertTemplateItem t)
    {
        var applyDx = !string.IsNullOrWhiteSpace(t.DiagnosisText) && ConsultRules.ShouldApplyTemplateDiagnosis(D.Diagnoses.Select(DxText), D.Assessment);
        var overwrites = (applyDx && !string.IsNullOrWhiteSpace(D.McDiagnosis) && D.McDiagnosis != t.DiagnosisText)
            || (!string.IsNullOrWhiteSpace(D.McRecommendations) && D.McRecommendations != t.Recommendations)
            || (!string.IsNullOrWhiteSpace(D.McPurposeException) && D.McPurposeException != t.PurposeException);
        if (overwrites && !await DisplayAlertAsync("Replace certificate text?", $"“{t.Title}” will replace the certificate text you typed.", "Replace", "Cancel")) return;
        if (applyDx) { D.McDiagnosis = t.DiagnosisText; _mcDxDirty = true; }
        if (!string.IsNullOrWhiteSpace(t.Recommendations)) D.McRecommendations = t.Recommendations;
        if (!string.IsNullOrWhiteSpace(t.PurposeException)) D.McPurposeException = t.PurposeException;
        RenderSection(); Changed();
    }

    async Task SaveCertTemplateAsync()
    {
        var title = await DisplayPromptAsync("Save Certificate Template",
            "Title (e.g. \"Fit to Work\", \"Sick Leave 3 days\")\nSaves the Diagnosis/Impressions, Recommendations, and Purpose exception fields currently filled in — not the dates or the patient's address.",
            "Save", "Cancel", "Title", 200);
        if (string.IsNullOrWhiteSpace(title)) return;
        try
        {
            var saved = await _lib.SaveCertTemplateAsync(D.DoctorId, title, D.McDiagnosis, D.McRecommendations, D.McPurposeException);
            _certTemplates = _certTemplates.Append(saved).OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToList();
            ShowToast($"Saved template “{saved.Title}”.", null, ++_noticeSeq);
            RenderSection();
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    void ConfirmCertificate(bool print)
    {
        var dx = _mcDxDirty || !string.IsNullOrWhiteSpace(D.McDiagnosis) ? D.McDiagnosis : McDiagnosisDefault();
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web dialog shows Diagnosis, Recommendations and Come back on only; "Go Back"
        var comeBack = (D.McComeBackOn ?? (D.FollowUpMode == FollowUpMode.Dated ? D.FollowUpDate : null))?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        var webBody = new List<View>
        {
            Strong("Diagnosis", string.IsNullOrWhiteSpace(dx) ? "—" : dx),
            Strong("Recommendations", string.IsNullOrWhiteSpace(D.McRecommendations) ? (string.IsNullOrWhiteSpace(D.Plan) ? "—" : D.Plan) : D.McRecommendations),
        };
        if (comeBack is not null) webBody.Add(Strong("Come back on", comeBack));
        ShowOverlay("Confirm Medical Certificate", webBody,
            ("Go Back", HideOverlay, "SecondaryButton"),
            (print ? "Confirm & Print" : "Confirm & Save", () => { HideOverlay(); _ = IssueCertificateAsync(print); }, "PrimaryButton"));
        return;
        //JJQR - END
#pragma warning disable CS0162
        var body = new List<View>
        {
            ClinicTheme.Text($"Patient: {_workspace!.PatientName}", "BodyMd"),
            ClinicTheme.Text($"Address: {(string.IsNullOrWhiteSpace(D.McAddress) ? "—" : D.McAddress)}", "BodyMd"),
            ClinicTheme.Text($"Examined at: {(string.IsNullOrWhiteSpace(D.McExaminedAt) ? ConsultRules.McExaminedAtDefault : D.McExaminedAt)}", "BodyMd"),
            ClinicTheme.Text($"Examination: {D.McExamFrom:MMM d, yyyy} – {(D.McExamTo ?? D.McExamFrom):MMM d, yyyy}", "BodyMd"),
            ClinicTheme.Text($"Diagnosis / Impressions: {(string.IsNullOrWhiteSpace(dx) ? "—" : dx)}", "BodyMd"),
            ClinicTheme.Text($"Recommendations: {(string.IsNullOrWhiteSpace(D.McRecommendations) ? (string.IsNullOrWhiteSpace(D.Plan) ? "—" : D.Plan) : D.McRecommendations)}", "BodyMd"),
            ClinicTheme.Text($"Purpose exception: {(string.IsNullOrWhiteSpace(D.McPurposeException) ? "—" : D.McPurposeException)}", "BodyMd"),
            ClinicTheme.Text($"Please come back on: {(D.McComeBackOn ?? (D.FollowUpMode == FollowUpMode.Dated ? D.FollowUpDate : null))?.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) ?? "—"}", "BodyMd"),
        };
        ShowOverlay("Confirm Medical Certificate", body,
            ("Cancel", HideOverlay, "SecondaryButton"),
            (print ? "Confirm & Print" : "Confirm & Save", () => { HideOverlay(); _ = IssueCertificateAsync(print); }, "PrimaryButton"));
#pragma warning restore CS0162
    }

    async Task IssueCertificateAsync(bool print)
    {
        if (_busy) return;
        var wasMissing = _patientAddressMissing;
        D.CertificateExists = true;
        if (!_mcDxDirty && string.IsNullOrWhiteSpace(D.McDiagnosis)) D.McDiagnosis = McDiagnosisDefault();
        if (string.IsNullOrWhiteSpace(D.McRecommendations) && !string.IsNullOrWhiteSpace(D.Plan)) D.McRecommendations = D.Plan;
        D.McComeBackOn ??= D.FollowUpMode == FollowUpMode.Dated ? D.FollowUpDate : null;
        FollowTotal(); // certificate issued: the fee page ticks it and the amount updates by itself
        var ok = await SaveAsync(goNext: false, silent: true);
        if (!ok) return;
        if (wasMissing && !string.IsNullOrWhiteSpace(D.McAddress))
        {
            try { if (await _lib.UpdatePatientAddressAsync(D.PatientId, D.McAddress!.Trim())) _patientAddressMissing = false; }
            catch { /* best-effort: the certificate still issues */ }
        }
        ShowToast($"Certificate saved at {DateTime.Now:h:mm tt}.", null, ++_noticeSeq);
        if (print) await PrintAsync("Print Certificate", id => _printDocs.MedicalCertificateAsync(id), saveFirst: false);
    }

    async Task RemoveCertificateAsync()
    {
        if (!await DisplayAlertAsync("Remove certificate", "Remove the medical certificate from this visit?", "Remove", "Cancel")) return;
        D.CertificateExists = false;
        D.McDiagnosis = D.McRecommendations = D.McPurposeException = null;
        D.McComeBackOn = null;
        D.McExamFrom = D.McExamTo = ClinicClock.Today;
        _mcDxDirty = false;
        _medCertManual = null; // the certificate is gone: back to following it
        FollowTotal();
        await SaveAsync(goNext: false, silent: true);
        RenderSection();
    }

    // Professional Fee Decision — the LAST required step
    private Label? _feeBanner, _feeSummary, _pfDiffers;
    private Button? _pfUseTotal;

    //JJQR - AI (Nov 21, 2026) - Professional Fee Decision, laid out professionally: big amount banner on top; LEFT = visit details (type, discount, certificate); RIGHT = the decision (Charge / Waive, amount, note).
    // Presentation only: every state field and handler (pfEdited / pfOverrideBase / followTotal / medCertManual) is unchanged.
    Label? _feeBig;
    void RenderFee()
    {
        static View Labeled(string label, View v) => new VerticalStackLayout { Spacing = 6, Children = { ClinicTheme.Text(label, "FieldLabel"), v } };
        static Label CardTitle(string t) => new() { Text = t, FontFamily = ClinicTheme.FontSemiBold, FontSize = 16 };
        static Border Panel(View content) => new()
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"), Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Padding = new Thickness(20, 18), VerticalOptions = LayoutOptions.Start, Content = content,
        };

        // ---- banner: the amount is the hero ----
        _feeBig = new Label { FontFamily = ClinicTheme.FontSemiBold, FontSize = 34, TextColor = ClinicTheme.Color("Primary") };
        _feeBanner = ClinicTheme.Text("", "BodyMd");
        SectionHost.Children.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#E7F6F2"), Stroke = ClinicTheme.Color("Primary").WithAlpha(0.4f), StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Padding = new Thickness(22, 16),
            Content = new VerticalStackLayout { Spacing = 2, Children = { ClinicTheme.Text("FINAL STEP · PROFESSIONAL FEE", "LabelSm"), _feeBig, _feeBanner } },
        });

        // ---- left: visit details ----
        var visit = ClinicNode.App.Views.Doctor.Extra.DoctorExtraUi.Segmented(["New", "Follow-up"], D.VisitType == VisitType.FollowUp ? 1 : 0,
            i => { D.VisitType = i == 1 ? VisitType.FollowUp : VisitType.New; FollowTotal(); RenderVisitTypeBar(); RenderSection(); Changed(); });
        visit.HorizontalOptions = LayoutOptions.Start;
        var discount = ClinicNode.App.Views.Doctor.Extra.DoctorExtraUi.Segmented(["None", "Senior citizen", "PWD"], D.DiscountCategory == "PWD" ? 2 : D.DiscountCategory == "Senior" ? 1 : 0,
            i => { D.DiscountCategory = i switch { 1 => "Senior", 2 => "PWD", _ => null }; FollowTotal(); RenderSection(); Changed(); });
        discount.HorizontalOptions = LayoutOptions.Start;
        var medCert = new CheckBox { IsChecked = MedCertRequested() };
#if WINDOWS
        //JJQR - AI (Nov 23, 2026) - WINDOWS ONLY: owner's app crashed during a consultation. Redrawing the screen inside the tick/tap that caused it can crash WinUI (WINDOWS-HANDOFF.md item 2); redraw on the next UI turn instead. //JJQR - END
        medCert.CheckedChanged += (_, e) => { _medCertManual = e.Value; FollowTotal(); Dispatcher.Dispatch(RenderSection); Changed(); };
#else
        medCert.CheckedChanged += (_, e) => { _medCertManual = e.Value; FollowTotal(); RenderSection(); Changed(); };
#endif
        var certRow = ClinicPickers.CheckRow(medCert, $"Medical certificate  +{FeeCalculator.FormatPeso(_settings.FeeMedCert)}", "Ticks by itself when you save the Medical Certificate step. You can still change it here.");
        var toCert = Btn("Open the Medical Certificate step", () => GoTo(7), "GhostButton");
        toCert.Margin = new Thickness(0);
        var left = Panel(new VerticalStackLayout
        {
            Spacing = 18,
            Children = { CardTitle("Visit details"), Labeled("Appointment type", visit), Labeled("Discount", discount), Labeled("Extras", new VerticalStackLayout { Spacing = 2, Children = { certRow, toCert } }) },
        });

        // ---- right: the decision ----
        var decision = ClinicNode.App.Views.Doctor.Extra.DoctorExtraUi.Segmented(["Charge", "Waive"], D.PfDecision == "Charge" ? 0 : D.PfDecision == "Waive" ? 1 : -1,
            i => { D.PfDecision = i == 0 ? "Charge" : "Waive"; RenderSection(); Changed(); });
        decision.HorizontalOptions = LayoutOptions.Start;
        var rightCol = new VerticalStackLayout { Spacing = 18 };
        rightCol.Children.Add(CardTitle("Decision"));
        rightCol.Children.Add(Labeled("Professional fee", decision));
        _feeSummary = ClinicTheme.Text("", "MutedBody");
        if (D.PfDecision == "Charge")
        {
            var amount = new Entry { Text = PfShown()?.ToString("0.##", CultureInfo.InvariantCulture), Placeholder = "0", Keyboard = Keyboard.Numeric, FontSize = 18, HorizontalOptions = LayoutOptions.Fill };
            amount.TextChanged += (_, e) =>
            {
                _pfTyped = e.NewTextValue ?? "";
                _pfEdited = true;
                _pfOverrideBase = PreviewFee().Total;
                RefreshFeeLabels(); Changed();
            };
            var amountRow = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
            amountRow.Add(new Label { Text = "₱", FontFamily = ClinicTheme.FontSemiBold, FontSize = 20, VerticalOptions = LayoutOptions.Center, TextColor = ClinicTheme.Color("OnSurfaceVariant") }, 0, 0);
            amountRow.Add(amount, 1, 0);
            rightCol.Children.Add(Labeled("Amount to charge", amountRow));
            var differs = new VerticalStackLayout { Spacing = 4 };
            _pfDiffers = ClinicTheme.Text("", "BodyMd");
            _pfDiffers.TextColor = ClinicTheme.Color("StatusWarning");
            _pfUseTotal = Btn("", () => { FollowTotal(); RenderSection(); Changed(); }, "SecondaryButton");
            _pfUseTotal.Margin = new Thickness(0);
            differs.Children.Add(_pfDiffers);
            differs.Children.Add(_pfUseTotal);
            rightCol.Children.Add(differs);
            rightCol.Children.Add(_feeSummary);
            rightCol.Children.Add(Labeled("Payment", ClinicTheme.Text("Pay at clinic. Collected at the front desk.", "BodyMd")));
        }
        else if (D.PfDecision == "Waive")
        {
            var reason = new Editor { Text = D.PfWaiveReason, HeightRequest = 100, Placeholder = "Why is the fee waived? (minimum 5 characters)", AutoSize = EditorAutoSizeOption.Disabled };
            reason.TextChanged += (_, e) => { D.PfWaiveReason = e.NewTextValue; RefreshFeeLabels(); Changed(); };
            rightCol.Children.Add(Labeled("Reason for waiving *", reason));
            rightCol.Children.Add(ClinicTheme.Text("The front desk will see this visit as ₱0 and confirm the waiver.", "MutedSm"));
        }
        else rightCol.Children.Add(ClinicTheme.Text("Choose Charge or Waive to continue.", "MutedBody"));
        var right = Panel(rightCol);

        var cols = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        cols.Add(left, 0, 0);
        cols.Add(right, 1, 0);
        SectionHost.Children.Add(cols);
        RefreshFeeLabels();
    }
    //JJQR - END

    void RefreshFeeLabels()
    {
        var preview = PreviewFee();
        if (_feeBanner is not null) _feeBanner.Text = ConsultRules.FeeBannerLine(D.PfDecision, PfShown(), D.PfWaiveReason);
        if (_feeBig is not null) _feeBig.Text = D.PfDecision == "Waive" ? "Waived" : D.PfDecision == "Charge" ? FeeCalculator.FormatPeso(PfShown() ?? 0m) : FeeCalculator.FormatPeso(preview.Total); //JJQR - AI (Nov 21, 2026) //JJQR - END
        if (_feeSummary is not null) _feeSummary.Text = preview.Summary;
        if (_pfDiffers is not null && _pfUseTotal is not null)
        {
            var typed = TypedAmount();
            var show = _pfEdited && typed != preview.Total;
            _pfDiffers.IsVisible = _pfUseTotal.IsVisible = show;
            _pfDiffers.Text = $"You set {(typed is { } t ? FeeCalculator.FormatPeso(t) : "no amount")}; the computed total for this visit is {FeeCalculator.FormatPeso(preview.Total)}.";
            _pfUseTotal.Text = $"Use {FeeCalculator.FormatPeso(preview.Total)}";
        }
    }

    // ---------- footer (web: Prev / Next navigate only; Save & next; Review & complete last) ----------
    void UpdateFooter()
    {
        if (_workspace is null) return;
        var missing = Missing();
        var missingNames = string.Join(", ", missing.Select(i => ConsultSteps.Sections[i]));
        PrevButton.IsEnabled = Neighbor(-1) is not null;
        var next = Neighbor(1);
        NextButton.IsEnabled = next is not null;
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - header prints: both show once there is an Rx or a lab order, each disabled when it has nothing to print; no cert/slip in the header
        var hasRx = D.Prescriptions.Any(l => !string.IsNullOrWhiteSpace(l.GenericName));
        var hasLabs = D.LabOrders.Any(l => !string.IsNullOrWhiteSpace(l.TestName));
        PrintRxButton.IsVisible = PrintLabsButton.IsVisible = hasRx || hasLabs;
        PrintRxButton.IsEnabled = hasRx;
        PrintLabsButton.IsEnabled = hasLabs;
        //JJQR - AI (Nov 21, 2026) - "Print medical certificate" is always at the top while a certificate is relevant: on the certificate step, or once one exists
        PrintCertButton.IsVisible = _section == 7 || D.CertificateExists;
        PrintCertButton.Text = D.CertificateExists ? "Print medical certificate" : "Issue & print certificate";
        PrintSlipButton.IsVisible = false;
        SaveCertButton.IsVisible = _section == 7 && _mode != "view"; //JJQR - AI (Nov 21, 2026) - "Save without printing" lives in the top bar next to Issue & print //JJQR - END
        //JJQR - END
        //JJQR - END
        HistoryButton.IsVisible = true;
        HelpButton.IsVisible = _mode != "view"; //JJQR - AI (21 Nov 2026) - web: "?" only while editing //JJQR - END

        AmendButton.IsVisible = _mode == "view" && IsClosed;
        SaveNextButton.IsVisible = _mode != "view";
        CompleteButton.IsVisible = _mode == "complete";
        if (_mode == "amend")
        {
            SaveNextButton.Text = "Save amendment";
            SaveNextButton.IsEnabled = missing.Count == 0 && !_busy;
            //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web save-status wording; Save is primary in amend
            SaveNextButton.Style = ClinicTheme.Style("PrimaryButton");
            StatusLabel.Text = SaveStatusText();
            //JJQR - END
        }
        else if (_mode == "complete")
        {
            SaveNextButton.Text = next is null ? "Save draft" : "Save & next";
            SaveNextButton.IsEnabled = !_busy;
            CompleteButton.IsEnabled = !_busy; //JJQR - AI (Nov 21, 2026) - always clickable: with steps missing it opens the checklist that shows exactly which ones //JJQR - END
            //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web: Save is secondary once Review & complete is available; Review & complete is primary only when it can complete
            SaveNextButton.Style = ClinicTheme.Style(missing.Count == 0 ? "SecondaryButton" : "PrimaryButton");
            CompleteButton.Style = ClinicTheme.Style(missing.Count == 0 ? "PrimaryButton" : "SecondaryButton");
            StatusLabel.Text = missing.Count == 0 ? SaveStatusText() : $"{SaveStatusText()}  ·  Still needed to complete: {missingNames}"; //JJQR - AI (Nov 21, 2026) //JJQR - END
            //JJQR - END
        }
        else StatusLabel.Text = "Viewing a completed visit (read-only).";
        //JJQR - AI (Nov 22, 2026) - owner: show a loading spinner (and "Saving...") whenever Save & next / Save / Next / Prev are working, so the page never looks frozen
        FooterSpinner.IsVisible = FooterSpinner.IsRunning = _busy;
        PrevButton.IsEnabled = !_busy;
        NextButton.IsEnabled = !_busy;
        if (_busy && SaveNextButton.IsVisible) SaveNextButton.Text = "Saving…";
        //JJQR - END
    }

    void OnAmend(object? sender, EventArgs e)
    {
        _mode = "amend";
        _autosave?.Stop();
        RenderAll();
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web info notice on entering amend
        _amendSavedAt = null;
        ShowToast("Manual save only — autosave is off in amend mode.", null, ++_noticeSeq);
        //JJQR - END
    }

    //JJQR - AI (Nov 22, 2026) - QA high #5
    async Task OnBackRequestedAsync()
    {
        if (_mode == "amend" && Dirty && !_busy) { ConfirmDiscard(async () => await Shell.Current.GoToAsync("..")); return; }
        await Shell.Current.GoToAsync("..");
    }

    protected override bool OnBackButtonPressed()
    {
        if (_mode == "amend" && Dirty && !_busy) { ConfirmDiscard(async () => await Shell.Current.GoToAsync("..")); return true; }
        return base.OnBackButtonPressed();
    }
    //JJQR - END

    async void OnCancelAmend(object? sender, EventArgs e)
    {
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web "Discard changes?" dialog (Keep Editing / Discard)
        if (Dirty) { ConfirmDiscard(async () => await DiscardAmendAsync()); return; }
        await DiscardAmendAsync();
    }

    async Task DiscardAmendAsync()
    {
        //JJQR - END
        try
        {
            _workspace = await _consults.LoadByBookingAsync(D.BookingId);
            _mode = "view";
            InitFormState();
            RenderAll();
        }
        catch (Exception ex) { Error(ex.Message); }
    }

    void OnPrev(object? sender, EventArgs e) { if (Neighbor(-1) is int i) GoTo(i); }
    void OnNext(object? sender, EventArgs e) { if (Neighbor(1) is int i) GoTo(i); } // Next only navigates; it never saves.

    async void OnSaveNext(object? sender, EventArgs e)
    {
        var ok = await SaveAsync(goNext: false);
        if (ok && _mode == "complete" && Neighbor(1) is int i) GoTo(i);
    }

    async Task<bool> SaveAsync(bool goNext, bool silent = false)
    {
        if (_busy || _workspace is null || _mode == "view") return false;
        if (_mode == "amend" && !CanComplete)
        {
            Error("Finish " + string.Join(", ", Missing().Select(i => ConsultSteps.Sections[i])) + " to save the amendment.");
            return false;
        }
        SyncFeeToDraft();
        _busy = true;
        UpdateFooter();
        try
        {
            var sent = D;
            var result = _mode == "amend" ? await _consults.AmendAsync(sent) : await _consults.SaveDraftAsync(sent);
            //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web save error wording + "Not saved" status
            if (!result.Succeeded)
            {
                _saveFailed = true;
                Error(result.Error ?? (_mode == "amend" ? "Could not save your changes. Check your connection and try again." : "Could not save the draft. Check your connection and try again."));
                return false;
            }
            _saveFailed = false;
            //JJQR - AI (Nov 22, 2026) - QA high #3: say so when the save only reached THIS computer (clinic server unreachable)
            var wasLocalOnly = _savedLocalOnly;
            _savedLocalOnly = ClinicNode.Api.OnlineFirst.OnlineRouteStatus.LastWriteWasLocalOnly;
            if (_savedLocalOnly && !wasLocalOnly)
            {
                //JJQR - AI (Nov 22, 2026) - step 0.4: a computer that is not registered can no longer reach this line (the save is refused with a clear message), so the notice only needs the connection wording
                ShowToast("Saved on this computer only — the clinic server could not be reached. The changes will be sent when the connection is back.", null, ++_noticeSeq);
            }
            //JJQR - END
            if (_mode == "amend") _amendSavedAt = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ManilaZone()).ToString("h:mm tt", CultureInfo.GetCultureInfo("en-PH")) + " PHT";
            //JJQR - END
            if (result.Draft is { } back && !ReferenceEquals(back, sent)) ConsultDraftExtras.CopyExtras(sent, back);
            _workspace = _workspace with { Draft = result.Draft ?? sent };
            _savedSnapshot = Snapshot();
            _draftSavedAt = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, ManilaZone()).ToString("h:mm:ss tt", CultureInfo.GetCultureInfo("en-PH"));
            ErrorLabel.IsVisible = false;
            //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
            AfterSaved(amended: _mode == "amend");
            //JJQR - END
            if (_mode == "amend" && !silent) ShowToast("Amendment saved.", null, ++_noticeSeq);
            if (goNext && Neighbor(1) is int i) GoTo(i);
            return true;
        }
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web shows its own message, never the raw exception
        catch (Exception)
        {
            _saveFailed = true;
            Error(_mode == "amend" ? "Could not save your changes. Check your connection and try again." : "Could not save the draft. Check your connection and try again.");
            return false;
        }
        //JJQR - END
        finally
        {
            _busy = false;
            RebuildSteps();
            UpdateFooter();
        }
    }

    static TimeZoneInfo ManilaZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"); }
        catch { return TimeZoneInfo.CreateCustomTimeZone("PHT", TimeSpan.FromHours(8), "PHT", "PHT"); }
    }

    // ---------- overlay: history + checklist ----------
    void ShowOverlay(string title, IEnumerable<View> body, params (string Text, Action Do, string Style)[] actions)
    {
        OverlayTitle.Text = title;
        OverlayBody.Children.Clear();
        foreach (var v in body) OverlayBody.Children.Add(v);
        OverlayActions.Children.Clear();
        foreach (var a in actions) OverlayActions.Children.Add(Btn(a.Text, a.Do, a.Style));
        FitOverlay();
        Overlay.IsVisible = true;
    }

    //JJQR - AI (Nov 23, 2026) - owner (Windows laptop): the Completion Checklist's Confirm & Complete row was cut off below the page. The scroll area now gets the page height minus
    // the card margin (56), title bar (~60) and button row (~80), so the buttons always show; the body scrolls instead. Re-fits when the window is resized.
    bool _overlayFitHooked;
    void FitOverlay()
    {
        if (!_overlayFitHooked) { _overlayFitHooked = true; Root.SizeChanged += (_, _) => FitOverlay(); }
        if (Root.Height <= 0) return;
        OverlayScroll.MaximumHeightRequest = Math.Clamp(Root.Height - Root.Padding.VerticalThickness - 200, 160, 560);
    }
    //JJQR - END

    void HideOverlay() => Overlay.IsVisible = false;
    void OnOverlayClose(object? sender, EventArgs e) => HideOverlay(); //JJQR - AI (Nov 21, 2026) - title-bar close //JJQR - END

    //JJQR - AI (Nov 21, 2026) - earlier visits for this patient, newest first, from the live chart (falls back to this computer's copy when the live records can't be reached)
    async Task<IReadOnlyList<PastVisit>> LoadLiveHistoryAsync()
    {
        try
        {
            var doctorId = ClinicNode.App.Views.Doctor.Extra.DoctorExtraUi.DoctorId(_serviceProvider);
            var chart = doctorId is null ? null : await _serviceProvider.GetRequiredService<ClinicNode.Core.DoctorExtra.IDoctorPatientsService>().GetChartAsync(doctorId.Value, D.PatientId);
            if (chart is not null)
            {
                var queue = chart.Bookings.ToDictionary(b => b.BookingId, b => b.QueueNumber);
                int Seq(Guid id) => ClinicNode.App.Views.Doctor.Extra.DoctorPatientChartPage.Seq(queue.GetValueOrDefault(id));
                var names = chart.VitalTemplates.ToDictionary(t => t.TemplateId);
                return chart.Consultations.Where(c => c.BookingId != D.BookingId)
                    .OrderByDescending(c => c.AppointmentDate).ThenByDescending(c => Seq(c.BookingId)).Take(30)
                    .Select(c =>
                    {
                        var ex = chart.VisitExtras?.FirstOrDefault(e => e.BookingId == c.BookingId);
                        var vit = chart.VitalVisits.FirstOrDefault(v => v.BookingId == c.BookingId)?.Values
                            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && names.ContainsKey(kv.Key))
                            .OrderBy(kv => chart.VitalTemplates.ToList().IndexOf(names[kv.Key]))
                            .Select(kv => $"{names[kv.Key].Description} {kv.Value}{(string.IsNullOrEmpty(names[kv.Key].Unit) ? "" : " " + names[kv.Key].Unit)}") ?? [];
                        var rx = chart.Prescriptions.Where(g => g.BookingId == c.BookingId).SelectMany(g => g.Lines).Select(l => $"{l.GenericName} {l.Dosage} #{l.Quantity}");
                        return new PastVisit(c.BookingId, c.AppointmentDate, c.Status.ToString(), c.ChiefComplaint, c.Subjective, c.Objective, c.Assessment, c.Plan, null,
                            string.Join("; ", c.Diagnoses), string.Join(", ", vit), string.Join("; ", rx), string.Join(", ", ex?.LabOrders ?? []));
                    }).ToList();
            }
        }
        catch { /* fall back below */ }
        return await _catalog.GetHistoryAsync(D.PatientId, D.BookingId);
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - Patient history is built from the LIVE chart (the local copy only held the visits this computer had synced) and shows every earlier visit with its full detail
    async void OnHistory(object? sender, EventArgs e)
    {
        var body = new List<View> { ClinicTheme.Text("Loading earlier visits…", "MutedBody") };
        ShowOverlay($"Patient history — {_workspace?.PatientName}", body, ("Close", HideOverlay, "SecondaryButton"));
        try
        {
            var doctorId = ClinicNode.App.Views.Doctor.Extra.DoctorExtraUi.DoctorId(_serviceProvider);
            var chart = doctorId is null ? null : await _serviceProvider.GetRequiredService<ClinicNode.Core.DoctorExtra.IDoctorPatientsService>().GetChartAsync(doctorId.Value, D.PatientId);
            if (chart is not null)
            {
                var cards = ClinicNode.App.Views.Doctor.Extra.ChartVisitCards.Build(chart, D.BookingId);
                ShowOverlay($"Patient history — {_workspace?.PatientName}",
                    cards.Count == 0 ? [ClinicTheme.Text("No earlier visits for this patient.", "MutedBody")] : cards,
                    ("Close", HideOverlay, "SecondaryButton"));
                return;
            }
        }
        catch { /* fall back to the copy on this computer below */ }
        var fallback = new List<View>();
        if (_history.Count == 0) fallback.Add(ClinicTheme.Text("No earlier visits for this patient.", "MutedBody"));
        foreach (var h in _history)
        {
            var lines = new List<View> { ClinicTheme.Text($"{h.Date:MMM d, yyyy} · {h.Status}", "LabelLg") };
            void Add(string label, string? v) { if (!string.IsNullOrWhiteSpace(v)) lines.Add(ClinicTheme.Text($"{label}: {v}", "BodyMd")); }
            Add("Chief complaint", h.ChiefComplaint); Add("Subjective", h.Subjective); Add("Objective", h.Objective); Add("Assessment", h.Assessment); Add("Plan", h.Plan);
            Add("Diagnoses", h.Diagnoses); Add("Vitals", h.Vitals); Add("Prescription", h.Prescriptions); Add("Labs", h.Labs);
            fallback.Add(Card(lines.ToArray()));
        }
        ShowOverlay($"Patient history — {_workspace?.PatientName}", fallback, ("Close", HideOverlay, "SecondaryButton"));
    }
    //JJQR - END

    //JJQR - AI (Nov 25, 2026) - owner: the patient chart during the consult (Lab Results / Documents upload, Reprint). Opened for THIS visit, so uploads are saved with it.
    // Saved first, like before printing, so nothing typed is lost while the chart is open.
    bool _openingChart;
    bool _backFromChart; //JJQR - AI (Nov 25, 2026) - set when the chart opens; OnAppearing then reloads the Lab Orders "Uploaded results" panel //JJQR - END
    async void OnOpenChart(object? sender, EventArgs e)
    {
        if (_openingChart || _workspace is null) return;
        _openingChart = true;
        try
        {
            if (_mode != "view" && Dirty && !await SaveAsync(goNext: false, silent: true)) return;
            await WriteThroughBeforePrintAsync(); // so a reprint of this visit from the chart matches what was just saved
            //JJQR - AI (Nov 25, 2026) - code review: open WITHOUT this booking. With it the chart turns on prescription Edit/Delete and "Add Vitals" (a second consult page),
            // and this consult's autosave would later write its older prescription back. Uploads in the chart are per visit, so nothing is lost.
            _backFromChart = true;
            await ClinicNode.App.Views.Doctor.Extra.DoctorPatientChartPage.OpenAsync(this, D.PatientId);
            //JJQR - END
        }
        catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Consult.OpenChart", ex); }
        finally { _openingChart = false; }
    }
    //JJQR - END

    // Completion Checklist: each visible section with its indicator and what was entered
    void OnComplete(object? sender, EventArgs e)
    {
        if (_busy || _workspace is null) return;
        if (_rxEditOriginal is not null) { RestoreRxEdit(); RenderSection(); } //JJQR - AI (Nov 21, 2026) - never complete while a medicine is half-edited //JJQR - END
        _completeAttempted = true;
        RebuildSteps();
        ErrorLabel.IsVisible = false;
        var shown = PfShown();
        var body = new List<View> { ClinicTheme.Text("Review what was entered before you complete this visit.", "MutedBody") };
        //JJQR - AI (Nov 21, 2026) - one row per section: LEFT = status badge + section name, RIGHT = what was entered, with a hairline between rows (title and content no longer look alike)
        var first = true;
        foreach (var i in ConsultRules.VisibleOrder(_showMore, _section))
        {
            var required = ConsultRules.RequiredSections.Contains(i);
            var ok = ConsultRules.SectionSatisfied(i, D, shown);
            if (!first) body.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") });
            first = false;
            body.Add(ChecklistRow(i, ok, required, ConsultRules.SectionSummary(i, D, shown)));
        }
        //JJQR - END
        //JJQR - AI (Nov 21, 2026) - what still blocks completion, in plain words; the Confirm button stays off until it is done
        var stillMissing = ConsultRules.MissingRequired(D, shown);
        if (stillMissing.Count > 0)
            body.Insert(1, Warning($"Not ready to complete yet. Still needed: {string.Join(", ", stillMissing.Select(i => ConsultSteps.Sections[i]))}. Go back and finish the steps marked !."));
        //JJQR - END
        if (!ConsultRules.SectionSatisfied(3, D, shown)) body.Add(Warning(ConsultRules.ChecklistRxWarning));
        if (!ConsultRules.SectionSatisfied(6, D, shown)) body.Add(Warning(ConsultRules.ChecklistFollowUpWarning));
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - a failed Complete shows its error inside the checklist
        _checklistError = ClinicTheme.Text("", "ErrorText");
        _checklistError.IsVisible = false;
        body.Insert(1, _checklistError); //JJQR - AI (Nov 21, 2026) - at the top, where it is seen without scrolling
        //JJQR - END
        ShowOverlay("Completion Checklist", body,
            ("Go Back", HideOverlay, "SecondaryButton"),
            ("Confirm & Complete", () => _ = CompleteWithFeedbackAsync(), "PrimaryButton"));
        if (stillMissing.Count > 0 && OverlayActions.Children.LastOrDefault() is Button confirmBtn) { confirmBtn.IsEnabled = false; confirmBtn.Opacity = 0.4; } //JJQR - AI (Nov 21, 2026) //JJQR - END
    }

    //JJQR - AI (Nov 21, 2026)
    View ChecklistRow(int section, bool ok, bool required, string summary)
    {
        var good = ok;
        var bad = !ok && required;
        var badgeBg = good ? ClinicTheme.Color("StatusSuccessSurface") : bad ? ClinicTheme.Color("StatusDangerSurface") : ClinicTheme.Color("SurfaceContainer");
        var badgeFg = good ? ClinicTheme.Color("StatusSuccess") : bad ? ClinicTheme.Color("StatusDanger") : ClinicTheme.Color("OnSurfaceVariant");
        var badge = new Border
        {
            WidthRequest = 24, HeightRequest = 24, StrokeThickness = 0, BackgroundColor = badgeBg, VerticalOptions = LayoutOptions.Start,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Content = new Label { Text = good ? "✓" : bad ? "!" : "–", FontFamily = ClinicTheme.FontSemiBold, FontSize = 13, TextColor = badgeFg, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
        };
        var name = new Label { Text = ConsultSteps.Sections[section], FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, TextColor = bad ? ClinicTheme.Color("StatusDanger") : ClinicTheme.Color("OnSurface"), VerticalOptions = LayoutOptions.Start, LineBreakMode = LineBreakMode.WordWrap };
        var left = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        left.Add(badge, 0, 0);
        left.Add(name, 1, 0);

        var right = new VerticalStackLayout { Spacing = 6 };
        if (string.IsNullOrWhiteSpace(summary))
            right.Children.Add(new Label { Text = "Nothing entered.", FontSize = 13, TextColor = ClinicTheme.Color("OnSurfaceVariant") });
        else if (section == 1)
        {
            // vital signs: one chip per reading
            var chips = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap };
            foreach (var part in summary.Split(" · ", StringSplitOptions.RemoveEmptyEntries))
            {
                // label (grey, small caps) + value (dark, bold) inside one chip; the chip never shrinks, so the value is never cut off
                var t = part.Trim();
                //JJQR - AI (Nov 21, 2026) - the value starts at the first digit that follows a space ("O2 SATURATION 97 %": the "2" inside O2 is part of the label)
                var firstDigit = System.Text.RegularExpressions.Regex.Match(t, @"\s\d") is { Success: true } dm ? dm.Index + 1 : t.IndexOfAny("0123456789".ToCharArray());
                //JJQR - END
                var label = firstDigit > 0 ? t[..firstDigit].Trim() : t;
                var value = firstDigit > 0 ? t[firstDigit..].Trim() : "";
                var chip = new Border
                {
                    StrokeThickness = 0, BackgroundColor = ClinicTheme.Color("SurfaceContainer"), Padding = new Thickness(12, 7), Margin = new Thickness(0, 0, 8, 8),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                    Content = new VerticalStackLayout
                    {
                        Spacing = 1,
                        Children =
                        {
                            new Label { Text = label.ToUpperInvariant(), FontSize = 10.5, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("OnSurfaceVariant"), LineBreakMode = LineBreakMode.NoWrap },
                            new Label { Text = value.Length > 0 ? value : "—", FontSize = 15, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("OnSurface"), LineBreakMode = LineBreakMode.NoWrap },
                        },
                    },
                };
                Microsoft.Maui.Controls.FlexLayout.SetShrink(chip, 0f);
                chips.Children.Add(chip);
            }
            right.Children.Add(chips);
        }
        else
        {
            foreach (var line in summary.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = line.IndexOf(':');
                if (colon is > 0 and < 24)
                {
                    var lbl = new Label { Text = line[..colon].ToUpperInvariant(), FontSize = 11, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("OnSurfaceVariant"), CharacterSpacing = 0.4 };
                    var val = new Label { Text = line[(colon + 1)..].Trim(), FontSize = 14, TextColor = ClinicTheme.Color("OnSurface") };
                    right.Children.Add(new VerticalStackLayout { Spacing = 1, Children = { lbl, val } });
                }
                else right.Children.Add(new Label { Text = line.Trim(), FontSize = 14, TextColor = ClinicTheme.Color("OnSurface") });
            }
        }
        var row = new Grid { ColumnSpacing = 20, Padding = new Thickness(0, 14), ColumnDefinitions = { new ColumnDefinition(new GridLength(230)), new ColumnDefinition(GridLength.Star) } };
        row.Add(left, 0, 0);
        row.Add(right, 1, 0);
        return row;
    }
    //JJQR - END

    static View Warning(string text)
    {
        var l = ClinicTheme.Text(text, "BodyMd");
        l.TextColor = ClinicTheme.Color("StatusWarning");
        return new Border { Style = ClinicTheme.Style("Card"), BackgroundColor = ClinicTheme.Color("StatusWarningSurface"), Content = l };
    }

    //JJQR - AI (Nov 23, 2026) - owner (Windows): "Review & complete shows the modal but after that there is no action". The button now shows "Completing…" while it works and any failure is shown in the checklist and written to errors.log
    async Task CompleteWithFeedbackAsync()
    {
        var btn = OverlayActions.Children.OfType<Button>().LastOrDefault();
        var label = btn?.Text;
        if (btn is not null) { btn.IsEnabled = false; btn.Text = "Completing…"; }
        try { await CompleteAsync(); }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("ConsultationPage.Complete", ex);
            ChecklistError("Could not complete the consultation: " + ex.Message);
        }
        finally
        {
            // when the checklist is still open (a failure), give the button back
            if (btn is not null && Overlay.IsVisible && btn.Text == "Completing…") { btn.Text = label ?? "Confirm & Complete"; btn.IsEnabled = true; }
        }
    }
    //JJQR - END

    async Task CompleteAsync()
    {
        if (_busy || _workspace is null) return;
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - errors stay inside the open checklist
        if (!CanComplete)
        {
            ChecklistError("Finish " + string.Join(", ", Missing().Select(i => ConsultSteps.Sections[i])) + " to complete.");
            return;
        }
        //JJQR - END
        SyncFeeToDraft();
        _busy = true;
        try
        {
            var sent = D;
            var result = await _consults.CompleteAsync(sent);
            if (!result.Succeeded) { ChecklistError(result.Error ?? "Could not complete the consultation. Check your connection and try again."); return; } //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 //JJQR - END
            if (result.Draft is { } back && !ReferenceEquals(back, sent)) ConsultDraftExtras.CopyExtras(sent, back);
            _workspace = _workspace with { Draft = result.Draft ?? sent };
            _savedSnapshot = Snapshot();
            _autosave?.Stop();
            _mode = "view";
            //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs)
            AfterSaved(amended: false);
            await LoadAmendmentsAsync();
            //JJQR - END
            //JJQR - AI (Nov 23, 2026) - the visit IS saved at this point: if the "Consultation saved" card cannot be drawn, still close the checklist and show the record
            try { ShowJustCompleted(); }
            catch (Exception ex)
            {
                ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("ConsultationPage.ShowJustCompleted", ex);
                HideOverlay();
                try { RenderAll(); } catch (Exception ex2) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("ConsultationPage.RenderAll", ex2); }
                ShowToast("Consultation saved.", null, ++_noticeSeq);
            }
            //JJQR - END
        }
        catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("ConsultationPage.CompleteAsync", ex); ChecklistError("Could not complete the consultation. Check your connection and try again."); } //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 //JJQR - END
        finally { _busy = false; UpdateFooter(); }
    }

    /// <summary>web justCompleted: "Consultation saved" with the print options and the way back.</summary>
    void ShowJustCompleted()
    {
        //JJQR - AI (21 Nov 2026) - UI pass vs web consultation2 - web justCompleted card (ConsultationPage.Ui.cs)
        ShowJustCompletedWeb();
        return;
        //JJQR - END
#pragma warning disable CS0162
        var shown = PfShown();
        var body = new List<View>
        {
            ClinicTheme.Text($"{_workspace!.PatientName} · {ConsultRules.SectionSummary(8, D, shown)}", "BodyMd"),
            ClinicTheme.Text("The visit is complete and is now on the front desk's payment list.", "MutedSm"),
        };
        var actions = new List<(string, Action, string)>();
        if (D.Prescriptions.Count > 0) actions.Add(("Print Prescription", () => OnPrintRx(null, EventArgs.Empty), "SecondaryButton"));
        if (D.LabOrders.Count > 0) actions.Add(("Print Lab Orders", () => OnPrintLabs(null, EventArgs.Empty), "SecondaryButton"));
        if (D.CertificateExists) actions.Add(("Print Certificate", () => OnPrintCert(null, EventArgs.Empty), "SecondaryButton"));
        actions.Add(("View record", () => { HideOverlay(); RenderAll(); }, "SecondaryButton"));
        actions.Add(("Back to today", async () => { HideOverlay(); await Shell.Current.GoToAsync(".."); }, "PrimaryButton"));
        ShowOverlay("Consultation saved", body, actions.ToArray());
#pragma warning restore CS0162
    }

    // ---------- print (IPrinter only) ----------
    async void OnPrintRx(object? sender, EventArgs e) => await PrintAsync("Print Prescription", id => _printDocs.PrescriptionAsync(id));
    async void OnPrintLabs(object? sender, EventArgs e) => await PrintAsync("Print Lab Orders", id => _printDocs.LabRequestAsync(id));
    void OnSaveCert(object? sender, EventArgs e) => ConfirmCertificate(print: false); //JJQR - AI (Nov 21, 2026) //JJQR - END

    //JJQR - AI (Nov 21, 2026) - one button: prints the certificate, or issues it first when there is none yet
    async void OnPrintCert(object? sender, EventArgs e)
    {
        if (!D.CertificateExists) { ConfirmCertificate(print: true); return; }
        //JJQR - AI (Nov 22, 2026) - the screen said "issued" but the saved copy had no certificate ("No medical certificate has been issued"): save the visit again first so they match //JJQR - END
        await PrintAsync("Print Certificate", id => _printDocs.MedicalCertificateAsync(id), forceSave: true);
    }
    //JJQR - END
    async void OnPrintSlip(object? sender, EventArgs e) => await PrintAsync("Print queue slip", id => _printDocs.QueueSlipAsync(id));

    async Task PrintAsync(string title, Func<Guid, Task<PrintDocument>> build, bool saveFirst = true, bool forceSave = false)
    {
        if (_workspace is null) return;
        // Persist live form lines first so the print document matches what is on screen (web does the same).
        if (saveFirst && _mode != "view" && (Dirty || forceSave) && !await SaveAsync(goNext: false, silent: true)) return;
        if (_busy) return;
        _busy = true;
        try
        {
            //JJQR - AI (21 Nov 2026) - gap close (ConsultationPage.Gaps.cs) - online: copy the server rows into SQLite first so the print matches what was just saved
            await WriteThroughBeforePrintAsync();
            //JJQR - END
            ClinicNode.App.Controls.PrintBusy.Begin(); //JJQR - AI (Nov 24, 2026) - "Preparing the print…" until the print window shows //JJQR - END
            var document = await build(D.BookingId);
            await _printer.PrintAsync(document);
            ShowToast(title + " sent to the printer.", null, ++_noticeSeq);
            ErrorLabel.IsVisible = false;
        }
        catch (Exception ex) { Error(ex.Message); }
        finally { ClinicNode.App.Controls.PrintBusy.End(); _busy = false; UpdateFooter(); }
    }

    // ---------- small builders ----------
    static string? Nb(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    void Bind(InputView view, Action<string?> set) => view.TextChanged += (_, e) => { set(e.NewTextValue); Changed(); };

    static Button Btn(string text, Action onClick, string style = "SecondaryButton")
    {
        var b = new Button { Text = text, Style = ClinicTheme.Style(style), Margin = new Thickness(0, 0, 8, 8) };
        //JJQR - AI (Nov 23, 2026) - a button action that throws used to do nothing at all (owner: "after the modal there is no action"); now the reason goes to errors.log
        b.Clicked += (_, _) =>
        {
            ClinicNode.App.Controls.UiTurn.Next(() =>
            {
                try { onClick(); }
                catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("ConsultationPage.Button:" + text, ex); }
            });
        };
        //JJQR - END
        return b;
    }

    //JJQR - AI (Nov 21, 2026) - desktop dropdown: a button opens a panel that FLOATS over the page (search box + scrollable list), picks once on click and closes. Nothing below it moves.
    View PickButton(string title, IReadOnlyList<string> items, Func<int, Task> onPick, string style = "SecondaryButton")
    {
        var btn = new Button { Text = title.TrimEnd('…', '.') + "  ▾", Style = ClinicTheme.Style(style), Margin = new Thickness(0, 0, 8, 8) };
        btn.Clicked += (_, _) =>
        {
            if (items.Count == 0) return;
            var search = new Entry { Placeholder = "Search…" };
            var listHost = new VerticalStackLayout { Spacing = 0 };
            var scroll = new ScrollView { Content = listHost, MaximumHeightRequest = 300 };
            var panel = new Border
            {
                BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
                Stroke = ClinicTheme.Color("Outline").WithAlpha(0.5f),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(8),
                Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.28f, Radius = 22, Offset = new Point(0, 8) },
                Content = new VerticalStackLayout { Spacing = 6, Children = { search, scroll } },
            };
            void Fill(string? q)
            {
                listHost.Children.Clear();
                var any = false;
                for (var k = 0; k < items.Count; k++)
                {
                    var idx = k;
                    if (!string.IsNullOrWhiteSpace(q) && items[k].IndexOf(q.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                    any = true;
                    var label = new Label { Text = items[k], FontSize = 14, LineBreakMode = LineBreakMode.TailTruncation, VerticalOptions = LayoutOptions.Center };
                    var row = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 }, Padding = new Thickness(10, 8), BackgroundColor = Colors.Transparent, Content = label };
                    var hover = new PointerGestureRecognizer();
                    hover.PointerEntered += (_, _) => row.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
                    hover.PointerExited += (_, _) => row.BackgroundColor = Colors.Transparent;
                    row.GestureRecognizers.Add(hover);
                    var tap = new TapGestureRecognizer();
                    tap.Tapped += async (_, _) => { HidePopup(); await onPick(idx); };
                    row.GestureRecognizers.Add(tap);
                    listHost.Children.Add(row);
                }
                if (!any) listHost.Children.Add(new Label { Text = "No matches", Style = ClinicTheme.Style("MutedSm"), Padding = new Thickness(10, 8) });
            }
            search.TextChanged += (_, e) => Fill(e.NewTextValue);
            Fill("");
            ShowPopup(btn, panel, 360, search);
        };
        return btn;
    }

    /// <summary>Position of <paramref name="v"/> inside the page grid, following scroll offsets.</summary>
    Point PositionInRoot(VisualElement v)
    {
        double x = 0, y = 0;
        Element? e = v;
        while (e is VisualElement ve && !ReferenceEquals(ve, Root))
        {
            x += ve.X; y += ve.Y;
            if (ve.Parent is ScrollView sv) { x -= sv.ScrollX; y -= sv.ScrollY; }
            e = ve.Parent;
        }
        return new Point(x, y);
    }

    void ShowPopup(VisualElement anchor, View panel, double width, View? focus = null)
    {
        PopupLayer.Children.Clear();
        var scrim = new BoxView { Color = Colors.Transparent };
        scrim.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(HidePopup) });
        AbsoluteLayout.SetLayoutBounds(scrim, new Rect(0, 0, 1, 1));
        AbsoluteLayout.SetLayoutFlags(scrim, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.All);
        PopupLayer.Children.Add(scrim);
        //JJQR - AI (Nov 21, 2026) - position and limits are measured against the popup layer itself (it sits inside the page padding), so the panel can never run off the window edge
        var pos = PositionInRoot(anchor);
        var layerW = PopupLayer.Width > 0 ? PopupLayer.Width : Root.Width;
        var layerH = PopupLayer.Height > 0 ? PopupLayer.Height : Root.Height;
        var px = pos.X - PopupLayer.X; var py = pos.Y - PopupLayer.Y;
        var x = Math.Max(8, Math.Min(px, Math.Max(8, layerW - width - 16)));
        var below = py + anchor.Height + 4;
        var y = below + 340 > layerH ? Math.Max(8, py - 340 - 4) : below;
        //JJQR - END
        AbsoluteLayout.SetLayoutBounds(panel, new Rect(x, y, width, AbsoluteLayout.AutoSize));
        AbsoluteLayout.SetLayoutFlags(panel, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        PopupLayer.Children.Add(panel);
        PopupLayer.IsVisible = true;
        focus?.Focus();
    }

    void HidePopup()
    {
        PopupLayer.IsVisible = false;
        PopupLayer.Children.Clear();
    }
    //JJQR - END

    /// <summary>Toggle pill (web rounded-full border, primary when selected).</summary>
    static Button Pill(string text, bool on, Action onClick)
    {
        var b = new Button { Text = text, Style = ClinicTheme.Style(on ? "StepButtonActive" : "StepButton"), Margin = new Thickness(0, 0, 8, 8) };
        b.Clicked += (_, _) => ClinicNode.App.Controls.UiTurn.Next(onClick); //JJQR - AI (Nov 23, 2026) - Windows: redraw on the next UI turn //JJQR - END
        return b;
    }

    static View Row(View left, View right) => new Grid
    {
        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        ColumnSpacing = 12,
        Children = { left, right },
    }.Also(g => Grid.SetColumn(right, 1));

    static View Card(params View[] children) => new Border
    {
        Style = ClinicTheme.Style("Card"),
        Content = new VerticalStackLayout { Spacing = 6 }.Also(v => { foreach (var c in children) v.Children.Add(c); }),
    };

    Editor AddEditor(string label, string? value)
    {
        SectionHost.Children.Add(ClinicTheme.Text(label, "FieldLabel"));
        var editor = new Editor { Text = value, AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 72 };
        SectionHost.Children.Add(editor);
        return editor;
    }

    Entry AddEntry(string label, string? value)
    {
        SectionHost.Children.Add(ClinicTheme.Text(label, "FieldLabel"));
        var entry = new Entry { Text = value };
        SectionHost.Children.Add(entry);
        return entry;
    }

    //JJQR - AI (Nov 21, 2026) - re-flows the children added since <start> (each <perCell> children = one field) into a grid with <columns> columns
    void GroupColumns(int start, int perCell, int columns)
    {
        var kids = SectionHost.Children.Skip(start).Cast<View>().ToList();
        if (kids.Count == 0) return;
        while (SectionHost.Children.Count > start) SectionHost.Children.RemoveAt(start);
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 12 };
        for (var i = 0; i < columns; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var cells = (kids.Count + perCell - 1) / perCell;
        for (var cell = 0; cell < cells; cell++)
        {
            var stack = new VerticalStackLayout { Spacing = 6 };
            foreach (var k in kids.Skip(cell * perCell).Take(perCell)) stack.Children.Add(k);
            grid.Add(stack, cell % columns, cell / columns);
        }
        SectionHost.Children.Add(grid);
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - the WHOLE row (circle + name) is the click target, with a hover highlight
    static View WrapCheck(CheckBox box, string label)
    {
        var caption = ClinicTheme.Text(label, "BodyMd");
        caption.VerticalOptions = LayoutOptions.Center;
        box.InputTransparent = true;
        var row = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(6, 4, 12, 4),
            BackgroundColor = Colors.Transparent,
            Content = new HorizontalStackLayout { Spacing = 8, Children = { box, caption } },
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => { if (box.IsEnabled) box.IsChecked = !box.IsChecked; };
        row.GestureRecognizers.Add(tap);
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (_, _) => { if (box.IsEnabled) row.BackgroundColor = ClinicTheme.Color("PrimarySubtle"); };
        hover.PointerExited += (_, _) => row.BackgroundColor = Colors.Transparent;
        row.GestureRecognizers.Add(hover);
        return row;
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - dates: the WHOLE box is the button. It shows the date and a ▾ arrow and opens our own floating month calendar (the native picker was a tiny dark popover).
    // The DatePicker stays in the code as the value holder, so every existing DateSelected handler keeps working.
    View DateBox(DatePicker dp)
    {
        dp.IsVisible = false;
        var icon = new ClinicNode.App.Controls.ClinicIcon { Name = "calendar_today", IconSize = ClinicNode.App.Controls.ClinicIcons.SizeSm, IconColor = ClinicTheme.Color("Primary"), VerticalOptions = LayoutOptions.Center };
        var text = new Label { FontSize = 14, FontFamily = ClinicTheme.FontMedium, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.NoWrap };
        var arrow = new Label { Text = "▾", FontSize = 14, TextColor = ClinicTheme.Color("Primary"), VerticalOptions = LayoutOptions.Center };
        void Refresh()
        {
            if (dp.Date is { } d) { text.Text = d.ToString("ddd, MMM d, yyyy", CultureInfo.InvariantCulture); text.TextColor = ClinicTheme.Color("OnSurface"); }
            else { text.Text = "Choose a date"; text.TextColor = ClinicTheme.Color("OnSurfaceVariant"); }
        }
        Refresh();
        dp.DateSelected += (_, _) => Refresh();
        var grid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(icon, 0, 0);
        grid.Add(text, 1, 0);
        grid.Add(arrow, 2, 0);
        var box = new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
            Stroke = ClinicTheme.Color("Primary").WithAlpha(0.55f),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(12, 9),
            MinimumWidthRequest = 220,
            HorizontalOptions = LayoutOptions.Start,
            Content = grid,
        };
        var hover = new PointerGestureRecognizer();
        hover.PointerEntered += (_, _) => box.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
        hover.PointerExited += (_, _) => box.BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest");
        box.GestureRecognizers.Add(hover);
        box.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => ShowPopup(box, BuildCalendar(dp.Date, dp.MinimumDate ?? new DateTime(1900, 1, 1), d => { HidePopup(); dp.Date = d; }), 316))
        });
        return new VerticalStackLayout { Children = { box, dp } };
    }

    View BuildCalendar(DateTime? selected, DateTime min, Action<DateTime> onPick)
    {
        var shown = new DateTime((selected ?? DateTime.Today).Year, (selected ?? DateTime.Today).Month, 1);
        var holder = new VerticalStackLayout { Spacing = 8 };
        void Render()
        {
            holder.Children.Clear();
            var head = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            View Nav(string glyph, int delta)
            {
                var b = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 }, Padding = new Thickness(12, 6), BackgroundColor = Colors.Transparent, Content = new Label { Text = glyph, FontSize = 16, TextColor = ClinicTheme.Color("Primary") } };
                b.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { shown = shown.AddMonths(delta); Render(); }) });
                var h = new PointerGestureRecognizer();
                h.PointerEntered += (_, _) => b.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
                h.PointerExited += (_, _) => b.BackgroundColor = Colors.Transparent;
                b.GestureRecognizers.Add(h);
                return b;
            }
            head.Add(Nav("◀", -1), 0, 0);
            head.Add(new Label { Text = shown.ToString("MMMM yyyy", CultureInfo.InvariantCulture), FontFamily = ClinicTheme.FontSemiBold, FontSize = 15, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center }, 1, 0);
            head.Add(Nav("▶", 1), 2, 0);
            holder.Children.Add(head);

            var grid = new Grid { ColumnSpacing = 2, RowSpacing = 2 };
            for (var i = 0; i < 7; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var names = new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };
            for (var i = 0; i < 7; i++) grid.Add(new Label { Text = names[i], FontSize = 11, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("OnSurfaceVariant"), HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2) }, i, 0);
            var first = (int)shown.DayOfWeek;
            var days = DateTime.DaysInMonth(shown.Year, shown.Month);
            for (var d = 1; d <= days; d++)
            {
                var date = new DateTime(shown.Year, shown.Month, d);
                var idx = first + d - 1;
                var isSel = selected is { } s && s.Date == date;
                var isToday = date == DateTime.Today;
                var disabled = date < min.Date;
                var lbl = new Label { Text = d.ToString(CultureInfo.InvariantCulture), FontSize = 13, HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center, FontFamily = isSel ? ClinicTheme.FontSemiBold : ClinicTheme.Font, TextColor = isSel ? Colors.White : disabled ? ClinicTheme.Color("OutlineVariant") : ClinicTheme.Color("OnSurface") };
                var cell = new Border
                {
                    StrokeThickness = isToday && !isSel ? 1.5 : 0,
                    Stroke = ClinicTheme.Color("Primary"),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                    HeightRequest = 34,
                    BackgroundColor = isSel ? ClinicTheme.Color("Primary") : Colors.Transparent,
                    Content = lbl,
                };
                if (!disabled)
                {
                    cell.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => onPick(date)) });
                    if (!isSel)
                    {
                        var h = new PointerGestureRecognizer();
                        h.PointerEntered += (_, _) => cell.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
                        h.PointerExited += (_, _) => cell.BackgroundColor = Colors.Transparent;
                        cell.GestureRecognizers.Add(h);
                    }
                }
                grid.Add(cell, idx % 7, 1 + idx / 7);
            }
            holder.Children.Add(grid);
            var todayBtn = new Border { StrokeThickness = 0, Padding = new Thickness(8, 6), BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.Start, Content = new Label { Text = "Today", FontFamily = ClinicTheme.FontSemiBold, FontSize = 13, TextColor = ClinicTheme.Color("Primary") } };
            if (DateTime.Today >= min.Date) todayBtn.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => onPick(DateTime.Today)) });
            holder.Children.Add(todayBtn);
        }
        Render();
        return new Border
        {
            BackgroundColor = ClinicTheme.Color("SurfaceContainerLowest"),
            Stroke = ClinicTheme.Color("Outline").WithAlpha(0.5f),
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12),
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.28f, Radius = 22, Offset = new Point(0, 8) },
            Content = holder,
        };
    }
    //JJQR - END

    void Error(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}

internal static class ConsultViewExtensions
{
    public static T Also<T>(this T self, Action<T> act) { act(self); return self; }
}
//JJQR - END
// JJQR - END
