//JJQR - AI (21 Nov 2026) - (1:1 pass 21 Nov, read from TemplatesWorkspace.tsx + SoapTab/PhrasesTab/RxTab/FavoritesTab/CertsTab/shared.tsx) Doctor Templates workspace, 1:1 with Project02-fe doctor/templates (TemplatesWorkspace + SOAP / Phrases / Prescriptions / Favorites / Certificates tabs):
// search across tabs, per-tab list, add / edit (pushed DoctorTemplateEditorPage) / duplicate / delete-with-5s-undo, phrase + favorite reorder, "Restore built-in templates".
// Online-first through IDoctorTemplatesWorkspaceService (offline = the read from local SQLite, writes say "needs the clinic server"). Not routed here: see DoctorExtra2Module.
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.App.Views.Doctor.Extra; //JJQR - AI (21 Nov 2026) DoctorTemplateEditorPage / DoctorAppointmentDetailPage / DoctorExtraUi live in the Extra namespace //JJQR - END
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra2;

//JJQR - AI (21 Nov 2026) - deep links like the web: doctor-templates?tab=&field=&id=  (id = a template id, or "new" to open the new-item editor; field picks the phrase section)
[QueryProperty(nameof(TabText), "tab")]
[QueryProperty(nameof(FieldText), "field")]
[QueryProperty(nameof(IdText), "id")]
//JJQR - END
public sealed class DoctorTemplatesWorkspacePage : StaffLivePage
{
    private readonly VerticalStackLayout _list = StaffUi.Column(12);
    private readonly Entry _search = new();
    private readonly VerticalStackLayout _scopeHost = StaffUi.Column(0);
    private readonly Label _ok = StaffUi.Success();
    private readonly Label _err = StaffUi.Error();
    private readonly HashSet<Guid> _hidden = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _pending = [];
    private TemplatesWorkspace? _lib;
    private Guid? _doctorId;
    private bool _loaded, _loadFailed;
    private string _tab = "soap";
    private bool _allTabs;
    //JJQR - AI (21 Nov 2026) - web ViewToggle ("List" default / "Cards") and deep-link state
    private bool _cards;
    private ScrollView? _scroll;
    private View? _scrollTarget;     // linked phrase section / opened item, set while rendering
    private FlexLayout? _cardHost;   // Cards mode: wrapping host (web grid-cols-1 xl:grid-cols-2)
    private bool _wide;              // web xl breakpoint (>= 1280 px): two card columns
    private double _pageWidth;
    private string? _qTab, _qField, _qId;
    private bool _linkPending, _missing;
    private string? _fieldFocus;   // the phrase section the link pointed at (web: highlighted ring)
    private Guid? _openId;          // the item the link opened (web: highlight)
    public string? TabText { get => _qTab; set { _qTab = value; _linkPending = true; } }
    public string? FieldText { get => _qField; set { _qField = value; _linkPending = true; } }
    public string? IdText { get => _qId; set { _qId = value; _linkPending = true; } }
    //JJQR - END

