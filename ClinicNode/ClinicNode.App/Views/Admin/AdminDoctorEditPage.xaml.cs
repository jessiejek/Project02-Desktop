//JJQR - AI (21 Nov 2026) - body built by AdminDoctorFormView (web DoctorForm)
using ClinicNode.App.ViewModels.Admin;

namespace ClinicNode.App.Views.Admin;

public partial class AdminDoctorEditPage : ContentPage
{
    readonly AdminDoctorEditViewModel _vm;
    public AdminDoctorEditPage(AdminDoctorEditViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        Content = ClinicNode.App.Controls.Skeleton.Overlay(AdminDoctorFormView.Build(vm), nameof(AdminDoctorEditViewModel.IsFirstLoad)); //JJQR - AI (Nov 22, 2026) - skeleton while the doctor loads //JJQR - END
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.LoadAsync();
    }
}
//JJQR - END
