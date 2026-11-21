//JJQR - AI (Nov 21, 2026) - "Register a new patient" dialog (WalkInWizard quick register): node-issued MF- code; "Register and select".
//JJQR - AI (21 Nov 2026) - 1:1 with the web quick-register Modal: First name / Middle name (optional) / Last name, Date of birth, Sex (Select…),
// Contact number (optional), Address (optional), "Prepare portal account" (+ the "Not wired yet" note), the live warning
// (quickRegisterProblem: future birth date, lengths, phone), the register error, Cancel + "Register and select" (disabled until valid, "Registering…").
//JJQR - END
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class QuickRegisterPage : ContentPage
{
    private readonly TaskCompletionSource<StaffPatientRow?> _done = new();
    private bool _registering; //JJQR - AI (21 Nov 2026) //JJQR - END

    QuickRegisterPage(IStaffPatientsService svc)
    {
        Title = "Register a new patient";
        BackgroundColor = ClinicTheme.Color("Background");
        var first = new Entry();
        var middle = new Entry();
        var last = new Entry();
        var dob = new DatePicker { Date = null, MaximumDate = DateTime.Today, MinimumDate = new DateTime(1900, 1, 1) };
        var sex = new Picker { Title = "Select…", ItemsSource = new[] { "Male", "Female" } };
        var contact = new Entry { Keyboard = Keyboard.Telephone };
        var address = new Entry();
        //JJQR - AI (21 Nov 2026) - web FormAlerts: register error (danger), quickRegisterProblem (warning), portal note (info)
        var error = Alert("StatusDanger");
        var problem = Alert("StatusWarning");
        var portalNote = Alert("StatusInfo");
        ((Label)portalNote.Content!).Text = "Not wired yet — this registers as a guest record for now; portal invites need an email field this form doesn't collect.";
        var portal = new CheckBox { VerticalOptions = LayoutOptions.Start };
        portal.CheckedChanged += (_, e) => portalNote.IsVisible = e.Value;
        //JJQR - END

        QuickRegisterForm Form() => new(first.Text ?? "", middle.Text, last.Text ?? "",
            dob.Date is { } d ? DateOnly.FromDateTime(d) : null,
            sex.SelectedIndex switch { 0 => SexType.Male, 1 => SexType.Female, _ => null },
            contact.Text, address.Text);

        Button? register = null;
        var cancel = StaffUi.Btn("Cancel", async () => { if (_registering) return; _done.TrySetResult(null); await Navigation.PopModalAsync(); }, "SecondaryButton");
        //JJQR - AI (21 Nov 2026) - web quickRegisterProblem (same order) + canQuickRegister
        void Sync()
        {
            var f = Form();
            var p = StaffRules.NotFuture(f.DateOfBirth, ClinicClock.Today)
                ?? StaffRules.Length(f.FirstName, 100, "First name")
                ?? StaffRules.Length(f.MiddleName, 100, "Middle name")
                ?? StaffRules.Length(f.LastName, 100, "Last name")
                ?? StaffRules.Length(f.Address, 300, "Address")
                ?? StaffRules.Phone(f.ContactNumber);
            Show(problem, p);
            if (register is not null) register.IsEnabled = !_registering && svc.CanQuickRegister(f);
        }
        register = StaffUi.Btn("Register and select", async () =>
        {
            if (_registering || !svc.CanQuickRegister(Form())) return;
            Show(error, null);
            _registering = true;
            register!.Text = "Registering…";
            cancel.IsEnabled = false;
            try
            {
                var (result, row) = await svc.QuickRegisterAsync(Form());
                if (!result.Succeeded || row is null) { Show(error, string.IsNullOrWhiteSpace(result.Error) ? "Could not register this patient. Try again." : result.Error); return; }
                _done.TrySetResult(row);
                await Navigation.PopModalAsync();
            }
            catch (Exception) { Show(error, "Could not register this patient. Try again."); }
            finally
            {
                _registering = false;
                register!.Text = "Register and select";
                cancel.IsEnabled = true;
                Sync();
            }
        });
        foreach (var e in new[] { first, middle, last, contact, address }) e.TextChanged += (_, _) => Sync();
        dob.DateSelected += (_, _) => Sync();
        sex.SelectedIndexChanged += (_, _) => Sync();
        //JJQR - END

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24, Spacing = 12, MaximumWidthRequest = 560,
                Children =
                {
                    ClinicTheme.Text("Register a new patient", "HeadlineMd"),
                    error,
                    problem,
                    StaffUi.Field("First name", first),
                    StaffUi.Field("Middle name (optional)", middle),
                    StaffUi.Field("Last name", last),
                    StaffUi.Field("Date of birth", dob),
                    StaffUi.Field("Sex", sex),
                    StaffUi.Field("Contact number (optional)", contact),
                    StaffUi.Field("Address (optional)", address),
                    //JJQR - AI (21 Nov 2026)
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            portal,
                            new VerticalStackLayout
                            {
                                Spacing = 2,
                                Children = { ClinicTheme.Text("Prepare portal account", "BodyMd"), ClinicTheme.Text("Creates login access; the patient sets a password on first login.", "FieldHint") },
                            },
                        },
                    },
                    portalNote,
                    StaffUi.Wrap(cancel, register),
                    //JJQR - END
                }
            }
        };
        Sync();
    }

    //JJQR - AI (21 Nov 2026) - FormAlert tone box (hidden until it has text)
    static Border Alert(string tone) => new()
    {
        IsVisible = false,
        Padding = new Thickness(16, 12),
        BackgroundColor = ClinicTheme.Color(tone + "Surface"),
        Stroke = ClinicTheme.Color(tone + "Border"),
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
        Content = new Label { Style = ClinicTheme.Style("BodySm"), TextColor = ClinicTheme.Color(tone) },
    };

    static void Show(Border alert, string? text)
    {
        ((Label)alert.Content!).Text = text ?? "";
        alert.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    protected override bool OnBackButtonPressed()
    {
        if (_registering) return true;
        _done.TrySetResult(null);
        return base.OnBackButtonPressed();
    }
    //JJQR - END

    public static async Task<StaffPatientRow?> ShowAsync(Page from, IStaffPatientsService svc)
    {
        var p = new QuickRegisterPage(svc);
        await from.Navigation.PushModalAsync(p);
        return await p._done.Task;
    }
}
//JJQR - END
