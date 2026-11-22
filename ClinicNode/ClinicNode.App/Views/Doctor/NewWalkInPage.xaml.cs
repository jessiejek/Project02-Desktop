// JJQR - AI (Nov 21, 2026) - doctor (and admin) walk-in: reuse WalkInWizardView, LoadAsync in OnAppearing.
//JJQR - AI (21 Nov 2026) - same host layout as StaffWalkInPage / AdminWalkInPage (web WalkInWizard): the step-1 Continue bar is docked at the
// bottom outside the scroll, and the page header is hidden on the done card. Header text, OpenBooking and service routing unchanged.
//JJQR - END
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Staff;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.Auth;

namespace ClinicNode.App.Views.Doctor;

public partial class NewWalkInPage : ContentPage
{
    private readonly WalkInWizardView _wizard;

    public NewWalkInPage(IServiceProvider services)
    {
        InitializeComponent();
        ClinicNode.App.Views.Shared.TopBarOnPushed.Attach(this, services); //JJQR - AI (Nov 22, 2026) //JJQR - END
        BackgroundColor = ClinicTheme.Color("Background");
        _wizard = new WalkInWizardView(services);
        if (services.GetService<IAuthService>()?.Current?.IsDoctor == true)
            _wizard.OpenBooking = id => Shell.Current.GoToAsync($"consult?bookingId={id}");
        //JJQR - AI (21 Nov 2026) - docked Continue bar + header hidden on done
        var header = StaffUi.Header("New walk-in consult", "Look up a synced patient, or register a new one, then start the consultation on this computer (no internet needed).");
        _wizard.StepRendered += step => header.IsVisible = step != WalkInStep.Done;
        _wizard.DockContinueBar = true;
        var page = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        page.Add(StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children = { header, _wizard },
        }), 0, 0);
        page.Add(_wizard.ContinueBar, 0, 1);
        Content = page;
        //JJQR - END
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _wizard.LoadAsync();
    }
}
// JJQR - END
