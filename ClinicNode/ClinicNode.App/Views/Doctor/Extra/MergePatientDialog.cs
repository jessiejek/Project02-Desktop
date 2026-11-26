//JJQR - AI (2026-11-26) - GAV-12: owner (t118u): "the doctor will click MERGE TO EXISTING ACCOUNT". Opened from "Merge to existing patient" (consultation
// header, right after "Wrong patient?") and "Merge…" (patient chart, right below Edit details). Web: MergePatientDialog. Online only, syncs first:
//   1. this computer's unsent work for the CURRENT patient is synced (IPatientSyncGate); if any is still waiting, nothing opens
//   2. search the EXISTING (older) record; choosing it loads the side-by-side compare (rows that differ marked ●; the picked record is KEPT, its details
//      win, blanks are filled), the visits open today (a warning, not a stop) and "both have portal accounts" (refused)
//   3. note, Confirm: one server transaction. Reuses AdminDialog.ShowSubmitAsync (Confirm runs while it stays open; a refusal shows inside it;
//      it closes first, then answers). Windows rules: every control is new each time it opens; search results and the compare are new controls on
//      every search / choice (never reused with an old parent) and are drawn on the next UI turn. Nothing is logged (patient data).
using System.Globalization;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Admin;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Views.Doctor.Extra;

