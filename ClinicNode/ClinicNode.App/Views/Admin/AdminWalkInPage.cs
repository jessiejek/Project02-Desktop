// JJQR - AI (Nov 21, 2026) - A-WALK hosts allen3's shared WalkInWizardView (check-in + queue number + print slip, no consult).
//JJQR - AI (21 Nov 2026) - allen0 I-1: wiring note resolved; wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs). //JJQR - END
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;

namespace ClinicNode.App.Views.Admin;

public sealed class AdminWalkInPage : ContentPage
{
    readonly WalkInWizardView _wizard;

    public AdminWalkInPage(IServiceProvider sp)
    {
        Title = "Walk-In";
        BackgroundColor = ClinicTheme.Color("Background");
        _wizard = new WalkInWizardView(sp)
        {
            OpenBooking = async id =>
            {
                try { await Shell.Current.GoToAsync($"admin-booking-detail?id={id}"); }
                catch (Exception ex)
                {
                    await DisplayAlertAsync("Navigation", ex.Message + "\n\nAsk allen to register this route in AppShell / MauiProgram.", "OK");
                }
            }
        };
        //JJQR - AI (21 Nov 2026) - allen3 walk-in 1:1 pass: Continue bar docked at the bottom (web fixed bar) and the header hidden on the done card.
        var header = new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                new Label { Text = "Walk-in check-in", Style = ClinicTheme.Style("HeadlineMd") },
                new Label
                {
                    Text = "Find the patient (or register them), confirm the visit, and hand them a queue number.",
                    Style = ClinicTheme.Style("MutedBody")
                },
            }
        };
        _wizard.StepRendered += step => header.IsVisible = step != ClinicNode.App.ViewModels.Staff.WalkInStep.Done;
        _wizard.DockContinueBar = true;
        var page = new Grid { RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) } };
        page.Add(new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 16,
                Children = { header, _wizard }
            }
        }, 0, 0);
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
