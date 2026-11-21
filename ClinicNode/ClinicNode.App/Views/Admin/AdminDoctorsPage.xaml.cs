//JJQR - AI (21 Nov 2026) - web /admin/doctors layout (see AdminDoctorsPage.xaml)
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Shared.Controls;

namespace ClinicNode.App.Views.Admin;

public partial class AdminDoctorsPage : ContentPage
{
    readonly AdminDoctorsViewModel _vm;
    readonly VerticalStackLayout _body = new() { Spacing = 16 };

    public AdminDoctorsPage(AdminDoctorsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        var add = ClinicUi.Button("+ Add doctor", () => { _vm.OpenNewCommand.Execute(null); return Task.CompletedTask; });
        Root.Children.Add(ClinicUi.PageHeader("Doctors", "Who patients can book, their fees and the days they see patients.", add));
        Root.Children.Add(_body);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
        Render();
    }

    void Render()
    {
        _body.Children.Clear();
        if (_vm.HasError && _vm.Items.Count == 0)
        {
            _body.Children.Add(ClinicTheme.Text("Doctors unavailable", "HeadlineSm"));
            _body.Children.Add(ClinicTheme.Text("The doctor list could not be loaded. Try again.", "MutedBody"));
            _body.Children.Add(ClinicUi.Button("Try again", async () => { await _vm.LoadAsync(); Render(); }, ButtonKind.Secondary));
            return;
        }
        var columns = new List<TableColumn>
        {
            new("Name", new GridLength(1.4, GridUnitType.Star), o => ClinicTheme.Text(((DoctorListRow)o).Name, "LabelLg")),
            new("Specialty", GridLength.Star, o => ClinicUi.Cell(((DoctorListRow)o).Specialization)),
            new("Fee", new GridLength(110), o => new Label { Text = ((DoctorListRow)o).Fee, Style = ClinicTheme.Style("TableCell"), HorizontalTextAlignment = TextAlignment.End }),
            new("Working days", new GridLength(1.4, GridUnitType.Star), o =>
            {
                var days = ((DoctorListRow)o).WorkingDays;
                var none = string.IsNullOrWhiteSpace(days) || days == "—" || days == "No working days";
                return ClinicTheme.Text(none ? "No working days" : days, none ? "MutedBody" : "TableCell");
            }),
            new("Status", GridLength.Auto, o => AdminPill.Make(((DoctorListRow)o).StatusKey)),
            new("Actions", GridLength.Auto, o => Actions((DoctorListRow)o)),
        };
        _body.Children.Add(ClinicUi.Table(columns, _vm.Items.Cast<object>(), "No doctors yet. Add one to open bookings."));
    }

    View Actions(DoctorListRow row)
    {
        var stack = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End };
        stack.Children.Add(ClinicUi.Button("Edit", () => { _vm.OpenEditCommand.Execute(row.DoctorId); return Task.CompletedTask; }, ButtonKind.Secondary));
        if (row.StatusKey != "Inactive")
        {
            var deactivate = ClinicUi.Button("Deactivate", async () => await ConfirmDeactivateAsync(row), ButtonKind.Ghost);
            deactivate.TextColor = ClinicTheme.Color("StatusDanger");
            stack.Children.Add(deactivate);
        }
        return stack;
    }

    async Task ConfirmDeactivateAsync(DoctorListRow row)
    {
        var name = string.IsNullOrWhiteSpace(row.Name) || row.Name == "—" ? "this doctor" : row.Name;
        //JJQR - AI (21 Nov 2026) - web Modal: Deactivate runs while the modal stays open ("Keep active" disabled while saving); a failure shows
        // the FormAlert inside the same open modal (was: the modal closed and reopened with the error).
        var body = new VerticalStackLayout { Spacing = 16 };
        body.Children.Add(ClinicTheme.Text("They will no longer appear in patient-facing booking. Existing bookings and records are kept.", "MutedBody"));
        if (!await AdminDialog.ShowSubmitAsync(this, $"Deactivate {name}?", body, "Deactivate", () => _vm.DeactivateWebAsync(row.DoctorId), cancel: "Keep active", danger: true)) return;
        //JJQR - END
        Render();
    }
}
//JJQR - END
