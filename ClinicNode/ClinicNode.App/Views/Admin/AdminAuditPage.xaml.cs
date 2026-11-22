//JJQR - AI (21 Nov 2026) - web /admin/audit-logs layout (see AdminAuditPage.xaml)
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;

namespace ClinicNode.App.Views.Admin;

public partial class AdminAuditPage : ContentPage
{
    readonly AdminAuditViewModel _vm;
    readonly Picker _type = new();
    readonly Entry _search = new() { Placeholder = "Action or details" };
    readonly VerticalStackLayout _body = new() { Spacing = 16 };
    CancellationTokenSource? _debounce;

    public AdminAuditPage(AdminAuditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _type.ItemsSource = vm.EntityOptions.ToList();
        _type.SelectedItem = vm.SelectedEntity;
        _search.Text = vm.Query;

        Root.Children.Add(ClinicUi.PageHeader("Audit log", "Who changed what, newest first. Times are shown in clinic time (Asia/Manila)."));
        var filters = new Grid
        {
            ColumnSpacing = 16,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(2, GridUnitType.Star)) },
        };
        filters.Add(ClinicUi.Field("Record type", ClinicPickers.PickerBox(null, _type)), 0, 0);
        filters.Add(ClinicUi.Field("Search", _search), 1, 0);
        Root.Children.Add(new Border { Style = ClinicTheme.Style("Card"), Content = filters });
        Root.Children.Add(_body);

        _type.SelectedIndexChanged += async (_, _) => await ReloadAsync(0);
        _search.TextChanged += async (_, _) => await ReloadAsync(250);
    }

    async Task ReloadAsync(int delayMs)
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        try { await Task.Delay(delayMs, cts.Token); } catch (TaskCanceledException) { return; }
        await LoadAndRenderAsync(() => _vm.ApplyFiltersAsync(_type.SelectedItem as string, _search.Text));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAndRenderAsync(_vm.LoadAsync);
    }

    //JJQR - AI (21 Nov 2026) - web: "Loading entries…" and Previous / Next disabled while a page loads (was only re-rendered afterwards)
    async Task LoadAndRenderAsync(Func<Task> load)
    {
        var task = load();
        Render();
        await task;
        Render();
    }
    //JJQR - END

    void Render()
    {
        _body.Children.Clear();
        //JJQR - AI (21 Nov 2026) - web: ErrorState replaces table + pager whenever the load failed
        if (_vm.LoadFailed)
        {
            _body.Children.Add(ClinicTheme.Text("Audit log unavailable", "HeadlineSm"));
            _body.Children.Add(ClinicTheme.Text("Entries could not be loaded. Try again.", "MutedBody"));
            var retry = ClinicUi.Button("Try again", () => LoadAndRenderAsync(_vm.LoadAsync), ButtonKind.Secondary);
            retry.IsEnabled = !_vm.IsLoading;
            _body.Children.Add(retry);
            return;
        }
        //JJQR - END
        var columns = new List<TableColumn>
        {
            new("When", new GridLength(170), o => ClinicUi.Cell(((AuditRow)o).When)),
            new("Action", GridLength.Star, o => ClinicTheme.Text(((AuditRow)o).Action, "LabelLg")),
            new("Record", new GridLength(1.3, GridUnitType.Star), o =>
            {
                var r = (AuditRow)o;
                return new VerticalStackLayout
                {
                    Children =
                    {
                        ClinicUi.Cell(r.EntityType),
                        new Label { Text = r.EntityId, FontFamily = "Courier New", FontSize = 12, TextColor = ClinicTheme.Color("OnSurfaceVariant"), LineBreakMode = LineBreakMode.CharacterWrap },
                    },
                };
            }),
            new("By", GridLength.Star, o => new Label { Text = ((AuditRow)o).Who, Style = ClinicTheme.Style("TableCell"), LineBreakMode = LineBreakMode.CharacterWrap }),
            new("Details", new GridLength(1.6, GridUnitType.Star), o => new Label { Text = ((AuditRow)o).Details, Style = ClinicTheme.Style("TableCell"), LineBreakMode = LineBreakMode.WordWrap }),
        };
        //JJQR - AI (21 Nov 2026) - web DataTable shows loading rows, not the empty message, while the first page loads
        if (_vm.IsLoading && _vm.Items.Count == 0)
            _body.Children.Add(new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Start });
        else
            _body.Children.Add(ClinicUi.Table(columns, _vm.Items.Cast<object>(), _vm.EmptyMessage));

        var prev = ClinicUi.Button("‹ Previous", () => LoadAndRenderAsync(() => _vm.GoToPageAsync(_vm.CurrentPage - 1)), ButtonKind.Secondary);
        prev.IsEnabled = _vm.CurrentPage > 1 && !_vm.IsLoading;
        var next = ClinicUi.Button("Next ›", () => LoadAndRenderAsync(() => _vm.GoToPageAsync(_vm.CurrentPage + 1)), ButtonKind.Secondary);
        next.IsEnabled = _vm.CurrentPage < _vm.PageCount && !_vm.IsLoading;
        //JJQR - END
        var nav = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var range = ClinicTheme.Text(_vm.IsLoading ? "Loading entries…" : _vm.RangeLabel, "LabelMd"); //JJQR - AI (21 Nov 2026) //JJQR - END
        range.VerticalOptions = LayoutOptions.Center;
        nav.Add(range, 0, 0);
        var pageLabel = ClinicTheme.Text($"Page {_vm.CurrentPage} of {_vm.PageCount}", "LabelMd");
        pageLabel.VerticalOptions = LayoutOptions.Center;
        nav.Add(new HorizontalStackLayout { Spacing = 8, Children = { prev, pageLabel, next } }, 1, 0);
        _body.Children.Add(nav);
    }
}
//JJQR - END
