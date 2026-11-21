//JJQR - AI (Nov 21, 2026) - /staff/walk-in "Walk-in check-in": hosts the shared WalkInWizardView (replaces the doctor's NewWalkInPage on this route).
//JJQR - AI (21 Nov 2026) - web parity: the step-1 Continue bar is docked at the bottom (outside the scroll), and the page header is hidden on the
// done card (web renders only the card there).
//JJQR - END
using ClinicNode.App.Controls;
using ClinicNode.App.ViewModels.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffWalkInPage : ContentPage
{
    private readonly WalkInWizardView _wizard;

    public StaffWalkInPage(IServiceProvider sp)
    {
        Title = "Walk-In";
        BackgroundColor = ClinicTheme.Color("Background");
        _wizard = new WalkInWizardView(sp) { OpenBooking = id => StaffUi.PushAsync<StaffBookingDetailPage>(this, id) };
        //JJQR - AI (21 Nov 2026)
        var header = StaffUi.Header("Walk-in check-in", "Find the patient (or register them), confirm the visit, and hand them a queue number.");
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
//JJQR - END
