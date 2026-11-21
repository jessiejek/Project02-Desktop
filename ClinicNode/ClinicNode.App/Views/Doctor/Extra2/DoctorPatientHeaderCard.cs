//JJQR - AI (21 Nov 2026) - the doctor patient header card, 1:1 with the web (doctor/patients/[id]/page.tsx): round initials avatar (primary at 10%), name + patient code
// on one row, "{sex} · {age} yrs · Born {yyyy-MM-dd}" underneath, and a phone chip (outlined, tap = call) only when the patient has a number. No clinical data.
// The print card (GET /api/patients/{id}/card -> PatientCardDocument) is a desktop-only printout: the web has none.
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Doctor.Extra; //JJQR - AI (21 Nov 2026) DoctorExtraUi //JJQR - END
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra2;

public static class DoctorPatientHeaderCard
{
    public static View Build(ChartPatient p, params View[] extraActions)
    {
        var avatar = new Border
        {
            WidthRequest = 56, HeightRequest = 56, StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 28 },
            BackgroundColor = ClinicTheme.Color("Primary").WithAlpha(0.1f),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = DoctorPatientCardRules.Initials(p.FullName), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                FontAttributes = FontAttributes.Bold, TextColor = ClinicTheme.Color("Primary"),
            },
        };
        var nameRow = StaffUi.Wrap(
            DoctorExtraUi.Spaced(ClinicTheme.Text(p.FullName, "HeadlineLg"), 12, 0),
            new Label { Text = p.PatientCode, FontFamily = "monospace", Style = ClinicTheme.Style("MutedSm"), VerticalOptions = LayoutOptions.End });
        var text = new VerticalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.Fill };
        text.Children.Add(nameRow);
        text.Children.Add(ClinicTheme.Text(DoctorPatientCardRules.Subtitle(p.Sex.ToString(), p.Age, p.DateOfBirth), "MutedBody"));

        var grid = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        grid.Add(avatar, 0);
        grid.Add(text, 1);
        //JJQR - AI (Nov 21, 2026) - actions (phone chip + print card) sit together on the right of the header instead of wrapping under the name
        var actions = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End };
        if (!string.IsNullOrWhiteSpace(p.ContactNumber))
        {
            var number = p.ContactNumber!;
            var chip = new Border
            {
                Stroke = ClinicTheme.Color("OutlineVariant"), StrokeThickness = 1, Padding = new Thickness(12, 8), VerticalOptions = LayoutOptions.Center,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Content = ClinicTheme.Text("📞 " + number, "MutedSm"),
            };
            chip.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => { try { await Launcher.Default.OpenAsync("tel:" + number); } catch { /* no dialer on this computer: nothing to do */ } }),
            });
            actions.Children.Add(chip);
        }
        foreach (var a in extraActions) actions.Children.Add(a);
        grid.Add(actions, 2);
        var col = new VerticalStackLayout { Spacing = 8 };
        col.Children.Add(grid);
        //JJQR - END
        return StaffUi.Card(col);
    }
}
//JJQR - END
