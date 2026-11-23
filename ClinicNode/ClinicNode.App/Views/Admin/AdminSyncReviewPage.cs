//JJQR - AI (Nov 23, 2026) - web parity: admin "Sync review" (web /admin/sync-review + MergePatientsModal). Same wording, filters and rules: an item opens a dialog; a possible duplicate offers "Compare and merge…" / "Not a duplicate",
// every other kind "Mark resolved" / "Dismiss". A note of at least 5 characters is required. Online only: offline it shows a short "needs internet" note.
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.Views.Admin;

public sealed class AdminSyncReviewPage : ContentPage
{
    readonly AdminSyncReviewViewModel _vm;
    readonly VerticalStackLayout _root = new() { Padding = 24, Spacing = 24 };

    public AdminSyncReviewPage(AdminSyncReviewViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "Sync review";
        BackgroundColor = ClinicTheme.Color("Background");
        Content = new ScrollView { Content = _root };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Render();
        var hide = ClinicNode.App.Controls.Skeleton.CoverPage(this);
        try { await _vm.LoadAsync(); } finally { hide(); }
        Render();
    }

    async Task ReloadAsync() { await _vm.LoadAsync(); Render(); }

    void Render()
    {
        _root.Children.Clear();
        var refresh = ClinicUi.Button("Refresh", ReloadAsync, ButtonKind.Secondary);
        refresh.IsEnabled = _vm.Loaded;
        _root.Children.Add(ClinicUi.PageHeader("Sync review",
            $"Problems from the clinic computers that need a person to look at them. {_vm.OpenTotal} open.", refresh));

        if (!_vm.Loaded) { _root.Children.Add(new ActivityIndicator { IsRunning = true }); return; }
        if (_vm.LoadFailed)
        {
            _root.Children.Add(ClinicTheme.Text("Sync review unavailable", "HeadlineSm"));
            _root.Children.Add(ClinicTheme.Text("The list could not be loaded. Try again.", "MutedBody"));
            _root.Children.Add(ClinicUi.Button("Try again", ReloadAsync, ButtonKind.Secondary));
            return;
        }
        if (!_vm.FromServer)
        {
            _root.Children.Add(new Border { Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(12, 8), Content = ClinicTheme.Text(LocalAdminSyncReviewService.NeedsInternet, "PillLabelInfo") });
            return;
        }

        // filters (web: Status + Type)
        var statusPicker = new Picker { ItemsSource = AdminSyncReviewViewModel.StatusOptions.ToList(), SelectedItem = char.ToUpperInvariant(_vm.Status[0]) + _vm.Status[1..] };
        var kinds = new List<string> { "All types" }; kinds.AddRange(SyncReviewRules.Kinds.Select(SyncReviewRules.KindLabel));
        var kindPicker = new Picker { ItemsSource = kinds, SelectedIndex = _vm.Kind.Length == 0 ? 0 : Array.IndexOf(SyncReviewRules.Kinds, _vm.Kind) + 1 };
        statusPicker.SelectedIndexChanged += async (_, _) => { if (statusPicker.SelectedItem is string s && !string.Equals(s, _vm.Status, StringComparison.OrdinalIgnoreCase)) { _vm.Status = s.ToLowerInvariant(); _vm.Page = 1; await ReloadAsync(); } };
        kindPicker.SelectedIndexChanged += async (_, _) => { var k = kindPicker.SelectedIndex <= 0 ? "" : SyncReviewRules.Kinds[kindPicker.SelectedIndex - 1]; if (k != _vm.Kind) { _vm.Kind = k; _vm.Page = 1; await ReloadAsync(); } };
        var filters = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 16 };
        filters.Add(ClinicUi.Field("Status", statusPicker), 0, 0);
        filters.Add(ClinicUi.Field("Type", kindPicker), 1, 0);
        _root.Children.Add(new Border { Style = ClinicTheme.Style("Card"), Content = filters });

        var empty = _vm.Status == "open" && _vm.Kind.Length == 0
            ? "Nothing needs your attention. Everything the computers sent has been accepted."
            : "No items match these filters.";
        _root.Children.Add(ClinicUi.Table(
            [
                new TableColumn("Type", new GridLength(230), o => AdminPill.Make("", SyncReviewRules.KindTone(((SyncReviewItem)o).Kind), SyncReviewRules.KindLabel(((SyncReviewItem)o).Kind))),
                new TableColumn("What happened", GridLength.Star, o => ClinicUi.Cell(((SyncReviewItem)o).Title)),
                new TableColumn("When", new GridLength(170), o => ClinicUi.Cell(ClinicDateTime(((SyncReviewItem)o).CreatedAt))),
                new TableColumn("Status", new GridLength(150), o => ClinicUi.Cell(StatusText((SyncReviewItem)o))),
            ],
            _vm.Items.Cast<object>(), empty, o => _ = ShowItemAsync((SyncReviewItem)o)));