public static class MergePatientDialog
{
    /// <summary>Shows the dialog; returns the merge result (dialog already closed), or null when cancelled / refused before opening.</summary>
    public static async Task<PatientMerged?> ShowAsync(Page page, IServiceProvider sp, Guid currentPatientId, string currentLabel)
    {
        var svc = sp.GetRequiredService<IPatientMergeService>();
        var gate = sp.GetService<IPatientSyncGate>();
        var patients = sp.GetRequiredService<IAdminPatientService>();
        try
        {
            // online check first (a self-merge preview answers 400 when online, "needs an internet connection" when offline) so no sync runs offline
            var probe = await svc.PreviewAsync(currentPatientId, currentPatientId);
            if (!probe.Succeeded && AdminOnlineOnly.IsNeedsInternet(probe.Error)) { ClinicToast.Show(probe.Error!, ToastKind.Error); return null; }
            if (gate is not null && await gate.SyncFirstAsync(currentPatientId) is { } waiting) { ClinicToast.Show(waiting, ToastKind.Error); return null; }
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("MergePatientDialog.Open", ex);
            ClinicToast.Show("This patient could not be checked. Nothing was changed; try again.", ToastKind.Error);
            return null;
        }

        MergeToExistingPreview? preview = null;
        var intro = new Border
        {
            Padding = new Thickness(12, 10), BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"), Stroke = ClinicTheme.Color("OutlineVariant"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label { Text = $"You are looking at {currentLabel}.", Style = ClinicTheme.Style("BodyMd") },
                    new Label { Text = "If this person was already registered before, pick that existing record. It is kept; everything recorded here moves to it.", Style = ClinicTheme.Style("MutedSm") },
                },
            },
        };

        // search the existing record
        var search = new Entry { Placeholder = "Name, patient code or phone", MaxLength = 100 };
        AutomationProperties.SetName(search, "Search existing patient");
        var searchButton = new Button { Text = "Search", Style = ClinicTheme.Style("SecondaryButton") };
        var results = new VerticalStackLayout { Spacing = 4 };
        var chosen = new Label { Style = ClinicTheme.Style("LabelMd"), IsVisible = false };
        var compareHost = new ContentView();
        var warning = new Label { Style = ClinicTheme.Style("FormAlertErrorText") };
        var warningBox = new Border { Style = ClinicTheme.Style("FormAlertError"), Content = warning, IsVisible = false };
        var summary = new Label { Style = ClinicTheme.Style("BodyMd"), AutomationId = "mp-summary" };
        var summaryBox = new Border { Padding = new Thickness(12, 10), BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"), Stroke = ClinicTheme.Color("OutlineVariant"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Content = summary, IsVisible = false };
        var searchSeq = 0;
        var chooseSeq = 0;

        async Task RunSearchAsync()
        {
            var q = (search.Text ?? "").Trim();
            var mine = ++searchSeq;
            UiTurn.Next(() => { results.Children.Clear(); results.Children.Add(new Label { Text = q.Length < 2 ? "Type at least 2 letters." : "Searching…", Style = ClinicTheme.Style("MutedSm") }); });
            if (q.Length < 2) return;
            AdminPage<AdminPatientListItem> found;
            try { found = await patients.SearchAsync(q, 1, 8); }
            catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("MergePatientDialog.Search", ex); found = new AdminPage<AdminPatientListItem>([], 0, 1, 8); }
            if (mine != searchSeq) return; // an older search answered late
            UiTurn.Next(() =>
            {
                results.Children.Clear(); // new controls every search
                var list = found.Items.Where(p => p.PatientId != currentPatientId).ToList();
                if (list.Count == 0) { results.Children.Add(new Label { Text = "No other patient matches. Try another spelling.", Style = ClinicTheme.Style("MutedSm") }); return; }
                foreach (var p in list)
                {
                    var b = new Button { Text = $"{p.FirstName} {p.LastName} · {p.PatientCode} · born {p.DateOfBirth.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}", Style = ClinicTheme.Style("SecondaryButton"), HorizontalOptions = LayoutOptions.Fill };
                    var pid = p.PatientId;
                    b.Clicked += async (_, _) => await ChooseAsync(pid);
                    results.Children.Add(b);
                }
            });
        }

        async Task ChooseAsync(Guid keepId)
        {
            var mine = ++chooseSeq;
            var got = await svc.PreviewAsync(keepId, currentPatientId);
            if (mine != chooseSeq) return;
            UiTurn.Next(() =>
            {
                preview = got.Succeeded ? got.Value : null;
                compareHost.Content = null; // the old grid leaves its parent before a new one is made
                if (preview is null) { chosen.Text = got.Error ?? "That patient could not be loaded."; chosen.IsVisible = true; warningBox.IsVisible = false; summaryBox.IsVisible = false; return; }
                results.Children.Clear();
                chosen.Text = $"Keep: {preview.Keep.Name} ({preview.Keep.PatientCode}) — this record merges into it.";
                chosen.IsVisible = true;
                compareHost.Content = CompareGrid(preview);
                var w = preview.BothHaveAccounts ? "Both patients have a patient-portal account, so they cannot be merged here." : PatientMergeRules.ActiveVisitWarning(preview);
                warning.Text = w ?? ""; warningBox.IsVisible = w is not null;
                summary.Text = PatientMergeRules.Summary(preview); summaryBox.IsVisible = true;
            });
        }

        searchButton.Clicked += async (_, _) => await searchButton.RunBusyAsync(RunSearchAsync);
        search.Completed += async (_, _) => await RunSearchAsync();
        var searchRow = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        searchRow.Add(search, 0, 0); searchRow.Add(searchButton, 1, 0);

        var note = new Editor { AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 70, MaxLength = PatientMergeRules.NoteMax, Placeholder = "e.g. Same person; front desk registered him again on Nov 25" };
        AutomationProperties.SetName(note, "Note (kept in the audit log)");

        var form = new VerticalStackLayout
        {
            Spacing = 12,
            Children = { intro, ClinicUi.Field("Existing patient", searchRow, required: true), results, chosen, warningBox, compareHost, summaryBox, ClinicUi.Field("Note (kept in the audit log)", note, required: true) },
        };
        var body = new ScrollView { Content = form, MaximumHeightRequest = 640 };

        PatientMerged? merged = null;
        var ok = await AdminDialog.ShowSubmitAsync(page, "Merge to existing patient", body, "Confirm merge", async () =>
        {
            if (PatientMergeRules.Problem(currentPatientId, preview?.Keep.PatientId, note.Text, preview?.BothHaveAccounts == true) is { } bad) return bad;
            if (gate is not null && await gate.PendingProblemAsync(currentPatientId) is { } waiting) return waiting; // typed elsewhere meanwhile
            var outcome = await svc.MergeAsync(preview!.Keep.PatientId, currentPatientId, note.Text ?? "");
            if (outcome.Succeeded) { merged = outcome.Merged; return null; }
            return outcome.Error ?? "The merge could not be completed, so nothing was changed. Try again.";
        }, busyText: "Merging…");
        return ok ? merged : null;
    }

    /// <summary>The side-by-side compare. A new Grid (and new Labels) every call.</summary>
    static View CompareGrid(MergeToExistingPreview p)
    {
        var g = new Grid
        {
            ColumnSpacing = 12, RowSpacing = 4,
            ColumnDefinitions = { new ColumnDefinition(new GridLength(130)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
        };
        void Row(int r, View a, View b, View c) { g.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); g.Add(a, 0, r); g.Add(b, 1, r); g.Add(c, 2, r); }
        Row(0, new Label { Text = "FIELD", Style = ClinicTheme.Style("TableHeader") },
            new Label { Text = "EXISTING RECORD — KEEP", Style = ClinicTheme.Style("TableHeader"), TextColor = ClinicTheme.Color("Primary") },
            new Label { Text = "THIS RECORD — MERGES IN", Style = ClinicTheme.Style("TableHeader") });
        var r = 1;
        foreach (var (label, keep, merge, differs) in PatientMergeRules.CompareRows(p.Keep, p.Merge))
        {
            var fills = differs && keep == "—";
            Row(r++, new Label { Text = (differs ? "● " : "") + label, Style = ClinicTheme.Style("MutedSm") },
                new Label { Text = keep, Style = ClinicTheme.Style("BodyMd"), LineBreakMode = LineBreakMode.WordWrap },
                new Label { Text = merge + (fills ? "  (fills the blank)" : ""), Style = ClinicTheme.Style("BodyMd"), LineBreakMode = LineBreakMode.WordWrap });
        }
        return new Border
        {
            Content = g, Padding = new Thickness(12, 10), Stroke = ClinicTheme.Color("OutlineVariant"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
        };
    }

    /// <summary>The header / chart button (web: merge icon). A new Button every call.</summary>
    public static void Decorate(Button b, string text = "Merge to existing patient")
    {
        b.ImageSource = ClinicIcons.FontImage("merge", 14, ClinicTheme.Color("Primary"));
        AutomationProperties.SetName(b, text);
    }

    public static Button ChartButton(Func<Task> onClick)
    {
        var b = new Button { Text = "Merge…", Style = ClinicTheme.Style("SecondaryButton") };
        Decorate(b, "Merge…");
        b.Clicked += async (_, _) => await BusyButton.RunBusyAsync(b, onClick);
        return b;
    }

    public static string DoneSentence(PatientMerged m, string keepName, string goneName) => PatientMergeRules.DoneText(m, keepName, goneName);

    public static string MergedToast(PatientMerged m) => $"Merged into {m.KeptPatientCode} at {DateTime.Now.ToString("h:mm:ss tt", CultureInfo.InvariantCulture)}.";
}
//JJQR - END
