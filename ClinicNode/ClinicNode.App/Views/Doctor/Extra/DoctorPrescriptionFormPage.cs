//JJQR - AI (21 Nov 2026) - chart prescription editor (web doctor/patients/[id]/prescriptions/new|edit|copy -> components/doctor/PrescriptionForm.tsx).
// New / Edit / Copy: "Prescription items (n)" with Refill last visit Rx, Add to Template, Select All + delete, per-line edit ("Edit Prescription");
// "Add medication" tabs New Prescription / Favorites / Templates (Add Template, tap = add all, Edit / Delete per template -> PUT|DELETE
// /api/prescription-templates/{id}); "Confirm Prescription" (Go Back / Confirm & Save) -> PUT /api/prescription-groups/by-booking/{id}
// (+ POST /api/prescription-templates when Add to Template is on).
// Rx-form parity pass: sig auto-suggest (lib/rx-structured suggestFromSig on the New tab and in Edit Prescription; template medicines are prefilled),
// pediatric dose helper (patient under 18; weight from this visit's vitals, editable), allergy block ("Allergy conflict: x ↔ y") and pregnancy
// checks (checkDrugSafety pregnant), Refill last visit Rx (the patient's previous visit prescription), and the print "complete the sig" check.
// The web standalone page gets none of the patient props; here they are loaded the way the consultation passes them (allergies without NKDA,
// is_pregnant, age, weight from this booking's vitals, prior visit Rx). Print prints the lines ON SCREEN, like the web handlePrint.
using System.Text.RegularExpressions;
using ClinicNode.App.Controls;
using ClinicNode.App.Services;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Printing;

namespace ClinicNode.App.Views.Doctor.Extra;

public sealed class DoctorPrescriptionFormPage : ContentPage
{
    private readonly IServiceProvider _sp;
    private readonly Guid _patientId;
    private readonly string _patientName;
    private readonly Guid _bookingId;
    private readonly Guid? _editGroupId;
    private readonly Guid? _copyFromGroupId;
    private readonly IDoctorPrescriptionsService _svc;

    private readonly List<RxDraft> _items = [];
    private readonly HashSet<Guid> _selected = [];
    private IReadOnlyList<TplMedicine> _catalog = [];
    private IReadOnlyList<TplFavorite> _favorites = [];
    private IReadOnlyList<TplRxSet> _templates = [];
    private Guid? _doctorId;
    private bool _loaded;
    private bool _addToTemplate;
    private string _templateTitle = "";
    private string _rxTab = "new";
    private string? _templateNotice;
    // patient context (web patientAllergies / patientPregnant / patientAgeYears / weightKg / priorRxItems)
    private RxPatientSafety _patient = new([], false);
    private int? _ageYears;
    private string? _weightKg;
    private List<TplRxLine> _priorRx = [];
    private string _safetyToastKey = "";
    private string _pedsMgPerKg = "", _pedsMaxMg = "", _pedsConcMgMl = "";
    private string? _pedsWeightInput;

    private readonly Label _ok = StaffUi.Success();
    private readonly Label _error = StaffUi.Error();
    private readonly Label _warn = new() { Style = ClinicTheme.Style("MutedBody"), IsVisible = false };
    private readonly Label _printSig = StaffUi.Error();
    private readonly VerticalStackLayout _safety = StaffUi.Column(4);
    private readonly VerticalStackLayout _peds = StaffUi.Column(8);
    private readonly VerticalStackLayout _itemsCard = StaffUi.Column(12);
    private readonly VerticalStackLayout _addCard = StaffUi.Column(12);
    private readonly Button _print;

    /// <summary>Raised after Confirm &amp; Save succeeded (the chart switches to its Prescriptions tab).</summary>
    public event Action? Saved;

