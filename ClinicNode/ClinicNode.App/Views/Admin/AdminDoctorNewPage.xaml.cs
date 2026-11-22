//JJQR - AI (21 Nov 2026) - body built by AdminDoctorFormView (web DoctorForm)
using ClinicNode.App.ViewModels.Admin;

namespace ClinicNode.App.Views.Admin;

public partial class AdminDoctorNewPage : ContentPage
{
    readonly AdminDoctorNewViewModel _vm;
    public AdminDoctorNewPage(AdminDoctorNewViewModel vm)
    {
        InitializeComponent();
        ClinicNode.App.Views.Shared.TopBarOnPushed.Attach(this); //JJQR - AI (Nov 22, 2026) - top bar + back in one top row //JJQR - END
        BindingContext = _vm = vm;
        Content = AdminDoctorFormView.Build(vm);
    }
}
//JJQR - END