    public DoctorTemplatesWorkspacePage(IServiceProvider sp) : base(sp, "Templates", live: false)
    {
        _search.TextChanged += (_, _) => UiTurn.Next(Render); //JJQR - AI (Nov 24, 2026) - was Render() inside the search box's own event, which takes that same box out of the screen (Windows crash pattern 2); Mac unchanged //JJQR - END
        // web page header (app/doctor/templates/page.tsx)
        _scroll = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Header("Consultation templates", "SOAP phrases, diagnoses, prescriptions, and certificates you reuse during consultations."),
                LoadError, _ok, _err,
                _list,
            },
        });
        Content = _scroll; //JJQR - AI (21 Nov 2026) kept so a deep link can scroll the target into view // JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - Cards mode is one column on narrow windows and two from the web's xl breakpoint (1280 px)
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0) return;
        var wide = width >= 1280;
        var changed = wide != _wide || Math.Abs(width - _pageWidth) > 40;
        _wide = wide; _pageWidth = width;
        if (changed && _cards && _loaded) Render();
    }

    private double CardWidth() => _wide ? Math.Max(240, (_pageWidth - 48 - 8) / 2) : Math.Max(240, _pageWidth - 48);

    private async Task ScrollToTargetAsync()
    {
        if (_scroll is null || _scrollTarget is not { } t) return;
        await Task.Delay(60); // let the new rows lay out first
        try { await _scroll.ScrollToAsync(t, ScrollToPosition.Start, true); } catch { /* target not in the scroll content (yet): nothing to do */ }
    }
    //JJQR - END

    protected override async Task LoadAsync()
    {
        _doctorId = DoctorExtraUi.DoctorId(Sp);
        if (_doctorId is null) { _lib = null; _loaded = true; Render(); return; }
        try { _lib = await Svc<IDoctorTemplatesWorkspaceService>().GetWorkspaceAsync(_doctorId.Value); _loadFailed = false; }
        catch { _loadFailed = true; }
        _loaded = true;
        Render();
        if (_linkPending && _lib is not null) { _linkPending = false; await ApplyLinkAsync(); }
    }

    //JJQR - AI (21 Nov 2026) - web: the URL selects the tab, the phrase section and the open item; an id that is gone / not yours shows "That item no longer exists or isn’t yours to open." + Dismiss
    private async Task ApplyLinkAsync()
    {
        if (_doctorId is not { } doctorId || _lib is not { } lib) return;
        if (string.IsNullOrWhiteSpace(_qTab) && string.IsNullOrWhiteSpace(_qField) && string.IsNullOrWhiteSpace(_qId)) return;
        _tab = DoctorTemplateRules.ParseTab(_qTab);
        _fieldFocus = DoctorTemplateRules.ParseField(_qField);
        _allTabs = false; _search.Text = ""; _missing = false; _openId = null;
        var id = _qId?.Trim();
        _qTab = _qField = _qId = null;
        if (Guid.TryParse(id, out var openGuid)) _openId = openGuid; // highlight + scroll to the opened row even before the editor opens
        Render();
        await ScrollToTargetAsync();
        if (string.IsNullOrEmpty(id)) return;
        var svc = Svc<IDoctorTemplatesWorkspaceService>();
        if (id.Equals("new", StringComparison.OrdinalIgnoreCase))
        {
            switch (_tab)
            {
                case "soap": await Open(DoctorTemplateEditorPage.Soap(Sp, doctorId, null)); break;
                case "phrases": await Open(DoctorTemplateEditorPage.Phrase(Sp, doctorId, null, _fieldFocus ?? DoctorTemplateRules.PhraseFields[0].Value)); break;
                case "rx": await Open(DoctorTemplateEditorPage.RxSet(Sp, doctorId, null, await svc.ListMedicinesAsync())); break;
                case "favorites": await Open(DoctorTemplateEditorPage.Favorite(Sp, doctorId, null, await svc.ListMedicinesAsync())); break;
                default: await Open(DoctorTemplateEditorPage.Cert(Sp, doctorId, null)); break;
            }
            return;
        }
        if (!Guid.TryParse(id, out var gid)) { _missing = true; Render(); return; }
        switch (_tab)
        {
            case "soap": if (lib.Soap.FirstOrDefault(x => x.Id == gid) is { } so) await Open(DoctorTemplateEditorPage.Soap(Sp, doctorId, so)); else _missing = true; break;
            case "phrases": if (lib.Phrases.FirstOrDefault(x => x.Id == gid) is { } ph) await Open(DoctorTemplateEditorPage.Phrase(Sp, doctorId, ph, ph.Field)); else _missing = true; break;
            case "rx": if (lib.Rx.FirstOrDefault(x => x.Id == gid) is { } rx) await Open(DoctorTemplateEditorPage.RxSet(Sp, doctorId, rx, await svc.ListMedicinesAsync())); else _missing = true; break;
            case "favorites": if (lib.Favorites.FirstOrDefault(x => x.Id == gid) is { } fv) await Open(DoctorTemplateEditorPage.Favorite(Sp, doctorId, fv, await svc.ListMedicinesAsync())); else _missing = true; break;
            default: if (lib.Certificates.FirstOrDefault(x => x.Id == gid) is { } ce) await Open(DoctorTemplateEditorPage.Cert(Sp, doctorId, ce)); else _missing = true; break;
        }
        Render();
    }
    //JJQR - END

    private void Say(string text, bool ok) => DoctorExtraUi.Toast(_ok, _err, text, ok);

    private async Task Run(Task<DoctorResult> work, string okText, string failText)
    {
        var r = await work;
        Say(r.Succeeded ? okText : (r.Error ?? failText), r.Succeeded);
        if (r.Succeeded) await ReloadAsync();
    }

    //JJQR - AI (Nov 25, 2026) - code review: a quick double-tap opened two editors on top of each other (saving the second could overwrite the first)
    private bool _opening;
    //JJQR - END

    private async Task Open(DoctorTemplateEditorPage editor)
    {
        //JJQR - AI (Nov 25, 2026) - code review: ignore a second tap while an editor is opening or already open
        var nav = Shell.Current?.Navigation ?? Navigation;
        if (_opening || nav.NavigationStack.Any(p => p is DoctorTemplateEditorPage)) return;
        _opening = true;
        try
        {
        //JJQR - END
        editor.Saved += t => { Say(t, true); _ = ReloadAsync(); };
        //JJQR - AI (Nov 24, 2026) - owner (Windows): right after a Delete (its confirm dialog had just closed) the editor opened as a full-window overlay with no sidebar,
        // and could not close. Always open it inside the app's own frame (the Shell), never on top of a leftover dialog.
        //JJQR - AI (Nov 25, 2026) - code review: a confirm dialog still on the window's modal stack would host the push as an overlay. Dialogs now close
        // before they answer, so this is only a safety net: Windows only, and only our own confirm dialogs (StaffDialog), never other overlays.
        if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            try
            {
                var window = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
                while (window is not null && window.ModalStack.Count > 0 && window.ModalStack[^1] is StaffDialog) await window.PopModalAsync(false);
            }
            catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Templates.Open (close leftover dialog)", ex); }
        }
        //JJQR - END
        await nav.PushAsync(editor);
        //JJQR - END
        //JJQR - AI (Nov 25, 2026) - double-tap guard (see _opening)
        }
        finally { _opening = false; }
        //JJQR - END
    }

    private static string TabLabel(string id) => DoctorTemplateRules.Tabs.First(t => t.Id == id).Label;

    private string Query => _allTabs ? "" : _search.Text ?? "";

    //JJQR - AI (Nov 22, 2026) - owner: switching tabs felt slow (the whole list is rebuilt before anything is painted). Paint "Loading..." first, then build the rows.
    private void RenderSoon()
    {
        if (!_loaded) { Render(); return; }
        DetachSearch(); //JJQR - AI (Nov 24, 2026) - before Clear, while the old toolbar is still on screen (see DetachSearch) //JJQR - END
        _list.Children.Clear();
        _list.Children.Add(ClinicTheme.Text("Loading…", "MutedBody"));
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(30), Render);
    }
    //JJQR - END

    //JJQR - AI (Nov 24, 2026) - owner (Windows): Templates still crashed (0xC000027B) when switching tab / "All tabs" / "Cards". Errors trace: COMException "No installed components were detected"
    // at _list.Children.Add(toolbar), from the 30 ms delayed redraw. RenderSoon cleared the list first; by the time Render ran, Windows had already let go of the old toolbar
    // (its handler gone), so taking the search box out of it no longer removed it on the Windows side and the box still had a Windows parent. Take it out BEFORE clearing,
    // and on Windows also from any Windows parent it still has.
    private void DetachSearch()
    {
        if (_search.Parent is Layout oldToolbar) oldToolbar.Children.Remove(_search);
#if WINDOWS
        try
        {
            var handler = _search.Handler as IViewHandler;
            var native = (handler?.ContainerView ?? handler?.PlatformView) as Microsoft.UI.Xaml.FrameworkElement;
            if (native is not null && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(native) is Microsoft.UI.Xaml.Controls.Panel panel)
                panel.Children.Remove(native);
        }
        catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Templates.DetachSearch", ex); }
#endif
    }
    //JJQR - END

    private void Render()
    {
        DetachSearch(); //JJQR - AI (Nov 24, 2026) - before Clear (see DetachSearch) //JJQR - END
        _list.Children.Clear();
        _scrollTarget = null; _cardHost = null; //JJQR - AI (21 Nov 2026) // JJQR - END
        if (!_loaded) { _list.Children.Add(ClinicTheme.Text("Loading your templates…", "MutedBody")); return; }
        if (_doctorId is not { } doctorId) { _list.Children.Add(ClinicTheme.Text("Templates are available to signed-in doctors.", "MutedBody")); return; }

        var counts = new Dictionary<string, int>
        {
            ["soap"] = _lib?.Soap.Count ?? 0, ["phrases"] = _lib?.Phrases.Count ?? 0, ["rx"] = _lib?.Rx.Count ?? 0,
            ["favorites"] = _lib?.Favorites.Count ?? 0, ["certificates"] = _lib?.Certificates.Count ?? 0,
        };
        // nav: "SOAP (3)" ... (web VISIBLE_TEMPLATE_TABS; Diagnoses stays hidden)
        _list.Children.Add(DoctorExtraUi.Tabs(DoctorTemplateRules.Tabs.Select(t => (t.Id, $"{t.Label} ({counts[t.Id]})")).ToArray(), () => _tab, id => { _tab = id; RenderSoon(); }));
        //JJQR - AI (Nov 24, 2026) - owner (Windows): "COMException: No installed components were detected" on the Templates page. The search box is reused on every draw but was still a child of the previous draw's toolbar;
        // Windows refuses a view with two parents (the Mac allows it). Take it out of the old toolbar first (same fix as the walk-in search box).
        DetachSearch(); //JJQR - AI (Nov 24, 2026) - was: remove from the old toolbar here, after Clear (too late on Windows) //JJQR - END
        //JJQR - END
        // search row: input, "This tab" / "All tabs" chips
        _search.Placeholder = _allTabs ? "Search all tabs…" : "Search this tab…";
        //JJQR - AI (Nov 21, 2026) - one tidy toolbar row: wide search | scope toggle | view toggle (the old chips wrapped onto three lines)
        _search.HorizontalOptions = LayoutOptions.Fill;
        _search.WidthRequest = -1;
        _search.MaximumWidthRequest = 460;
        _search.HorizontalOptions = LayoutOptions.Start;
        _search.WidthRequest = 380;
        var scope = DoctorExtraUi.Segmented(["This tab", "All tabs"], _allTabs ? 1 : 0, i => { _allTabs = i == 1; RenderSoon(); });
        var viewToggle = DoctorExtraUi.Segmented(["List", "Cards"], _cards ? 1 : 0, i => { _cards = i == 1; RenderSoon(); });
        _primaryHost = new ContentView { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
        var toolbar = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) } };
        toolbar.Add(_search, 0, 0);
        toolbar.Add(scope, 1, 0);
        toolbar.Add(viewToggle, 2, 0);
        toolbar.Add(_primaryHost, 3, 0);
        _list.Children.Add(toolbar);
        //JJQR - END
        if (_missing)
            _list.Children.Add(StaffUi.Card(
                ClinicTheme.Text("That item no longer exists or isn’t yours to open.", "BodyMd"),
                StaffUi.Btn("Dismiss", () => { _missing = false; _openId = null; Render(); return Task.CompletedTask; }, "SecondaryButton")));
        //JJQR - END

        if (_loadFailed || _lib is null)
        {
            var label = _tab switch { "soap" => "SOAP templates", "phrases" => "phrases", "rx" => "prescription sets", "favorites" => "favorite medicines", _ => "certificate templates" };
            _list.Children.Add(StaffUi.Card(
                ClinicTheme.Text($"Couldn’t load your {label}. The other tabs still work.", "BodyMd"),
                StaffUi.Btn("Retry", () => ReloadAsync(), "SecondaryButton")));
            return;
        }
        if (_allTabs && !string.IsNullOrWhiteSpace(_search.Text)) { RenderSearch(_search.Text!, _lib); return; }

        var svc = Svc<IDoctorTemplatesWorkspaceService>();
        var lib = _lib;
        var q = Query;
        bool M(params string?[] t) => DoctorTemplateRules.MatchesQuery(q, t);
        switch (_tab)
        {
            case "soap":
            {
                Add("New SOAP template", () => Open(DoctorTemplateEditorPage.Soap(Sp, doctorId, null)));
                var rows = lib.Soap.OrderBy(t => t.Title, StringComparer.CurrentCulture).ToList();
                var visible = rows.Where(t => !_hidden.Contains(t.Id)).Where(t => M(t.Title, t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan)).ToList();
                if (visible.Count == 0) Hint(rows.Count == 0 ? "No SOAP templates yet. Create one, or save one from a consultation." : "No SOAP templates match these filters.");
                foreach (var t in visible)
                {
                    var row = t;
                    Item(row.Id, row.Title, (row.Category is "Adult" or "Pedia" ? row.Category : ClinicNode.Core.Doctor.TemplateSpecialty.DefaultCategory(row.Title)) + (DoctorTemplateRules.IsCopyTitle(row.Title) ? " · Copy" : ""),
                        DoctorTemplateRules.Truncate(new[] { row.ChiefComplaint, row.Subjective, row.Assessment, row.Plan, row.Objective }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? ""),
                        () => Open(DoctorTemplateEditorPage.Soap(Sp, doctorId, row)),
                        [("Edit", () => Open(DoctorTemplateEditorPage.Soap(Sp, doctorId, row))),
                         ("Duplicate", () => Run(svc.SaveSoapAsync(doctorId, null, row with { Id = Guid.Empty, IsSystem = false, Title = DoctorTemplateRules.CopyTitle(row.Title, rows.Select(s => s.Title)) }),
                              $"Duplicated to “{DoctorTemplateRules.CopyTitle(row.Title, rows.Select(s => s.Title))}”.", "Couldn’t duplicate the template. Please try again."))],
                        () => svc.DeleteSoapAsync(row.Id));
                }
                Restore("soap");
                break;
            }
            case "phrases":
            {
                Add("Add phrase", () => Open(DoctorTemplateEditorPage.Phrase(Sp, doctorId, null, DoctorTemplateRules.PhraseFields[0].Value)));
                foreach (var (value, label) in DoctorTemplateRules.PhraseFields)
                {
                    var all = lib.Phrases.Where(p => p.Field == value).OrderBy(p => p.SortOrder).ToList();
                    var visible = all.Where(p => !_hidden.Contains(p.Id)).Where(p => M(p.Label, p.Body)).ToList();
                    var filtering = !string.IsNullOrWhiteSpace(q);
                    var section = StaffUi.Column(6);
                    var headRow = StaffUi.Wrap(DoctorExtraUi.Spaced(ClinicTheme.Text($"{label} ({all.Count})", "LabelLg"), 12, 0),
                        StaffUi.Link($"Add to {label}", () => Open(DoctorTemplateEditorPage.Phrase(Sp, doctorId, null, value))));
                    section.Children.Add(headRow);
                    if (visible.Count == 0) section.Children.Add(ClinicTheme.Text(all.Count == 0 ? "No phrases yet." : "No phrases match your search.", "MutedBody"));
                    foreach (var p in visible)
                    {
                        var row = p; var idx = all.FindIndex(x => x.Id == row.Id);
                        var ids = all.Select(x => x.Id).ToList();
                        var col = StaffUi.Column(4);
                        col.Children.Add(ClinicTheme.Text(row.Label, "LabelLg"));
                        col.Children.Add(ClinicTheme.Text(DoctorTemplateRules.Truncate(row.Body, 120), "MutedSm"));
                        var acts = new HorizontalStackLayout { Spacing = 2 };
                        if (!filtering) { var upB = StaffUi.Btn("↑", () => Reorder(svc, doctorId, ids, row.Id, -1, true), "SegmentedButton"); upB.IsEnabled = idx > 0; acts.Children.Add(DoctorExtraUi.Spaced(upB)); }
                        if (!filtering) { var dnB = StaffUi.Btn("↓", () => Reorder(svc, doctorId, ids, row.Id, 1, true), "SegmentedButton"); dnB.IsEnabled = idx < all.Count - 1; acts.Children.Add(DoctorExtraUi.Spaced(dnB)); }
                        acts.Children.Add(DoctorExtraUi.Spaced(StaffUi.Btn("Move to…", () => MoveTo(svc, doctorId, row), "SegmentedButton")));
                        acts.Children.Add(DoctorExtraUi.Spaced(StaffUi.Btn("Edit", () => Open(DoctorTemplateEditorPage.Phrase(Sp, doctorId, row, row.Field)), "SegmentedButton")));
                        acts.Children.Add(DoctorExtraUi.Spaced(StaffUi.Btn("Duplicate", () => Run(svc.SavePhraseAsync(doctorId, null, row.Field, DoctorTemplateRules.CopyTitle(row.Label, all.Select(x => x.Label)), row.Body),
                            $"Duplicated to “{DoctorTemplateRules.CopyTitle(row.Label, all.Select(x => x.Label))}”.", "Couldn’t duplicate the phrase. Please try again."), "SegmentedButton")));
                        acts.Children.Add(DoctorExtraUi.Spaced(StaffUi.DangerGhost("Delete", () => Confirm(row.Id, row.Label, () => svc.DeletePhraseAsync(row.Id)))));
                        //JJQR - AI (Nov 21, 2026) - phrase row: text on the left, actions on the right (they used to hang under the text and wrap)
                        var phraseGrid = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                        acts.VerticalOptions = LayoutOptions.Center;
                        phraseGrid.Add(col, 0, 0); phraseGrid.Add(acts, 1, 0);
                        var phraseRow = StaffUi.Row(phraseGrid);
                        //JJQR - END
                        if (_openId == row.Id) { phraseRow.BackgroundColor = ClinicTheme.Color("Primary").WithAlpha(0.05f); _scrollTarget ??= phraseRow; } //JJQR - AI (21 Nov 2026) web: highlighted opened row // JJQR - END
                        section.Children.Add(phraseRow);
                    }
                    var sectionCard = StaffUi.Card(section);
                    if (_fieldFocus == value && sectionCard is Border sb) { sb.Stroke = ClinicTheme.Color("Primary"); sb.StrokeThickness = 2; _scrollTarget ??= sectionCard; } //JJQR - AI (21 Nov 2026) web ring on the linked field // JJQR - END
                    _list.Children.Add(sectionCard);
                }
                Restore("phrase");
                break;
            }
            case "rx":
            {
                Add("New prescription set", async () => await Open(DoctorTemplateEditorPage.RxSet(Sp, doctorId, null, await svc.ListMedicinesAsync())));
                var rows = lib.Rx.OrderBy(t => t.Title, StringComparer.CurrentCulture).ToList();
                var visible = rows.Where(t => !_hidden.Contains(t.Id)).Where(t => M(new[] { t.Title }.Concat(t.Lines.SelectMany(l => new[] { l.GenericName, l.Dosage, l.Instruction, l.Indication })).ToArray())).ToList();
                if (visible.Count == 0) Hint(rows.Count == 0 ? "No prescription sets yet." : "No prescription sets match these filters.");
                foreach (var t in visible)
                {
                    var row = t;
                    var meds = $"{row.Lines.Count} med{(row.Lines.Count == 1 ? "" : "s")}";
                    Item(row.Id, row.Title, (DoctorTemplateRules.IsCopyTitle(row.Title) ? "Copy · " : "") + meds, string.Join("\n", row.Lines.Select(DoctorTemplateRules.RxSig)),
                        async () => await Open(DoctorTemplateEditorPage.RxSet(Sp, doctorId, row, await svc.ListMedicinesAsync())),
                        [("Edit", async () => await Open(DoctorTemplateEditorPage.RxSet(Sp, doctorId, row, await svc.ListMedicinesAsync()))),
                         ("Duplicate", () => Run(svc.SaveRxSetAsync(doctorId, null, DoctorTemplateRules.CopyTitle(row.Title, rows.Select(x => x.Title)), row.Lines),
                              $"Duplicated to “{DoctorTemplateRules.CopyTitle(row.Title, rows.Select(x => x.Title))}”.", "Couldn’t duplicate the set. Please try again."))],
                        () => svc.DeleteRxSetAsync(row.Id));
                }
                Restore("rx");
                break;
            }
            case "favorites":
            {
                Add("Add favorite medicine", async () => await Open(DoctorTemplateEditorPage.Favorite(Sp, doctorId, null, await svc.ListMedicinesAsync())));
                var shown = lib.Favorites.Where(f => !_hidden.Contains(f.Id)).OrderBy(f => f.SortOrder).ToList();
                var visible = shown.Where(f => M(f.GenericName, f.Dosage, f.Instruction)).ToList();
                var filtering = !string.IsNullOrWhiteSpace(q);
                if (visible.Count == 0)
                {
                    if (lib.Favorites.Count == 0)
                        _list.Children.Add(StaffUi.Card(
                            ClinicTheme.Text("No favorite medicines yet", "LabelLg"),
                            ClinicTheme.Text("Favorites are the medicines you prescribe most, one tap away in the prescription step.", "MutedBody"),
                            ClinicTheme.Text("1. Tap Add favorite medicine.\n2. Pick the medicine, then add its usual dose, quantity and directions.\n3. Save. Use the ↑ ↓ buttons to put your top picks first.", "MutedBody"),
                            StaffUi.Btn("Add favorite medicine", async () => await Open(DoctorTemplateEditorPage.Favorite(Sp, doctorId, null, await svc.ListMedicinesAsync())))));
                    else Hint("No favorites match your search.");
                }
                foreach (var f in visible)
                {
                    var row = f; var idx = shown.FindIndex(x => x.Id == row.Id);
                    var ids = shown.Select(x => x.Id).ToList();
                    var col = StaffUi.Column(4);
                    col.Children.Add(ClinicTheme.Text(ClinicNode.App.Views.Doctor.Extra.DoctorPatientChartPage.NiceMedicine(row.GenericName), "LabelLg"));
                    col.Children.Add(ClinicTheme.Text(string.Join(" · ", new[] { row.Dosage, row.Quantity, row.Instruction }.Where(x => !string.IsNullOrWhiteSpace(x))), "MutedSm"));
                    var acts = new HorizontalStackLayout { Spacing = 2 };
                    if (!filtering) { var upB = StaffUi.Btn("↑", () => Reorder(svc, doctorId, ids, row.Id, -1, false), "SegmentedButton"); upB.IsEnabled = idx > 0; acts.Children.Add(DoctorExtraUi.Spaced(upB)); }
                    if (!filtering) { var dnB = StaffUi.Btn("↓", () => Reorder(svc, doctorId, ids, row.Id, 1, false), "SegmentedButton"); dnB.IsEnabled = idx < shown.Count - 1; acts.Children.Add(DoctorExtraUi.Spaced(dnB)); }
                    // favorites have no Duplicate and no built-ins (web A1 / A4)
                    acts.Children.Add(DoctorExtraUi.Spaced(StaffUi.Btn("Edit", async () => await Open(DoctorTemplateEditorPage.Favorite(Sp, doctorId, row, await svc.ListMedicinesAsync())), "SegmentedButton")));
                    acts.Children.Add(DoctorExtraUi.Spaced(StaffUi.DangerGhost("Delete", () => Confirm(row.Id, row.GenericName, () => svc.DeleteFavoriteAsync(row.Id)))));
                    var favGrid = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
                    acts.VerticalOptions = LayoutOptions.Center;
                    favGrid.Add(col, 0, 0); favGrid.Add(acts, 1, 0);
                    var favRow = StaffUi.Row(favGrid);
                    if (_openId == row.Id) { favRow.BackgroundColor = ClinicTheme.Color("Primary").WithAlpha(0.05f); _scrollTarget ??= favRow; } //JJQR - AI (21 Nov 2026) // JJQR - END
                    _list.Children.Add(favRow);
                }
                break;
            }
            default:
            {
                Add("New certificate template", () => Open(DoctorTemplateEditorPage.Cert(Sp, doctorId, null)));
                var rows = lib.Certificates.OrderBy(t => t.Title, StringComparer.CurrentCulture).ToList();
                var visible = rows.Where(t => !_hidden.Contains(t.Id)).Where(t => M(t.Title, t.DiagnosisText, t.Recommendations, t.PurposeException)).ToList();
                if (visible.Count == 0) Hint(rows.Count == 0 ? "No certificate templates yet." : "No certificate templates match these filters.");
                foreach (var t in visible)
                {
                    var row = t;
                    Item(row.Id, row.Title, DoctorTemplateRules.IsCopyTitle(row.Title) ? "Copy" : null,
                        DoctorTemplateRules.Truncate(new[] { row.DiagnosisText, row.Recommendations, row.PurposeException }.FirstOrDefault(x => !string.IsNullOrEmpty(x)) ?? ""),
                        () => Open(DoctorTemplateEditorPage.Cert(Sp, doctorId, row)),
                        [("Edit", () => Open(DoctorTemplateEditorPage.Cert(Sp, doctorId, row))),
                         ("Duplicate", () => Run(svc.SaveCertAsync(doctorId, null, row with { Id = Guid.Empty, IsSystem = false, Title = DoctorTemplateRules.CopyTitle(row.Title, rows.Select(x => x.Title)) }),
                              $"Duplicated to “{DoctorTemplateRules.CopyTitle(row.Title, rows.Select(x => x.Title))}”.", "Couldn’t duplicate the template. Please try again."))],
                        () => svc.DeleteCertAsync(row.Id));
                }
                Restore("cert");
                break;
            }
        }
    }

    private ContentView _primaryHost = new();

    //JJQR - AI (Nov 21, 2026) - the "+ New …" button lives in the toolbar row (right) instead of on a row of its own
    private void Add(string text, Func<Task> add)
    {
        var b = StaffUi.Btn("+ " + text, add);
        _primaryHost.Content = b;
    }
    //JJQR - END

    private void Hint(string text) => _list.Children.Add(StaffUi.Card(ClinicTheme.Text(text, "MutedBody")));

    /// <summary>web RestoreBuiltIns: a small text link at the bottom of the SOAP / Phrases / Prescriptions / Certificates tabs (favorites have none) + the confirm modal.</summary>
    private void Restore(string kind) =>
        _list.Children.Add(StaffUi.Link("Restore built-in templates", async () =>
        {
            if (_doctorId is not { } id) return;
            if (!await DoctorExtraUi.ConfirmAsync(this, DoctorTemplateRules.RestoreTitle, DoctorTemplateRules.RestoreBody, "Restore")) return;
            var r = await Svc<IDoctorTemplatesWorkspaceService>().RestoreBuiltInsAsync(id, kind);
            Say(r.Succeeded ? DoctorTemplateRules.RestoredToast(r.Restored) : "Couldn’t restore built-in templates. Please try again.", r.Succeeded);
            if (r.Succeeded) await ReloadAsync();
        }));

    private async Task Reorder(IDoctorTemplatesWorkspaceService svc, Guid doctorId, IReadOnlyList<Guid> ids, Guid id, int dir, bool phrases)
    {
        var next = DoctorTemplateRules.MoveId(ids, id, dir);
        if (next.SequenceEqual(ids)) return;
        var r = await (phrases ? svc.ReorderPhrasesAsync(doctorId, next) : svc.ReorderFavoritesAsync(doctorId, next));
        if (!r.Succeeded) Say("Couldn’t save the new order. It was put back.", false);
        await ReloadAsync();
    }

    /// <summary>web MoveToMenu: move a phrase to another SOAP field.</summary>
    private async Task MoveTo(IDoctorTemplatesWorkspaceService svc, Guid doctorId, TplPhrase row)
    {
        var options = DoctorTemplateRules.PhraseFields.Where(f => f.Value != row.Field).Select(f => f.Label).ToArray();
        var pick = await DisplayActionSheet($"Move “{row.Label}” to…", "Cancel", null, options);
        var target = DoctorTemplateRules.PhraseFields.FirstOrDefault(f => f.Label == pick);
        if (target.Value is null) return;
        var r = await svc.SavePhraseAsync(doctorId, row.Id, target.Value, row.Label, row.Body);
        Say(r.Succeeded ? $"Moved “{row.Label}” to {target.Label}." : "Couldn’t move the phrase. It stays where it was.", r.Succeeded);
        if (r.Succeeded) await ReloadAsync();
    }

    private void Item(Guid id, string title, string? pill, string? preview, Func<Task> open, (string Text, Func<Task> Do)[] actions, Func<Task<DoctorResult>> delete)
    {
        //JJQR - AI (21 Nov 2026) - web ItemCard: list = compact single row (title + one-line preview, pill, actions); cards = bordered card, title, pill, up to 3 preview lines, actions underneath
        var highlight = _openId == id;
        var col = StaffUi.Column(4);
        var titleRow = StaffUi.Wrap(DoctorExtraUi.Spaced(ClinicTheme.Text(title, "LabelLg"), 8, 0));
        if (pill is not null) titleRow.Children.Add(new Border { Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(8, 2), VerticalOptions = LayoutOptions.Center, Content = new Label { Text = pill, Style = ClinicTheme.Style("PillLabelInfo") } });
        col.Children.Add(titleRow);
        if (!string.IsNullOrWhiteSpace(preview))
        {
            var pv = ClinicTheme.Text(preview, "MutedSm");
            pv.MaxLines = _cards ? 3 : 1;
            pv.LineBreakMode = LineBreakMode.TailTruncation;
            col.Children.Add(pv);
        }
        //JJQR - AI (Nov 21, 2026) - cards: actions under the text; list: actions on the RIGHT of the row, vertically centred
        View acts;
        if (_cards)
        {
            var wrapActs = StaffUi.Wrap();
            foreach (var (text, run) in actions) wrapActs.Children.Add(DoctorExtraUi.Spaced(StaffUi.Btn(text, run, "SegmentedButton")));
            wrapActs.Children.Add(DoctorExtraUi.Spaced(StaffUi.DangerGhost("Delete", () => Confirm(id, title, delete))));
            col.Children.Add(wrapActs);
            acts = wrapActs;
        }
        else
        {
            var rowActs = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
            foreach (var (text, run) in actions) rowActs.Children.Add(StaffUi.Btn(text, run, "SecondaryButton"));
            rowActs.Children.Add(StaffUi.DangerGhost("Delete", () => Confirm(id, title, delete)));
            acts = rowActs;
        }
        //JJQR - END
        View row;
        if (_cards)
            row = new Border
            {
                Padding = 12, Margin = new Thickness(0, 0, 0, 8), StrokeThickness = highlight ? 2 : 1,
                Stroke = ClinicTheme.Color(highlight ? "Primary" : "OutlineVariant"),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Content = col,
            };
        else
        {
            //JJQR - AI (Nov 21, 2026) - list row: text on the left, actions on the right
            var line = new Grid { ColumnSpacing = 16, Padding = new Thickness(6, 8), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
            line.Add(col, 0, 0);
            line.Add(acts, 1, 0);
            var r = new Border
            {
                StrokeThickness = 1, Stroke = ClinicTheme.Color(highlight ? "Primary" : "OutlineVariant"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 0, 8), BackgroundColor = highlight ? ClinicTheme.Color("PrimarySubtle") : ClinicTheme.Color("SurfaceContainerLowest"), Content = line,
            };
            row = r;
            //JJQR - END
        }
        row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await open()) });
        if (highlight) _scrollTarget ??= row;
        if (_cards)
        {
            row.WidthRequest = CardWidth();
            row.Margin = new Thickness(0, 0, 8, 8);
            if (_cardHost is null) { _cardHost = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap }; _list.Children.Add(_cardHost); }
            _cardHost.Children.Add(row);
        }
        else _list.Children.Add(row);
        //JJQR - END
    }

    private async Task Confirm(Guid id, string label, Func<Task<DoctorResult>> commit)
    {
        if (!await DoctorExtraUi.ConfirmAsync(this, DoctorTemplateRules.DeleteTitle, DoctorTemplateRules.DeleteBody(label), "Delete", danger: true)) return;
        DeleteWithUndo(id, label, commit);
    }

    /// <summary>web useUndoableDelete: hide now, "Deleted “x”." + Undo for 5 s, then the real DELETE (a failure keeps the item).</summary>
    private void DeleteWithUndo(Guid id, string label, Func<Task<DoctorResult>> commit)
    {
        if (_pending.ContainsKey(id)) return;
        var cts = new CancellationTokenSource();
        _pending[id] = cts;
        _hidden.Add(id);
        Render();
        _list.Children.Insert(0, StaffUi.Link($"Deleted “{label}”. Undo", () => { cts.Cancel(); return Task.CompletedTask; }));
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(5000, cts.Token); } catch (TaskCanceledException) { }
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                _pending.Remove(id);
                if (cts.IsCancellationRequested) { _hidden.Remove(id); Say($"Restored “{label}”.", true); Render(); return; }
                var r = await commit();
                _hidden.Remove(id);
                if (!r.Succeeded) Say($"Couldn’t delete “{label}”. It was kept.", false);
                await ReloadAsync();
            });
        });
    }

    private void RenderSearch(string query, TemplatesWorkspace lib)
    {
        var hits = DoctorTemplateRules.SearchAll(query, lib);
        if (hits.Values.Sum(h => h.Count) == 0) { _list.Children.Add(ClinicTheme.Text($"Nothing matches “{query.Trim()}” in any tab.", "MutedBody")); return; }
        foreach (var (id, label) in DoctorTemplateRules.Tabs)
        {
            if (!hits.TryGetValue(id, out var h) || h.Count == 0) continue;
            _list.Children.Add(ClinicTheme.Text($"{label} ({h.Count})", "LabelLg"));
            foreach (var hit in h)
            {
                var tab = id;
                var col = StaffUi.Column(2);
                col.Children.Add(ClinicTheme.Text(hit.Title, "LabelLg"));
                if (!string.IsNullOrWhiteSpace(hit.Snippet)) col.Children.Add(ClinicTheme.Text(hit.Snippet, "MutedSm"));
                var row = StaffUi.Row(col);
                row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => UiTurn.Next(() => { _allTabs = false; _tab = tab; if (string.IsNullOrEmpty(_search.Text)) Render(); else _search.Text = ""; })) }); //JJQR - AI (Nov 24–25, 2026) - Windows: the tapped row is removed by the redraw, so redraw on the next UI turn; clearing the box already redraws (TextChanged), so redraw only once //JJQR - END
                _list.Children.Add(row);
            }
        }
    }
}
//JJQR - END
