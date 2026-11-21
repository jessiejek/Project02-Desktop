//JJQR - AI (21 Nov 2026) - Templates workspace editor (web components/ui/SidePanel + the *Editor of SoapTab / PhrasesTab / RxTab / FavoritesTab / CertsTab).
// One pushed page per item: title, fields, the "why can't I save" line, Cancel / Save. Cancel with unsaved edits asks
// "Discard unsaved changes?" (Keep editing / Discard). Writes go through IDoctorTemplatesWorkspaceService (API first; offline = NeedsServer).
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Doctor.Extra; //JJQR - AI (21 Nov 2026) DoctorExtraUi, DoctorRxFields //JJQR - END
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra2; //JJQR - AI (21 Nov 2026) moved from ...Extra (clean-up); DoctorExtraUi / DoctorRxFields come from the Extra using above //JJQR - END

public sealed class DoctorTemplateEditorPage : ContentPage
{
    private readonly IDoctorTemplatesWorkspaceService _svc;
    private readonly Guid _doctorId;
    private readonly VerticalStackLayout _body = StaffUi.Column(12);
    private readonly Label _blocked = new() { Style = ClinicTheme.Style("MutedSm"), IsVisible = false };
    private readonly Label _error = StaffUi.Error();
    private Func<string?> _reason = () => null;
    private Func<bool> _dirty = () => false;
    private Func<Task<(DoctorResult Result, string Toast, string Failed)>>? _save;
    private bool _closing;

    /// <summary>Raised after a successful save with the web toast text ("SOAP template “X” saved.").</summary>
    public event Action<string>? Saved;

