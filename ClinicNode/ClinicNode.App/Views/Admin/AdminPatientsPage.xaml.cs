//JJQR - AI (21 Nov 2026) - web /admin/patients layout (see AdminPatientsPage.xaml)
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;

namespace ClinicNode.App.Views.Admin;

public partial class AdminPatientsPage : ContentPage
{
    readonly AdminPatientsViewModel _vm;
    readonly Entry _search = new() { Placeholder = "Name, patient code, email or phone", MaximumWidthRequest = 480, HorizontalOptions = LayoutOptions.Start, WidthRequest = 480 };
    readonly VerticalStackLayout _body = new() { Spacing = 16 };
    CancellationTokenSource? _debounce;

    public AdminPatientsPage(AdminPatientsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        var add = ClinicUi.Button("+ Add patient", OpenAddAsync);
        Root.Children.Add(ClinicUi.PageHeader("Patients", "Everyone registered at the clinic, newest first. Open a patient to see their visits and account.", add));
        Root.Children.Add(new VerticalStackLayout { Spacing = 4, Children = { ClinicTheme.Text("Search patients", "FieldLabel"), _search } });
        Root.Children.Add(_body);
        // web: 250 ms debounce, back to page 1 on every change
        _search.TextChanged += async (_, e) =>
        {
            _debounce?.Cancel();
            var cts = _debounce = new CancellationTokenSource();
            try { await Task.Delay(250, cts.Token); } catch (TaskCanceledException) { return; }
            await LoadAndRenderAsync(() => _vm.SearchFromBoxAsync(e.NewTextValue ?? ""));
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAndRenderAsync(_vm.LoadAsync);
    }

    // web: "Loading patients…" in the range label and Previous / Next disabled while a page loads
    async Task LoadAndRenderAsync(Func<Task> load)
    {
        var task = load();
        Render();
        await task;
        Render();
    }

    void Render()
    {
        _body.Children.Clear();
        // web: ErrorState replaces the table and pager whenever the load failed (not only when no rows are left)
        if (_vm.LoadFailed)
        {
            _body.Children.Add(ClinicTheme.Text("Patients unavailable", "HeadlineSm"));
            _body.Children.Add(ClinicTheme.Text("The patient directory could not be loaded. Try again.", "MutedBody"));
            var retry = ClinicUi.Button("Try again", () => LoadAndRenderAsync(_vm.LoadAsync), ButtonKind.Secondary);
            retry.IsEnabled = !_vm.IsLoading;
            _body.Children.Add(retry);
            return;
        }
        var columns = new List<TableColumn>
        {
            new("Name", new GridLength(1.4, GridUnitType.Star), o => ClinicTheme.Text(((PatientRow)o).Name, "LabelLg")),
            new("Code", new GridLength(130), o => new Label { Text = ((PatientRow)o).Code, Style = ClinicTheme.Style("TableCell"), FontFamily = "Courier New", FontSize = 13 }),
            new("Contact", GridLength.Star, o => ClinicUi.Cell(((PatientRow)o).Contact)),
            new("Email", new GridLength(1.6, GridUnitType.Star), o => new Label { Text = ((PatientRow)o).Email, Style = ClinicTheme.Style("TableCell"), LineBreakMode = LineBreakMode.CharacterWrap }),
            new("Account", GridLength.Auto, o => AdminPill.Make(((PatientRow)o).AccountKey)),
        };
        // web DataTable shows loading rows (not the empty message) while the first page loads
        if (_vm.IsLoading && _vm.Items.Count == 0)
            _body.Children.Add(new ActivityIndicator { IsRunning = true, HorizontalOptions = LayoutOptions.Start });
        else
            _body.Children.Add(ClinicUi.Table(columns, _vm.Items.Cast<object>(), _vm.EmptyMessage,
                o => _vm.OpenPatientCommand.Execute(((PatientRow)o).PatientId)));

        var prev = ClinicUi.Button("‹ Previous", () => LoadAndRenderAsync(() => _vm.GoToPageAsync(_vm.CurrentPage - 1)), ButtonKind.Secondary);
        prev.IsEnabled = _vm.CurrentPage > 1 && !_vm.IsLoading;
        var next = ClinicUi.Button("Next ›", () => LoadAndRenderAsync(() => _vm.GoToPageAsync(_vm.CurrentPage + 1)), ButtonKind.Secondary);
        next.IsEnabled = _vm.CurrentPage < _vm.PageCount && !_vm.IsLoading;
        var nav = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var range = ClinicTheme.Text(_vm.RangeLabel, "LabelMd");
        range.VerticalOptions = LayoutOptions.Center;
        nav.Add(range, 0, 0);
        var pageLabel = ClinicTheme.Text($"Page {_vm.CurrentPage} of {_vm.PageCount}", "LabelMd");
        pageLabel.VerticalOptions = LayoutOptions.Center;
        nav.Add(new HorizontalStackLayout { Spacing = 8, Children = { prev, pageLabel, next } }, 1, 0);
        _body.Children.Add(nav);
    }

    // web Modal "Add a patient": "Add patient" stays disabled until the form is valid; it runs while the modal stays open ("Adding…",
    // Cancel disabled) and a failure shows the FormAlert inside the open modal with the typed details kept.
    async Task OpenAddAsync()
    {
        _vm.ResetNewPatient();
        var first = new Entry();
        var last = new Entry();
        var dob = new DatePicker { MaximumDate = DateTime.Today };
        var sex = new Picker { Title = "Select…", ItemsSource = new List<string> { "Male", "Female" } };
        var contact = new Entry { Keyboard = Keyboard.Telephone };
        var dobPicked = false;
        var warning = new VerticalStackLayout();
        AdminDialog? dialog = null;
        void Sync()
        {
            _vm.NewFirstName = first.Text ?? ""; _vm.NewLastName = last.Text ?? ""; _vm.NewContact = contact.Text ?? "";
            _vm.NewDobPicked = dobPicked ? dob.Date : null; _vm.NewSexPicked = sex.SelectedItem as string;
            warning.Children.Clear();
            if (_vm.NewPatientProblem is { } p)
                warning.Children.Add(new Border { Style = ClinicTheme.Style("PillWarning"), Padding = new Thickness(12, 8), Content = ClinicTheme.Text(p, "PillLabelWarning") });
            if (dialog is not null) dialog.ConfirmButton.IsEnabled = _vm.CanCreate;
        }
        first.TextChanged += (_, _) => Sync(); last.TextChanged += (_, _) => Sync(); contact.TextChanged += (_, _) => Sync();
        dob.DateSelected += (_, _) => { dobPicked = true; Sync(); }; sex.SelectedIndexChanged += (_, _) => Sync();

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Add(ClinicUi.Field("First name", first), 0, 0);
        grid.Add(ClinicUi.Field("Last name", last), 1, 0);
        grid.Add(ClinicUi.Field("Date of birth", dob), 0, 1);
        grid.Add(ClinicUi.Field("Sex", sex), 1, 1);
        var contactLabel = new Label
        {
            Style = ClinicTheme.Style("FieldLabel"),
            FormattedText = new FormattedString { Spans = { new Span { Text = "Contact number " }, new Span { Text = "(optional)", TextColor = ClinicTheme.Color("OnSurfaceVariant") } } },
        };
        var contactField = new VerticalStackLayout { Spacing = 4, Children = { contactLabel, contact } };
        grid.Add(contactField, 0, 2);
        Grid.SetColumnSpan(contactField, 2);

        var body = new VerticalStackLayout { Spacing = 16, Children = { warning, grid } };
        var added = await AdminDialog.ShowSubmitAsync(this, "Add a patient", body, "Add patient",
            async () => { Sync(); return await _vm.CreateWebAsync() ? null : (string.IsNullOrWhiteSpace(_vm.CreateError) ? "The patient could not be added. Check the details and try again." : _vm.CreateError); },
            busyText: "Adding…",
            onOpen: d => { dialog = d; Sync(); });
        if (!added) _vm.ResetNewPatient();
        Render();
    }
}
//JJQR - END