    public DoctorPrescriptionFormPage(IServiceProvider sp, Guid patientId, string patientName, Guid bookingId, Guid? editGroupId, Guid? copyFromGroupId)
    {
        ClinicNode.App.Views.Shared.TopBarOnPushed.Attach(this, sp); //JJQR - AI (Nov 22, 2026) //JJQR - END
        _sp = sp;
        _patientId = patientId;
        _patientName = patientName;
        _bookingId = bookingId;
        _editGroupId = editGroupId;
        _copyFromGroupId = copyFromGroupId;
        _svc = StaffUi.Get<IDoctorPrescriptionsService>(sp);
        var editing = editGroupId is not null;
        Title = editing ? "Edit prescription" : "New prescription";
        BackgroundColor = ClinicTheme.Color("Background");
        _print = StaffUi.Btn("Print", PrintAsync, "SecondaryButton");
        var save = StaffUi.Btn("Save prescription", SaveClickAsync);
        StaffUi.Show(_printSig, null);
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Link("← Back to chart", () => Navigation.PopAsync()),
                ClinicTheme.Text(Title, "HeadlineMd"),
                ClinicTheme.Text("For " + patientName, "MutedBody"),
                StaffUi.Wrap(DoctorExtraUi.Spaced(_print), DoctorExtraUi.Spaced(save)),
                _printSig,
                _ok, _error, _warn, _safety, _peds,
                StaffUi.Card(_itemsCard),
                StaffUi.Card(_addCard),
            },
        });
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_loaded) return;
        _loaded = true;
        try
        {
            _doctorId = DoctorExtraUi.DoctorId(_sp);
            if (_doctorId is not { } doctorId) { _itemsCard.Children.Add(DoctorExtraUi.NotLinked()); return; }
            if ((_editGroupId ?? _copyFromGroupId) is { } groupId && await _svc.GetGroupAsync(groupId) is { } g)
                _items.AddRange(g.Lines.Select(RxDraft.From));
            _catalog = await Safe(() => _svc.ListMedicinesAsync(), []);
            _favorites = await Safe(() => _svc.ListFavoritesAsync(doctorId), []);
            _templates = await Safe(() => _svc.ListRxSetsAsync(doctorId), []);
            await LoadPatientContextAsync(doctorId);
        }
        catch (Exception ex) { Toast("error", "This prescription didn’t load. " + ex.Message); }
        RenderPeds();
        RenderItems();
        RenderAdd();
    }

    /// <summary>What the consultation hands PrescriptionForm: allergies (no NKDA), is_pregnant, age, weight from this visit's vitals, prior visit Rx.</summary>
    private async Task LoadPatientContextAsync(Guid doctorId)
    {
        _patient = await Safe(() => _svc.GetPatientSafetyAsync(_patientId), _patient);
        var chart = await Safe<PatientChart?>(() => StaffUi.Get<IDoctorPatientsService>(_sp).GetChartAsync(doctorId, _patientId), null);
        if (chart is null) return;
        _ageYears = chart.Patient.Age;
        // web vitalFormValue(filledVitals, "weight") -> parseFloat
        if (chart.VitalVisits.FirstOrDefault(v => v.BookingId == _bookingId) is { } visit)
            foreach (var t in chart.VitalTemplates.Where(t => t.Description.Contains("weight", StringComparison.OrdinalIgnoreCase)))
                if (visit.Values.TryGetValue(t.TemplateId, out var raw) && DoctorRxRules.ParseFloat(raw) is { } kg) { _weightKg = DoctorRxRules.JsNumber(kg); break; }
        var prior = chart.Prescriptions.Where(p => p.BookingId != _bookingId).OrderByDescending(p => p.CreatedAt).FirstOrDefault();
        if (prior is not null && await Safe<RxGroupDetail?>(() => _svc.GetGroupAsync(prior.GroupId), null) is { } pg)
            _priorRx = pg.Lines.ToList();
    }

    private static async Task<T> Safe<T>(Func<Task<T>> load, T fallback)
    {
        try { return await load(); } catch { return fallback; }
    }

    private void Toast(string variant, string? message)
    {
        StaffUi.Show(_ok, variant == "success" ? message : null);
        StaffUi.Show(_error, variant == "error" ? message : null);
        StaffUi.Show(_warn, variant is "warning" or "info" ? message : null);
    }

    private void SetPrintSigError(bool on)
    {
        StaffUi.Show(_printSig, on ? DoctorRxRules.PrintSigIncomplete : null);
    }

    // ── pediatric dose helper (web showPedsHelper: patientAgeYears < 18) ───────
    private void RenderPeds()
    {
        _peds.Children.Clear();
        _peds.IsVisible = _ageYears is < 18;
        if (!_peds.IsVisible) return;
        var result = ClinicTheme.Text("", "BodyMd");
        void Compute()
        {
            var effectiveWeight = _pedsWeightInput ?? _weightKg ?? "";
            var weight = DoctorRxRules.ParseFloat(Regex.Replace(effectiveWeight, @"[^\d.]", ""));
            var mgPerKg = DoctorRxRules.ParseFloat(_pedsMgPerKg);
            var max = _pedsMaxMg.Trim().Length > 0 ? DoctorRxRules.ParseFloat(_pedsMaxMg) : null;
            var conc = DoctorRxRules.ParseFloat(_pedsConcMgMl);
            (double Mg, bool Capped)? dose = mgPerKg is { } a && weight is { } w && w > 0 ? DoctorRxRules.MgPerKgDose(a, w, max) : null;
            double? ml = dose is { } d && conc is { } c && c > 0 ? DoctorRxRules.MlFromConcentration(d.Mg, c) : null;
            result.Text = dose is { } x
                ? $"≈ {DoctorRxRules.JsNumber(x.Mg)} mg{(x.Capped ? " (max cap applied)" : "")}{(ml is { } m ? $" · ≈ {DoctorRxRules.JsNumber(m)} mL" : "")}"
                : "";
            result.IsVisible = result.Text.Length > 0;
        }
        Entry Box(string text, string placeholder, Action<string> set)
        {
            var e = new Entry { Text = text, Placeholder = placeholder, WidthRequest = 160 };
            e.TextChanged += (_, a) => { set(a.NewTextValue ?? ""); Compute(); };
            return e;
        }
        var grid = StaffUi.Wrap(
            DoctorExtraUi.Spaced(StaffUi.Field("Dose (mg/kg)", Box(_pedsMgPerKg, "mg/kg", v => _pedsMgPerKg = v))),
            DoctorExtraUi.Spaced(StaffUi.Field("Weight (kg)", Box(_pedsWeightInput ?? _weightKg ?? "", "Weight kg", v => _pedsWeightInput = v))),
            DoctorExtraUi.Spaced(StaffUi.Field("Maximum dose (mg, optional)", Box(_pedsMaxMg, "Max mg (opt)", v => _pedsMaxMg = v))),
            DoctorExtraUi.Spaced(StaffUi.Field("Concentration (mg/mL, optional)", Box(_pedsConcMgMl, "mg/mL (opt)", v => _pedsConcMgMl = v))));
        var col = StaffUi.Column(8);
        col.Children.Add(ClinicTheme.Text("Pediatric dose helper (mg/kg)", "LabelLg"));
        col.Children.Add(grid);
        col.Children.Add(result);
        _peds.Children.Add(StaffUi.Card(col));
        Compute();
    }

    // ── items card ─────────────────────────────────────────────────────────
    private void RenderItems()
    {
        _itemsCard.Children.Clear();
        _itemsCard.Children.Add(ClinicTheme.Text($"Prescription items ({_items.Count})", "HeadlineSm"));
        if (_priorRx.Count > 0)
            _itemsCard.Children.Add(StaffUi.Btn("Refill last visit Rx", () => { RefillLastVisit(); return Task.CompletedTask; }, "SecondaryButton"));
        var toggle = new Switch { IsToggled = _addToTemplate };
        var title = new Entry { Text = _templateTitle, Placeholder = "Template Title", IsVisible = _addToTemplate };
        toggle.Toggled += (_, a) => { _addToTemplate = a.Value; title.IsVisible = a.Value; };
        title.TextChanged += (_, a) => _templateTitle = a.NewTextValue ?? "";
        _itemsCard.Children.Add(StaffUi.Row(new HorizontalStackLayout { Spacing = 12, Children = { ClinicTheme.Text("Add to Template", "LabelMd"), toggle } }));
        _itemsCard.Children.Add(title);
        LineList(_itemsCard, _items, _selected, RenderItems, EditItemAsync);
        RenderSafety();
    }

    /// <summary>web LineItemList (shared by the form and the template modal): Select All + delete, name / dispense / sig / indication, Edit.</summary>
    private static void LineList(VerticalStackLayout host, List<RxDraft> items, HashSet<Guid> selected, Action rerender, Func<RxDraft, Task> edit)
    {
        if (items.Count == 0) { host.Children.Add(ClinicTheme.Text("Add New Medicine", "MutedBody")); return; }
        selected.IntersectWith(items.Select(i => i.Key));
        var all = new CheckBox { IsChecked = selected.Count == items.Count };
        all.CheckedChanged += (_, a) =>
        {
            if (a.Value == (selected.Count == items.Count)) return;
            selected.Clear();
            if (a.Value) selected.UnionWith(items.Select(i => i.Key));
            rerender();
        };
        var head = new HorizontalStackLayout { Spacing = 8, Children = { all, ClinicTheme.Text("Select All", "LabelMd") } };
        if (selected.Count > 0)
            head.Children.Add(StaffUi.DangerGhost("Delete selected", () =>
            {
                items.RemoveAll(i => selected.Contains(i.Key));
                selected.Clear();
                rerender();
                return Task.CompletedTask;
            }));
        host.Children.Add(head);
        foreach (var item in items.OrderBy(i => i.GenericName, StringComparer.CurrentCulture))
        {
            var d = item;
            var line = d.ToLine();
            var check = new CheckBox { IsChecked = selected.Contains(d.Key) };
            check.CheckedChanged += (_, a) => { if (a.Value) selected.Add(d.Key); else selected.Remove(d.Key); rerender(); };
            var col = StaffUi.Column(2);
            col.Children.Add(ClinicTheme.Text(d.GenericName + (d.Dosage.Length > 0 ? " " + d.Dosage : ""), "LabelLg"));
            col.Children.Add(ClinicTheme.Text("Dispense: " + d.Quantity, "MutedSm"));
            if (DoctorRxRules.DirectionsLine(line) is { Length: > 0 } sig) col.Children.Add(ClinicTheme.Text("Sig: " + sig, "BodyMd"));
            if (d.Indication.Length > 0) col.Children.Add(ClinicTheme.Text("For " + d.Indication, "MutedSm"));
            if (d.Instruction.Length > 0) col.Children.Add(ClinicTheme.Text(d.Instruction, "MutedSm"));
            col.Children.Add(StaffUi.Btn("Edit", () => edit(d), "GhostButton"));
            host.Children.Add(StaffUi.Row(new HorizontalStackLayout { Spacing = 8, Children = { check, col } }));
        }
    }

    private void RenderSafety()
    {
        _safety.Children.Clear();
        var alerts = DoctorRxRules.CheckDrugSafety(_items.Select(i => i.GenericName), _patient.IsPregnant);
        foreach (var a in alerts)
            _safety.Children.Add(ClinicTheme.Text(a.Message, "MutedBody"));
        // web: a toast for each NEW set of warnings
        var warns = alerts.Where(a => a.Level == "warn").Select(a => a.Message).ToList();
        var key = string.Join("|", warns);
        if (key.Length > 0 && key != _safetyToastKey) { _safetyToastKey = key; Toast("warning", warns[0]); }
        // web printReady: items and every line's sig complete
        _print.IsEnabled = _items.Count > 0 && _items.All(i => DoctorRxRules.PrintSigComplete(i.ToLine()));
    }

    private string? AllergyBlock(string genericName) => DoctorRxRules.AllergyBlockReason(_patient.Allergies, genericName);

    /// <summary>web addLineItem / handleNewRxAdd: allergy conflict, then block-level safety, a warning toast, duplicates refused.</summary>
    private bool TryAdd(RxDraft d)
    {
        if (AllergyBlock(d.GenericName) is { } allergen) { Toast("error", DoctorRxRules.AllergyConflict(d.GenericName, allergen)); return false; }
        if (!SafetyBeforeAdd(d.GenericName)) return false;
        if (DoctorRxRules.IsDuplicate(_items.Select(i => i.GenericName), d.GenericName)) { Toast("error", DoctorRxRules.ItemExists); return false; }
        _items.Add(d);
        SetPrintSigError(false);
        RenderItems();
        return true;
    }

    /// <summary>web applySafetyBeforeAdd.</summary>
    private bool SafetyBeforeAdd(string genericName)
    {
        var alerts = DoctorRxRules.CheckDrugSafety(_items.Select(i => i.GenericName).Append(genericName), _patient.IsPregnant);
        if (alerts.FirstOrDefault(a => a.Level == "block") is { } block) { Toast("error", block.Message); return false; }
        var warn = alerts.FirstOrDefault(a => a.Level == "warn");
        Toast(warn is null ? "success" : "warning", warn?.Message);
        return true;
    }

    /// <summary>web handleRefillLastVisit: every prior line that passes allergy, safety and duplicate checks, copied as-is.</summary>
    private void RefillLastVisit()
    {
        var added = 0;
        foreach (var item in _priorRx)
        {
            if (AllergyBlock(item.GenericName) is { } allergen) { Toast("error", DoctorRxRules.AllergyConflict(item.GenericName, allergen)); continue; }
            if (!SafetyBeforeAdd(item.GenericName)) continue;
            if (DoctorRxRules.IsDuplicate(_items.Select(i => i.GenericName), item.GenericName)) continue;
            _items.Add(RxDraft.From(item));
            added++;
        }
        if (added > 0) SetPrintSigError(false);
        RenderItems();
    }

    private async Task EditItemAsync(RxDraft item)
    {
        var dialog = new RxLineEditDialog(item.Clone());
        await Navigation.PushModalAsync(dialog);
        if (await dialog.Result is { } updated)
        {
            var i = _items.FindIndex(x => x.Key == item.Key);
            if (i >= 0) { _items[i] = updated; RenderItems(); }
        }
    }

    // ── add medication card ────────────────────────────────────────────────
    private void RenderAdd()
    {
        _addCard.Children.Clear();
        _addCard.Children.Add(ClinicTheme.Text("Add medication", "HeadlineSm"));
        _addCard.Children.Add(DoctorExtraUi.Tabs([("new", "New Prescription"), ("favorites", "Favorites"), ("templates", "Templates")],
            () => _rxTab, id => { _rxTab = id; RenderAdd(); }));
        var body = StaffUi.Column(8);
        _addCard.Children.Add(body);
        switch (_rxTab)
        {
            case "new":
                NewTab(body, async (line, addToFavorites) =>
                {
                    if (!TryAdd(RxDraft.From(line))) return;
                    if (addToFavorites && _doctorId is { } doctorId)
                    {
                        var r = await _svc.AddFavoriteAsync(doctorId, line);
                        if (r.Succeeded) _favorites = await Safe(() => _svc.ListFavoritesAsync(doctorId), _favorites);
                        else Toast("error", r.Error);
                    }
                    RenderAdd(); // web reset()
                });
                break;
            case "favorites": FavoritesTab(body, d => TryAdd(d)); break;
            default: TemplatesTab(body); break;
        }
    }

    /// <summary>web NewRxTab (also used inside the template modal). <paramref name="onAdd"/> gets the line (route defaults to PO) and "Add to Favorites".</summary>
    private void NewTab(VerticalStackLayout body, Func<TplRxLine, bool, Task> onAdd)
    {
        var d = new RxDraft { AsNeeded = false }; // web: useState(false), route "PO"
        TplMedicine? picked = null;
        var addToFavorites = false;
        var query = new Entry { Placeholder = "Medication — pick from the list or just type it" };
        var matches = StaffUi.Column(2);
        var typed = ClinicTheme.Text("", "MutedSm");
        var catalogWarn = ClinicTheme.Text(DoctorRxRules.NotInCatalog, "MutedSm");
        var add = StaffUi.Btn("Add Item", async () =>
        {
            d.GenericName = (picked?.GenericName ?? query.Text ?? "").Trim();
            d.MedicineId = picked?.MedicineId ?? Guid.Empty;
            if (d.GenericName.Length == 0 || d.Quantity.Trim().Length == 0) return;
            if (d.Route.Trim().Length == 0) d.Route = "PO";
            await onAdd(d.ToLine("PO"), addToFavorites);
        });
        void Sync()
        {
            var q = (query.Text ?? "").Trim();
            var name = (picked?.GenericName ?? q).Trim();
            matches.Children.Clear();
            if ((query.Text ?? "").Length >= 3 && picked is null)
                foreach (var m in _catalog.Where(m => m.GenericName.Contains(query.Text!, StringComparison.OrdinalIgnoreCase)).Take(8))
                    matches.Children.Add(StaffUi.Link(m.GenericName, () => { picked = m; query.Text = m.GenericName; Sync(); return Task.CompletedTask; }));
            typed.Text = q.Length > 0 && picked is null ? "Will be added as typed: " + q : "";
            typed.IsVisible = typed.Text.Length > 0;
            catalogWarn.IsVisible = name.Length > 0 && picked is null && !_catalog.Any(m => string.Equals(m.GenericName, name, StringComparison.OrdinalIgnoreCase));
            add.IsEnabled = name.Length > 0 && d.Quantity.Trim().Length > 0;
        }
        query.TextChanged += (_, a) => { if (picked is not null && a.NewTextValue != picked.GenericName) picked = null; Sync(); };
        body.Children.Add(StaffUi.Field("Medication", query));
        body.Children.Add(matches);
        body.Children.Add(typed);
        body.Children.Add(catalogWarn);
        // sig auto-suggest: nothing touched yet; route changes re-suggest too (web NewRxTab)
        var fields = DoctorRxFields.FormFields(d, new Label { Style = ClinicTheme.Style("MutedSm") }, [], suggestOnRoute: true);
        body.Children.Add(fields);
        // quantity gates "Add Item" (web canAdd)
        foreach (var e in Descendants<Entry>(fields)) e.TextChanged += (_, _) => Sync();
        var fav = new CheckBox();
        fav.CheckedChanged += (_, a) => addToFavorites = a.Value;
        body.Children.Add(new HorizontalStackLayout { Spacing = 8, Children = { fav, ClinicTheme.Text("Add to Favorites", "BodyMd") } });
        body.Children.Add(add);
        Sync();
    }

    private static IEnumerable<T> Descendants<T>(IView root) where T : class
    {
        if (root is T t) yield return t;
        if (root is Layout layout)
            foreach (var child in layout.Children)
                foreach (var x in Descendants<T>(child)) yield return x;
        if (root is ContentView cv && cv.Content is { } c)
            foreach (var x in Descendants<T>(c)) yield return x;
    }

    private void FavoritesTab(VerticalStackLayout body, Action<RxDraft> onAdd)
    {
        var search = new Entry { Placeholder = "Search Favorite" };
        var list = StaffUi.Column(4);
        void Render()
        {
            list.Children.Clear();
            var q = search.Text ?? "";
            var shown = _favorites.Where(f => f.GenericName.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (shown.Count == 0) list.Children.Add(ClinicTheme.Text("No favorites yet.", "MutedBody"));
            foreach (var f in shown)
            {
                var fav = f;
                list.Children.Add(StaffUi.Btn(fav.GenericName, () =>
                {
                    onAdd(new RxDraft { MedicineId = fav.MedicineId, GenericName = fav.GenericName, Dosage = fav.Dosage, Quantity = fav.Quantity, Instruction = fav.Instruction ?? "", Route = "" });
                    return Task.CompletedTask;
                }, "SecondaryButton"));
            }
        }
        search.TextChanged += (_, _) => Render();
        body.Children.Add(search);
        body.Children.Add(list);
        Render();
    }

    private void TemplatesTab(VerticalStackLayout body)
    {
        body.Children.Add(StaffUi.Btn("Add Template", () => OpenTemplateAsync(null), "SecondaryButton"));
        var search = new Entry { Placeholder = "Search Templates" };
        var notice = ClinicTheme.Text(_templateNotice ?? "", "MutedBody");
        notice.IsVisible = _templateNotice is not null;
        var list = StaffUi.Column(4);
        void Render()
        {
            list.Children.Clear();
            var q = search.Text ?? "";
            var shown = _templates.Where(t => t.Title.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
            if (shown.Count == 0) list.Children.Add(ClinicTheme.Text("No templates yet.", "MutedBody"));
            foreach (var t in shown)
            {
                var tpl = t;
                var apply = StaffUi.Btn(tpl.Title, () =>
                {
                    // web handleTemplateAddAll: one update (allergy + pregnancy aware), prefilled from the sig, result under the search box
                    var plan = DoctorRxRules.PlanTemplateApply(tpl.Lines, _items.Select(i => i.GenericName), AllergyBlock, _patient.IsPregnant);
                    if (plan.ToAdd.Count > 0)
                    {
                        _items.AddRange(plan.ToAdd.Select(DoctorRxRules.Prefill).Select(RxDraft.From));
                        SetPrintSigError(false);
                    }
                    var (_, message) = DoctorRxRules.DescribeTemplateApply(tpl.Title, tpl.Lines.Count, plan);
                    _templateNotice = message;
                    notice.Text = message;
                    notice.IsVisible = true;
                    RenderItems();
                    return Task.CompletedTask;
                }, "SecondaryButton");
                var row = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
                row.Add(apply, 0);
                row.Add(StaffUi.Btn("Edit", () => OpenTemplateAsync(tpl), "GhostButton"), 1);
                row.Add(StaffUi.DangerGhost("Delete", () => DeleteTemplateAsync(tpl)), 2);
                list.Children.Add(row);
            }
        }
        search.TextChanged += (_, _) => Render();
        body.Children.Add(search);
        body.Children.Add(notice);
        body.Children.Add(list);
        Render();
    }

    /// <summary>web TemplateModal ("Add Template" / "Edit Template"): Edit updates the row in place, Add creates one; then favorites + templates reload.</summary>
    private async Task OpenTemplateAsync(TplRxSet? template)
    {
        if (_doctorId is not { } doctorId) return;
        var dialog = new TemplateEditorDialog(this, template, async (title, lines) =>
        {
            var r = template is null
                ? await _svc.CreateRxSetAsync(doctorId, title, lines)
                : await _svc.UpdateRxSetAsync(template.Id, doctorId, title, lines);
            if (!r.Succeeded) return r.Error == DoctorExtraOnline.NeedsServer ? r.Error : TemplateNotSaved;
            _favorites = await Safe(() => _svc.ListFavoritesAsync(doctorId), _favorites);
            _templates = await Safe(() => _svc.ListRxSetsAsync(doctorId), _templates);
            return null;
        });
        await Navigation.PushModalAsync(dialog);
        if (await dialog.Result) RenderAdd();
    }

    private const string TemplateNotSaved = "The template was not saved. Your edits are still here, please try again.";

    /// <summary>web "Delete Template?" modal.</summary>
    private async Task DeleteTemplateAsync(TplRxSet template)
    {
        var deleted = await StaffDialog.ShowAsync(this, "Delete Template?", "Are you sure you want to delete this template?", null, null, "Cancel", "Delete", async _ =>
        {
            var r = await _svc.DeleteRxSetAsync(template.Id);
            return r.Succeeded ? null : r.Error;
        });
        if (!deleted) return;
        _templates = _templates.Where(t => t.Id != template.Id).ToList();
        RenderAdd();
    }

    // ── save / print ───────────────────────────────────────────────────────
    private async Task SaveClickAsync()
    {
        if (_items.Count == 0) { Toast("error", DoctorRxRules.NothingSelected); return; }
        if (_addToTemplate && _templateTitle.Trim().Length == 0) { Toast("error", DoctorRxRules.TemplateTitleRequired); return; }
        if (_doctorId is not { } doctorId) return;
        Toast("success", null);
        var lines = _items.Select(i => i.ToLine()).ToList();
        var summary = string.Join("\n\n", lines.Select((l, i) =>
        {
            var text = $"{i + 1}. {l.GenericName} {l.Dosage} — #{l.Quantity}" + (string.IsNullOrEmpty(l.Route) ? "" : " · " + l.Route);
            if (DoctorRxRules.DirectionsLine(l) is { Length: > 0 } sig) text += "\nSig preview: " + sig;
            if (!string.IsNullOrEmpty(l.Instruction)) text += " · " + l.Instruction;
            return text;
        }));
        var saved = await StaffDialog.ShowAsync(this, "Confirm Prescription", summary, null, null, "Go Back", "Confirm & Save", async _ =>
        {
            var r = await _svc.SaveByBookingAsync(_bookingId, _patientId, doctorId, lines);
            if (!r.Succeeded) return r.Error == DoctorExtraOnline.NeedsServer ? r.Error : DoctorRxRules.SaveFailed;
            if (_addToTemplate)
            {
                var t = await _svc.CreateRxSetAsync(doctorId, _templateTitle.Trim(), lines);
                if (!t.Succeeded) return DoctorRxRules.SaveFailed; // web: the whole confirm fails with the same toast
            }
            return null;
        });
        if (!saved) return;
        Saved?.Invoke();
        await Navigation.PopAsync();
    }

    /// <summary>web handlePrint: nothing selected / sig incomplete checks, then the A5 pad from the lines on screen.</summary>
    private async Task PrintAsync()
    {
        if (_items.Count == 0) { Toast("error", DoctorRxRules.NothingSelected); return; }
        var lines = _items.Select(i => i.ToLine()).ToList();
        if (!lines.All(DoctorRxRules.PrintSigComplete))
        {
            SetPrintSigError(true);
            Toast("error", DoctorRxRules.PrintSigIncomplete);
            return;
        }
        SetPrintSigError(false);
        if (_doctorId is not { } doctorId) return;
        var items = lines.Select(l => new RxLine
        {
            GenericName = l.GenericName, Dosage = l.Dosage, Quantity = l.Quantity, Instruction = l.Instruction, Timing = l.Timing,
            MealRelation = l.MealRelation, AsNeeded = l.AsNeeded, DurationKind = l.DurationKind, DurationValue = l.DurationValue,
            Indication = l.Indication, IsControlledSubstance = l.IsControlled, Route = l.Route,
        }).ToList();
        await PrintActions.RunAsync(this, async () =>
        {
            var header = await StaffUi.Get<IDoctorPrintDataService>(_sp).GetRxPrintHeaderAsync(doctorId, _patientId)
                ?? throw new InvalidOperationException("The patient or doctor details for this prescription could not be loaded.");
            return DoctorRxPrintDocument.Build(header, items);
        });
    }

    /// <summary>web EditLineItemModal ("Edit Prescription": same fields as the New tab, Save needs a name and a quantity; the sig re-suggests untouched fields).</summary>
    private sealed class RxLineEditDialog : ContentPage
    {
        private readonly TaskCompletionSource<RxDraft?> _done = new();
        public Task<RxDraft?> Result => _done.Task;

        public RxLineEditDialog(RxDraft d)
        {
            Title = "Edit Prescription";
            BackgroundColor = ClinicTheme.Color("Background");
            var name = new Entry { Text = d.GenericName, Placeholder = "Medication — pick from the list or just type it" };
            var col = new VerticalStackLayout { Spacing = 12, Padding = 24, MaximumWidthRequest = 560 };
            col.Children.Add(ClinicTheme.Text("Edit Prescription", "HeadlineMd"));
            col.Children.Add(StaffUi.Field("Medication", name));
            // web: mealTouched = mealRelation != null, timesTouched = timing set, asNeededTouched = asNeeded is a boolean
            var locked = new HashSet<string>();
            if (d.Meal is not null) locked.Add("meal");
            if (d.Slots.Count > 0) locked.Add("timing");
            if (d.AsNeeded is not null) locked.Add("asNeeded");
            var fields = DoctorRxFields.FormFields(d, new Label { Style = ClinicTheme.Style("MutedSm") }, locked, suggestOnRoute: false);
            col.Children.Add(fields);
            var save = StaffUi.Btn("Save", async () =>
            {
                d.GenericName = name.Text ?? "";
                _done.TrySetResult(d);
                await Navigation.PopModalAsync();
            });
            void Sync() => save.IsEnabled = (name.Text ?? "").Trim().Length > 0 && d.Quantity.Trim().Length > 0;
            name.TextChanged += (_, _) => Sync();
            foreach (var e in Descendants<Entry>(fields)) e.TextChanged += (_, _) => Sync();
            var close = StaffUi.Btn("Close", async () => { _done.TrySetResult(null); await Navigation.PopModalAsync(); }, "SecondaryButton");
            col.Children.Add(StaffUi.Wrap(DoctorExtraUi.Spaced(close), DoctorExtraUi.Spaced(save)));
            Content = new ScrollView { Content = col };
            Sync();
        }

        protected override bool OnBackButtonPressed()
        {
            _done.TrySetResult(null);
            return base.OnBackButtonPressed();
        }
    }

    /// <summary>web TemplateModal: Template Title + its medicine list (Select All / delete / Edit), tabs New Prescription / Favorites,
    /// Save enabled only with a title, at least one medicine and a change; a failed save keeps the edits and shows the error.</summary>
    private sealed class TemplateEditorDialog : ContentPage
    {
        private readonly TaskCompletionSource<bool> _done = new();
        public Task<bool> Result => _done.Task;

        private readonly DoctorPrescriptionFormPage _owner;
        private readonly List<RxDraft> _lines;
        private readonly HashSet<Guid> _selected = [];
        private readonly string _originalSnapshot;
        private readonly VerticalStackLayout _list = StaffUi.Column(8);
        private readonly VerticalStackLayout _tabBody = StaffUi.Column(8);
        private readonly VerticalStackLayout _tabs;
        private readonly Entry _title;
        private readonly Button _save;
        private readonly Label _saveError = StaffUi.Error();
        private string _tab = "new";

        public TemplateEditorDialog(DoctorPrescriptionFormPage owner, TplRxSet? template, Func<string, IReadOnlyList<TplRxLine>, Task<string?>> onSave)
        {
            _owner = owner;
            _lines = (template?.Lines ?? []).Select(RxDraft.From).ToList();
            _originalSnapshot = Snapshot(template?.Title ?? "", _lines);
            Title = template is null ? "Add Template" : "Edit Template";
            BackgroundColor = ClinicTheme.Color("Background");
            _title = new Entry { Text = template?.Title ?? "", Placeholder = "Template Title" };
            _title.TextChanged += (_, _) => Sync();
            _save = StaffUi.Btn("Save", async () =>
            {
                StaffUi.Show(_saveError, null);
                string? error;
                try { error = await onSave(_title.Text ?? "", _lines.Select(l => l.ToLine()).ToList()); }
                catch { error = TemplateNotSaved; }
                if (error is not null) { StaffUi.Show(_saveError, error); return; }
                _done.TrySetResult(true);
                await Navigation.PopModalAsync();
            });
            _tabs = DoctorExtraUi.Tabs([("new", "New Prescription"), ("favorites", "Favorites")], () => _tab, id => { _tab = id; RenderTab(); });
            var close = StaffUi.Btn("Close", async () => { _done.TrySetResult(false); await Navigation.PopModalAsync(); }, "SecondaryButton");
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Spacing = 16, Padding = 24, MaximumWidthRequest = 960,
                    Children =
                    {
                        ClinicTheme.Text(Title, "HeadlineMd"),
                        _title,
                        StaffUi.Card(_list),
                        _tabs,
                        StaffUi.Card(_tabBody),
                        _saveError,
                        StaffUi.Wrap(DoctorExtraUi.Spaced(close), DoctorExtraUi.Spaced(_save)),
                    },
                },
            };
            StaffUi.Show(_saveError, null);
            RenderList();
            RenderTab();
        }

        private static string Snapshot(string title, IEnumerable<RxDraft> lines) =>
            title + "\u0001" + string.Join("\u0002", lines.Select(l => l.ToLine().ToString()));

        private void Sync() => _save.IsEnabled = (_title.Text ?? "").Trim().Length > 0 && _lines.Count > 0
            && Snapshot(_title.Text ?? "", _lines) != _originalSnapshot;

        private void RenderList()
        {
            _list.Children.Clear();
            LineList(_list, _lines, _selected, RenderList, EditAsync);
            Sync();
        }

        private void RenderTab()
        {
            _tabBody.Children.Clear();
            if (_tab == "new")
                _owner.NewTab(_tabBody, (line, _) => { _lines.Add(RxDraft.From(line)); RenderList(); RenderTab(); return Task.CompletedTask; });
            else
                _owner.FavoritesTab(_tabBody, d => { _lines.Add(d); RenderList(); });
        }

        private async Task EditAsync(RxDraft item)
        {
            var dialog = new RxLineEditDialog(item.Clone());
            await Navigation.PushModalAsync(dialog);
            if (await dialog.Result is { } updated)
            {
                var i = _lines.FindIndex(x => x.Key == item.Key);
                if (i >= 0) { _lines[i] = updated; RenderList(); }
            }
        }

        protected override bool OnBackButtonPressed()
        {
            _done.TrySetResult(false);
            return base.OnBackButtonPressed();
        }
    }
}
//JJQR - END