    private DoctorTemplateEditorPage(IServiceProvider sp, Guid doctorId, string title)
    {
        _svc = StaffUi.Get<IDoctorTemplatesWorkspaceService>(sp);
        _doctorId = doctorId;
        Title = title;
        BackgroundColor = ClinicTheme.Color("Background");
        var cancel = StaffUi.Btn("Cancel", CloseAsync, "SecondaryButton");
        var save = StaffUi.Btn("Save", SaveAsync);
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            MaximumWidthRequest = 720,
            Children = { ClinicTheme.Text(title, "HeadlineMd"), _error, _body, _blocked, StaffUi.Wrap(DoctorExtraUi.Spaced(cancel), DoctorExtraUi.Spaced(save)) },
        });
    }

    private void Refresh() => StaffUi.Show(_blocked, _reason());

    private Entry Line(string text, string placeholder, Action<string> set)
    {
        var e = new Entry { Text = text, Placeholder = placeholder };
        e.TextChanged += (_, a) => { set(a.NewTextValue ?? ""); Refresh(); };
        return e;
    }

    private Editor Area(string text, double height, Action<string> set, string? placeholder = null)
    {
        var e = DoctorExtraUi.Area(text, height, placeholder);
        e.TextChanged += (_, a) => { set(a.NewTextValue ?? ""); Refresh(); };
        return e;
    }

    private async Task CloseAsync()
    {
        if (_closing) return;
        if (_dirty() && !await DoctorExtraUi.ConfirmAsync(this, "Discard unsaved changes?",
                "You have edits that haven't been saved. Closing now will lose them.", "Discard", danger: true, backText: "Keep editing"))
            return;
        _closing = true;
        await Navigation.PopAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        if (_closing || !_dirty()) return base.OnBackButtonPressed();
        _ = CloseAsync();
        return true;
    }

    private async Task SaveAsync()
    {
        StaffUi.Show(_error, null);
        if (_reason() is { } why) { StaffUi.Show(_blocked, why); return; }
        if (_save is null) return;
        var (r, toast, failed) = await _save();
        if (!r.Succeeded)
        {
            // web: "<Kind> was not saved. Your edits are still here — try again." (plus the offline reason when there is no server)
            StaffUi.Show(_error, r.Error == DoctorExtraOnline.NeedsServer ? failed + " " + r.Error : failed);
            return;
        }
        _closing = true;
        Saved?.Invoke(toast);
        await Navigation.PopAsync();
    }

    static string? Clean(string s) => s.Trim().Length > 0 ? s.Trim() : null;

    // ── SOAP template ──────────────────────────────────────────────────────
    public static DoctorTemplateEditorPage Soap(IServiceProvider sp, Guid doctorId, TplSoap? row)
    {
        var p = new DoctorTemplateEditorPage(sp, doctorId, row is null ? "New SOAP template" : $"Edit “{row.Title}”");
        var title = row?.Title ?? "";
        var keys = new[] { "ChiefComplaint", "Subjective", "Objective", "Assessment", "Plan" };
        var initial = new[] { row?.ChiefComplaint, row?.Subjective, row?.Objective, row?.Assessment, row?.Plan }.Select(v => v ?? "").ToArray();
        var fields = initial.ToArray();
        var startTitle = title;
        var preview = false;
        var area = StaffUi.Column(12);
        p._reason = () => DoctorTemplateRules.ValidateSoap(title, fields);
        p._dirty = () => title != startTitle || !fields.SequenceEqual(initial);
        void Render()
        {
            area.Children.Clear();
            if (!preview)
            {
                area.Children.Add(StaffUi.Field("Template name", p.Line(title, "e.g. \"Annual Physical — Normal\"", v => title = v)));
                for (var i = 0; i < keys.Length; i++)
                {
                    var k = i;
                    area.Children.Add(StaffUi.Field(DoctorTemplateRules.PhraseFieldLabel(keys[k]), p.Area(fields[k], 96, v => fields[k] = v)));
                }
            }
            else
            {
                area.Children.Add(ClinicTheme.Text(title.Length > 0 ? title : "Untitled template", "LabelLg"));
                for (var i = 0; i < keys.Length; i++)
                    area.Children.Add(StaffUi.Row(ClinicTheme.Text(DoctorTemplateRules.PhraseFieldLabel(keys[i]), "LabelMd"),
                        ClinicTheme.Text(fields[i].Trim().Length > 0 ? fields[i].Trim() : "—", "BodyMd")));
            }
        }
        var modes = StaffUi.Wrap();
        void RenderModes()
        {
            modes.Children.Clear();
            modes.Children.Add(DoctorExtraUi.Chip("Edit", !preview, () => { preview = false; RenderModes(); Render(); }));
            modes.Children.Add(DoctorExtraUi.Chip("Preview", preview, () => { preview = true; RenderModes(); Render(); }));
        }
        RenderModes();
        Render();
        p._body.Children.Add(modes);
        p._body.Children.Add(area);
        p._save = async () =>
        {
            var draft = new TplSoap(row?.Id ?? Guid.Empty, title.Trim(), false, Clean(fields[0]), Clean(fields[1]), Clean(fields[2]), Clean(fields[3]), Clean(fields[4]));
            var r = await p._svc.SaveSoapAsync(p._doctorId, row?.Id, draft);
            return (r, $"SOAP template “{draft.Title}” saved.", "SOAP template was not saved. Your edits are still here — try again.");
        };
        p.Refresh();
        return p;
    }

    // ── phrase ─────────────────────────────────────────────────────────────
    public static DoctorTemplateEditorPage Phrase(IServiceProvider sp, Guid doctorId, TplPhrase? row, string? field)
    {
        var p = new DoctorTemplateEditorPage(sp, doctorId, row is null ? "New phrase" : $"Edit “{row.Label}”");
        var f = row?.Field ?? (DoctorTemplateRules.PhraseFields.Any(x => x.Value == field) ? field! : DoctorTemplateRules.PhraseFields[0].Value);
        var label = row?.Label ?? "";
        var body = row?.Body ?? "";
        var (f0, l0, b0) = (f, label, body);
        var nameEdited = !string.IsNullOrEmpty(row?.Label);
        var picker = new Picker { ItemsSource = DoctorTemplateRules.PhraseFields.Select(x => x.Label).ToList() };
        picker.SelectedIndex = Array.FindIndex(DoctorTemplateRules.PhraseFields, x => x.Value == f);
        picker.SelectedIndexChanged += (_, _) => { if (picker.SelectedIndex >= 0) f = DoctorTemplateRules.PhraseFields[picker.SelectedIndex].Value; p.Refresh(); };
        var name = new Entry { Text = label, Placeholder = "Fills in from your text — or type your own" };
        var setting = false;
        name.TextChanged += (_, a) => { label = a.NewTextValue ?? ""; if (!setting) nameEdited = true; p.Refresh(); };
        var text = p.Area(body, 160, v =>
        {
            body = v;
            if (!nameEdited) { setting = true; name.Text = DoctorTemplateRules.PhraseNameFromText(v); setting = false; }
        });
        p._body.Children.Add(StaffUi.Field("SOAP field", ClinicPickers.PickerBox(null, picker)));
        p._body.Children.Add(StaffUi.Field("Phrase name", name, "Filled in from the first words of your text. Shown in the “Insert phrase…” list."));
        p._body.Children.Add(StaffUi.Field("Phrase text", text, "Inserted into the note at the cursor, on its own line."));
        p._reason = () => DoctorTemplateRules.ValidatePhrase(label, body);
        p._dirty = () => f != f0 || label != l0 || body != b0;
        p._save = async () =>
        {
            var r = await p._svc.SavePhraseAsync(p._doctorId, row?.Id, f, label.Trim(), body.Trim());
            return (r, $"Phrase “{label.Trim()}” saved.", "Phrase was not saved. Your edits are still here — try again.");
        };
        p.Refresh();
        return p;
    }

    // ── prescription set ───────────────────────────────────────────────────
    public static DoctorTemplateEditorPage RxSet(IServiceProvider sp, Guid doctorId, TplRxSet? row, IReadOnlyList<TplMedicine> catalog)
    {
        var p = new DoctorTemplateEditorPage(sp, doctorId, row is null ? "New prescription set" : $"Edit “{row.Title}”");
        var title = row?.Title ?? "";
        var lines = row is null || row.Lines.Count == 0 ? new List<RxDraft> { new() } : row.Lines.Select(RxDraft.From).ToList();
        var startTitle = title;
        var start = lines.Select(l => l.ToLine()).ToList();
        p._body.Children.Add(StaffUi.Field("Set name", p.Line(title, "e.g. \"Fever kit — adult\"", v => title = v)));
        var list = StaffUi.Column(12);
        void Render()
        {
            list.Children.Clear();
            for (var i = 0; i < lines.Count; i++)
            {
                var idx = i;
                var d = lines[i];
                var head = StaffUi.Wrap(DoctorExtraUi.Spaced(ClinicTheme.Text($"Medicine {idx + 1}", "LabelLg"), 16));
                var up = StaffUi.Btn("↑", () => { Move(d.Key, -1); return Task.CompletedTask; }, "GhostButton"); up.IsEnabled = idx > 0;
                var down = StaffUi.Btn("↓", () => { Move(d.Key, 1); return Task.CompletedTask; }, "GhostButton"); down.IsEnabled = idx < lines.Count - 1;
                var remove = StaffUi.DangerGhost("Remove", () => { lines.RemoveAll(x => x.Key == d.Key); Render(); return Task.CompletedTask; }); remove.IsEnabled = lines.Count > 1;
                head.Children.Add(DoctorExtraUi.Spaced(up)); head.Children.Add(DoctorExtraUi.Spaced(down)); head.Children.Add(DoctorExtraUi.Spaced(remove));
                list.Children.Add(StaffUi.Row(head, DoctorRxFields.TemplateLine(d, catalog)));
            }
            p.Refresh();
        }
        void Move(Guid key, int dir)
        {
            var ids = DoctorTemplateRules.MoveId(lines.Select(l => l.Key).ToList(), key, dir);
            lines = ids.Select(k => lines.First(l => l.Key == k)).ToList();
            Render();
        }
        Render();
        p._body.Children.Add(list);
        p._body.Children.Add(StaffUi.Wrap(DoctorExtraUi.Spaced(StaffUi.Btn("Add medicine", () => { lines.Add(new RxDraft()); Render(); return Task.CompletedTask; }, "SecondaryButton"))));
        p._reason = () => DoctorTemplateRules.ValidateRxSet(title, lines.Select(l => l.GenericName));
        p._dirty = () => title != startTitle || !lines.Select(l => l.ToLine()).SequenceEqual(start);
        p._save = async () =>
        {
            var items = lines.Where(l => l.GenericName.Trim().Length > 0).Select(l => l.ToLine()).ToList();
            var r = await p._svc.SaveRxSetAsync(p._doctorId, row?.Id, title.Trim(), items);
            return (r, $"Prescription set “{title.Trim()}” saved.", "Prescription set was not saved. Your edits are still here — try again.");
        };
        return p;
    }

    // ── favorite medicine ──────────────────────────────────────────────────
    public static DoctorTemplateEditorPage Favorite(IServiceProvider sp, Guid doctorId, TplFavorite? row, IReadOnlyList<TplMedicine> catalog)
    {
        var p = new DoctorTemplateEditorPage(sp, doctorId, row is null ? "Add favorite medicine" : $"Edit “{row.GenericName}”");
        var name = row?.GenericName ?? "";
        var medicineId = row?.MedicineId ?? Guid.Empty;
        var dosage = row?.Dosage ?? "";
        var quantity = row?.Quantity ?? "";
        var instruction = row?.Instruction ?? "";
        var start = (name, dosage, quantity, instruction);
        var nameEntry = p.Line(name, "Generic name", v => { name = v; medicineId = DoctorRxFields.MatchMedicine(catalog, v); });
        var picks = StaffUi.Column(4);
        nameEntry.TextChanged += (_, a) =>
        {
            picks.Children.Clear();
            var q = (a.NewTextValue ?? "").Trim();
            if (q.Length < 2 || catalog.Any(m => string.Equals(m.GenericName, q, StringComparison.OrdinalIgnoreCase))) return;
            foreach (var m in catalog.Where(m => m.GenericName.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(8))
                picks.Children.Add(StaffUi.Link(m.GenericName, () => { nameEntry.Text = m.GenericName; return Task.CompletedTask; }));
        };
        p._body.Children.Add(StaffUi.Field("Medicine", nameEntry, "Pick from the catalog or type a name."));
        p._body.Children.Add(picks);
        p._body.Children.Add(StaffUi.Wrap(
            DoctorExtraUi.Spaced(StaffUi.Field("Dosage", p.Line(dosage, "500 mg", v => dosage = v))),
            DoctorExtraUi.Spaced(StaffUi.Field("Quantity", p.Line(quantity, "21 tabs", v => quantity = v)))));
        p._body.Children.Add(StaffUi.Field("Directions", p.Line(instruction, "e.g. 1 tab every 8 hours", v => instruction = v)));
        p._reason = () => DoctorTemplateRules.ValidateFavorite(name);
        p._dirty = () => (name, dosage, quantity, instruction) != start;
        p._save = async () =>
        {
            var draft = new TplFavorite(row?.Id ?? Guid.Empty, medicineId, name.Trim(), dosage.Trim(), quantity.Trim(), Clean(instruction), row?.SortOrder ?? 0);
            var r = await p._svc.SaveFavoriteAsync(p._doctorId, row?.Id, draft);
            return (r, $"Favorite “{draft.GenericName}” saved.", "Favorite was not saved. Your edits are still here — try again.");
        };
        p.Refresh();
        return p;
    }

    // ── medical certificate template ───────────────────────────────────────
    public static DoctorTemplateEditorPage Cert(IServiceProvider sp, Guid doctorId, TplCert? row)
    {
        var p = new DoctorTemplateEditorPage(sp, doctorId, row is null ? "New certificate template" : $"Edit “{row.Title}”");
        var title = row?.Title ?? "";
        var dx = row?.DiagnosisText ?? "";
        var rec = row?.Recommendations ?? "";
        var pur = row?.PurposeException ?? "";
        var start = (title, dx, rec, pur);
        var preview = StaffUi.Column(8);
        void RenderPreview()
        {
            preview.Children.Clear();
            var head = ClinicTheme.Text("MEDICAL CERTIFICATE — PREVIEW", "LabelMd");
            head.HorizontalTextAlignment = TextAlignment.Center;
            preview.Children.Add(head);
            preview.Children.Add(ClinicTheme.Text("This is to certify that the patient was examined and found to have:", "BodyMd"));
            foreach (var (l, v) in new[] { ("Diagnosis / Impressions", dx), ("Recommendations", rec), ("Purpose exception", pur) })
                preview.Children.Add(ClinicTheme.Text($"{l}: " + (v.Trim().Length > 0 ? v.Trim() : "— (falls back to the visit’s own text)"), "BodyMd"));
        }
        p._body.Children.Add(StaffUi.Field("Template name", p.Line(title, "e.g. \"Fit to work\"", v => title = v)));
        p._body.Children.Add(StaffUi.Field("Diagnosis / Impressions", p.Area(dx, 80, v => { dx = v; RenderPreview(); })));
        p._body.Children.Add(StaffUi.Field("Recommendations", p.Area(rec, 80, v => { rec = v; RenderPreview(); })));
        p._body.Children.Add(StaffUi.Field("Purpose exception", p.Area(pur, 56, v => { pur = v; RenderPreview(); })));
        RenderPreview();
        p._body.Children.Add(StaffUi.Card(preview));
        p._reason = () => DoctorTemplateRules.ValidateCert(title, dx, rec, pur);
        p._dirty = () => (title, dx, rec, pur) != start;
        p._save = async () =>
        {
            var draft = new TplCert(row?.Id ?? Guid.Empty, title.Trim(), false, Clean(dx), Clean(rec), Clean(pur));
            var r = await p._svc.SaveCertAsync(p._doctorId, row?.Id, draft);
            return (r, $"Certificate template “{draft.Title}” saved.", "Certificate template was not saved. Your edits are still here — try again.");
        };
        p.Refresh();
        return p;
    }
}
//JJQR - END
