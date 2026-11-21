//JJQR - AI (21 Nov 2026) - body built by AdminDoctorFormView (web DoctorForm)
using ClinicNode.App.ViewModels.Admin;

namespace ClinicNode.App.Views.Admin;

public partial class AdminDoctorNewPage : ContentPage
{
    readonly AdminDoctorNewViewModel _vm;
    public AdminDoctorNewPage(AdminDoctorNewViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        Content = AdminDoctorFormView.Build(vm);
    }
}
//JJQR - END