        // paging (web: Previous / Page x of y / Next)
        var prev = ClinicUi.Button("Previous", async () => { _vm.Page--; await ReloadAsync(); }, ButtonKind.Secondary); prev.IsEnabled = _vm.Page > 1;
        var next = ClinicUi.Button("Next", async () => { _vm.Page++; await ReloadAsync(); }, ButtonKind.Secondary); next.IsEnabled = _vm.Page < _vm.PageCount;
        _root.Children.Add(new HorizontalStackLayout
        {
            Spacing = 12, VerticalOptions = LayoutOptions.Center,
            Children = { ClinicTheme.Text(_vm.Total == 0 ? "No items" : $"{_vm.Total} item{(_vm.Total == 1 ? "" : "s")}", "MutedBody"), prev, ClinicTheme.Text($"Page {_vm.Page} of {_vm.PageCount}", "MutedBody"), next },
        });
    }

    static string StatusText(SyncReviewItem i) => i.Status == "open" ? "Open" : $"Resolved ({i.Resolution ?? "—"})";
    static string ClinicDateTime(DateTimeOffset v) => v.ToLocalTime().ToString("MMM d, yyyy h:mm tt");

    // ── one item ──
    async Task ShowItemAsync(SyncReviewItem item)
    {
        var body = new VerticalStackLayout { Spacing = 12 };
        body.Children.Add(ClinicTheme.Text(item.Title, "LabelLg"));
        body.Children.Add(ClinicTheme.Text(SyncReviewRules.KindHelp(item.Kind), "MutedBody"));
        foreach (var (k, v) in item.Details)
            body.Children.Add(new HorizontalStackLayout { Spacing = 12, Children = { new Label { Text = k.Replace('_', ' '), Style = ClinicTheme.Style("FieldHint"), WidthRequest = 180 }, ClinicTheme.Text(v, "BodyMd") } });
        body.Children.Add(ClinicTheme.Text($"Received {ClinicDateTime(item.CreatedAt)}{(item.EntityId is null ? "" : $" · {item.Aggregate ?? "record"} {item.EntityId}")}", "FieldHint"));

        if (item.Status != "open")
        {
            body.Children.Add(ClinicUi.Alert($"Resolved ({item.Resolution}) {(item.ResolvedAt is { } at ? ClinicDateTime(at) : "")}{(string.IsNullOrWhiteSpace(item.ResolutionNote) ? "" : " — " + item.ResolutionNote)}", true));
            await AdminDialog.ShowAsync(this, SyncReviewRules.KindLabel(item.Kind), body, "Close", cancel: null);
            return;
        }

        var note = new Editor { HeightRequest = 72, AutoSize = EditorAutoSizeOption.Disabled, Placeholder = "What you checked or did", MaxLength = SyncReviewRules.NoteMax };
        body.Children.Add(ClinicUi.Field("Note (required to dismiss or resolve)", note, $"At least {SyncReviewRules.NoteMin} characters."));

        var dup = SyncReviewRules.IsDuplicate(item.Kind) && item.PatientId is not null && item.OtherPatientId is not null;
        var options = dup ? new[] { "Compare and merge…", "Not a duplicate" } : new[] { "Mark resolved", "Dismiss" };
        if (!await AdminDialog.ShowAsync(this, SyncReviewRules.KindLabel(item.Kind), body, "Choose an action…", cancel: "Close")) return;
        var choice = await DisplayActionSheetAsync(SyncReviewRules.KindLabel(item.Kind), "Cancel", null, options);
        if (choice is null || choice == "Cancel") return;

        if (choice == "Compare and merge…") { await ShowMergeAsync(item); return; }
        var resolution = choice == "Mark resolved" ? "fixed" : "dismissed";
        var error = await _vm.ResolveAsync(item, resolution, note.Text ?? "");
        if (error is not null) { await DisplayAlertAsync("Sync review", error, "OK"); return; }
        await ReloadAsync();
    }

    // ── merge two patients (web MergePatientsModal) ──
    async Task ShowMergeAsync(SyncReviewItem item)
    {
        // the web keeps the record already on file by default: the item's patient is the one that arrived, the other patient is already on file
        var (preview, loadError) = await _vm.PreviewAsync(item.OtherPatientId!.Value, item.PatientId!.Value);
        if (preview is null) { await DisplayAlertAsync("Merge two patients", loadError ?? "These patients could not be loaded.", "OK"); return; }
        var left = preview.Keep;   // already on file
        var right = preview.Merge; // arrived from a computer
        var keepFirst = false;     // false: keep the one already on file

        var summary = new Label { Style = ClinicTheme.Style("MutedBody") };
        var keepLeft = new RadioButton { GroupName = "keep", IsChecked = true, Content = $"KEEP — already on file ({SyncReviewRules.FullName(left)})" };
        var keepRight = new RadioButton { GroupName = "keep", Content = $"KEEP — arrived from a computer ({SyncReviewRules.FullName(right)})" };
        var note = new Editor { HeightRequest = 72, AutoSize = EditorAutoSizeOption.Disabled, Placeholder = "Why these are the same person", MaxLength = SyncReviewRules.NoteMax };

        void Refresh()
        {
            var keep = keepFirst ? right : left; var gone = keepFirst ? left : right;
            summary.Text = SyncReviewRules.MergeSummary(SyncReviewRules.FullName(keep), keep.PatientCode, SyncReviewRules.FullName(gone), gone.PatientCode, gone.Counts);
        }
        keepLeft.CheckedChanged += (_, e) => { if (e.Value) { keepFirst = false; Refresh(); } };
        keepRight.CheckedChanged += (_, e) => { if (e.Value) { keepFirst = true; Refresh(); } };
        Refresh();

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 6, ColumnDefinitions = { new ColumnDefinition(new GridLength(150)), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        int row = 0;
        void Add(string a, string b, string c, bool header = false, bool differs = false)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(new Label { Text = a, Style = ClinicTheme.Style("FieldHint") }, 0, row);
            grid.Add(new Label { Text = b, Style = ClinicTheme.Style(header ? "LabelLg" : "BodyMd"), BackgroundColor = differs ? ClinicTheme.Color("StatusWarningSurface") : Colors.Transparent }, 1, row);
            grid.Add(new Label { Text = c, Style = ClinicTheme.Style(header ? "LabelLg" : "BodyMd"), BackgroundColor = differs ? ClinicTheme.Color("StatusWarningSurface") : Colors.Transparent }, 2, row);
            row++;
        }
        Add("", "Already on file", "Arrived from a computer", header: true);
        foreach (var r in SyncReviewRules.CompareRows(left, right)) Add(r.Label, r.A, r.B, differs: r.Differs);
        Add("Records held", SyncReviewRules.CountsText(left.Counts), SyncReviewRules.CountsText(right.Counts));

        var body = new VerticalStackLayout { Spacing = 12 };
        body.Children.Add(ClinicTheme.Text("Choose the record to keep. Everything recorded for the other one moves to it. Rows that differ are highlighted.", "MutedBody"));
        if (preview.BothHaveAccounts) body.Children.Add(ClinicUi.Alert("Both patients have a patient-portal account, so they cannot be merged here.", false));
        body.Children.Add(keepLeft);
        body.Children.Add(keepRight);
        body.Children.Add(grid);
        body.Children.Add(new Border { Style = ClinicTheme.Style("FormAlertError"), Content = summary });
        body.Children.Add(ClinicUi.Field("Note (required, kept in the audit log)", note));

        var merged = await AdminDialog.ShowSubmitAsync(this, "Merge two patients", new ScrollView { Content = body, MaximumHeightRequest = 560 }, "Merge patients",
            () =>
            {
                var keep = keepFirst ? right : left; var gone = keepFirst ? left : right;
                return _vm.MergeAsync(keep.PatientId, gone.PatientId, note.Text ?? "");
            },
            cancel: "Cancel", busyText: "Merging…",
            onOpen: d =>
            {
                void Sync() => d.ConfirmButton.IsEnabled = !preview.BothHaveAccounts && SyncReviewRules.NoteProblem(note.Text) is null;
                note.TextChanged += (_, _) => Sync();
                Sync();
            });
        if (merged) await ReloadAsync();
    }
}
//JJQR - END
