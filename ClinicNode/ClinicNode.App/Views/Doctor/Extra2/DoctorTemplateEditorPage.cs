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
        //JJQR - AI (Nov 21, 2026) - editor chrome: title with Cancel / Save pinned at the top right (always visible, even on a long form), form below in a comfortable column
        var header = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Padding = new Thickness(0, 0, 0, 4) };
        header.Add(new Label { Text = title, Style = ClinicTheme.Style("HeadlineMd"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation }, 0, 0);
        header.Add(new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { cancel, save } }, 1, 0);
        var form = new VerticalStackLayout { Spacing = 16, Padding = new Thickness(0, 4, 0, 24), Children = { _error, _body, _blocked } };
        var page = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 8, Padding = new Thickness(24, 16, 24, 0), MaximumWidthRequest = 920, HorizontalOptions = LayoutOptions.Center };
        page.Add(header, 0, 0);
        page.Add(new ScrollView { Content = form }, 0, 1);
        Content = page;
        //JJQR - END
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
        await LeaveAsync();
    }

    //JJQR - AI (Nov 24, 2026) - owner (Windows): after a delete, the editor opened as a full-window overlay (no sidebar) and Save failed with
    // "PopAsync is not supported, please use a NavigationPage" (errors.log). Close the editor whichever way it was opened: as an overlay (modal),
    // as a normal pushed page, or else go back to Templates. A failure here is logged, never a crash.
    private async Task LeaveAsync()
    {
        try
        {
            if (Navigation.ModalStack.Contains(this)) { await Navigation.PopModalAsync(); return; }
            if (Navigation.NavigationStack.Count > 1 && Navigation.NavigationStack[^1] == this) { await Navigation.PopAsync(); return; }
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("TemplateEditor.Leave", ex);
            try
            {
                var nav = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
                while (nav is not null && nav.ModalStack.Count > 0) await nav.PopModalAsync();
                await Shell.Current.GoToAsync("//templates");
            }
            catch (Exception ex2) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("TemplateEditor.Leave (fallback)", ex2); }
        }
    }
    //JJQR - END

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
        await LeaveAsync(); //JJQR - AI (Nov 24, 2026) - was Navigation.PopAsync() (failed when opened as an overlay) //JJQR - END
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
        //JJQR - AI (Nov 21, 2026) - which patients the template is for (saved with the template; drives the Adult / Pedia filter in a consultation)
        var category = row?.Category is "Adult" or "Pedia" ? row.Category : ClinicNode.Core.Doctor.TemplateSpecialty.DefaultCategory(row?.Title);
        var startCategory = category;
        //JJQR - END
        var preview = false;
        var area = StaffUi.Column(12);
        p._reason = () => DoctorTemplateRules.ValidateSoap(title, fields);
        p._dirty = () => title != startTitle || category != startCategory || !fields.SequenceEqual(initial);
        void Render()
        {
            area.Children.Clear();
            if (!preview)
            {
                area.Children.Add(StaffUi.Field("Template name", p.Line(title, "e.g. \"Annual Physical — Normal\"", v => title = v)));
                area.Children.Add(StaffUi.Field("For", DoctorExtraUi.Segmented(["Adult", "Pedia"], category == "Pedia" ? 1 : 0, i => category = i == 1 ? "Pedia" : "Adult"), "Decides which group the template shows under when you pick one in a consultation."));
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
        var modes = DoctorExtraUi.Segmented(["Edit", "Preview"], 0, i => { preview = i == 1; Render(); });
        modes.HorizontalOptions = LayoutOptions.Start;
        Render();
        p._body.Children.Add(modes);
        p._body.Children.Add(area);
        p._save = async () =>
        {
            var draft = new TplSoap(row?.Id ?? Guid.Empty, title.Trim(), false, Clean(fields[0]), Clean(fields[1]), Clean(fields[2]), Clean(fields[3]), Clean(fields[4]), category);
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
        var list = StaffUi.Column(10);
        //JJQR - AI (Nov 21, 2026) - accordion: only ONE medicine is open for editing; the others fold into a one-line summary (name · dose · qty · route) so a long set stays readable
        Guid? open = lines.Count == 1 ? lines[0].Key : null;
        static string Summary(RxDraft d) => string.Join("  ·  ", new[] { d.Dosage, d.Quantity, d.Route, d.Instruction }.Where(x => !string.IsNullOrWhiteSpace(x)));
        void Render()
        {
            list.Children.Clear();
            for (var i = 0; i < lines.Count; i++)
            {
                var idx = i;
                var d = lines[i];
                var isOpen = open == d.Key;
                var badge = new Border
                {
                    WidthRequest = 28, HeightRequest = 28, StrokeThickness = 0, VerticalOptions = LayoutOptions.Center,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 }, BackgroundColor = ClinicTheme.Color("Primary").WithAlpha(0.12f),
                    Content = new Label { Text = (idx + 1).ToString(), FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = ClinicTheme.Color("Primary"), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
                };
                var name = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center, Children =
                {
                    new Label { Text = string.IsNullOrWhiteSpace(d.GenericName) ? "New medicine" : d.GenericName, Style = ClinicTheme.Style("LabelLg"), TextColor = string.IsNullOrWhiteSpace(d.GenericName) ? ClinicTheme.Color("OnSurfaceVariant") : ClinicTheme.Color("OnSurface") },
                    new Label { Text = Summary(d), Style = ClinicTheme.Style("MutedSm"), IsVisible = !isOpen && Summary(d).Length > 0, LineBreakMode = LineBreakMode.TailTruncation },
                } };
                var up = StaffUi.Btn("↑", () => { Move(d.Key, -1); return Task.CompletedTask; }, "GhostButton"); up.IsEnabled = idx > 0;
                var down = StaffUi.Btn("↓", () => { Move(d.Key, 1); return Task.CompletedTask; }, "GhostButton"); down.IsEnabled = idx < lines.Count - 1;
                var toggle = StaffUi.Btn(isOpen ? "Done" : "Edit", () => { open = isOpen ? null : d.Key; Render(); return Task.CompletedTask; }, isOpen ? "PrimaryButton" : "SecondaryButton");
                var remove = StaffUi.DangerGhost("Remove", () => { lines.RemoveAll(x => x.Key == d.Key); if (open == d.Key) open = null; Render(); return Task.CompletedTask; }); remove.IsEnabled = lines.Count > 1;
                var actions = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center, Children = { up, down, toggle, remove } };
                var head = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                head.Add(badge, 0, 0); head.Add(name, 1, 0); head.Add(actions, 2, 0);
                var body = new VerticalStackLayout { Spacing = 12, Children = { head } };
                if (isOpen) { body.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.5f) }); body.Children.Add(DoctorRxFields.TemplateLine(d, catalog)); }
                var card = StaffUi.Row(body);
                if (isOpen && card is Border b) { b.Stroke = ClinicTheme.Color("Primary"); b.StrokeThickness = 1.5; }
                if (!isOpen) card.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => UiTurn.Next(() => { open = d.Key; Render(); })) }); //JJQR - AI (Nov 24, 2026) - Windows: the tapped card is rebuilt, so redraw on the next UI turn //JJQR - END
                list.Children.Add(card);
            }
            p.Refresh();
        }
        //JJQR - END
        void Move(Guid key, int dir)
        {
            var ids = DoctorTemplateRules.MoveId(lines.Select(l => l.Key).ToList(), key, dir);
            lines = ids.Select(k => lines.First(l => l.Key == k)).ToList();
            Render();
        }
        Render();
        p._body.Children.Add(list);
        p._body.Children.Add(StaffUi.Wrap(DoctorExtraUi.Spaced(StaffUi.Btn("+ Add medicine", () => { var n = new RxDraft(); lines.Add(n); open = n.Key; Render(); return Task.CompletedTask; }, "PrimaryButton"))));
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
