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
        //JJQR - AI (Nov 21, 2026) - a real dialog: dimmed window, floating card, title bar + close, two-column form
        ControlTemplate = null;
        BackgroundColor = Colors.Transparent;
        Shell.SetPresentationMode(this, PresentationMode.ModalNotAnimated);
        //JJQR - END
        var first = new Entry();
        var middle = new Entry();
        var last = new Entry();
        var dob = new DatePicker { Date = null, MaximumDate = DateTime.Today, MinimumDate = new DateTime(1900, 1, 1) };
        var sexIndex = -1; //JJQR - AI (Nov 21, 2026) - two choices = two buttons, not a dropdown //JJQR - END
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

        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.3: ONE patient id for this registration dialog, so pressing Register again after a failure or a lost reply cannot create a second patient
        var registrationId = Guid.NewGuid();
        //JJQR - END
        //JJQR - AI (Nov 23, 2026) - Data Privacy Act (PH): the privacy notice must be shown to the patient before registering them. The tick is recorded in the audit log; the patient still consents in the portal.
        var notice = new CheckBox { VerticalOptions = LayoutOptions.Start };
        //JJQR - END
        QuickRegisterForm Form() => new(first.Text ?? "", middle.Text, last.Text ?? "",
            dob.Date is { } d ? DateOnly.FromDateTime(d) : null,
            sexIndex switch { 0 => SexType.Male, 1 => SexType.Female, _ => null },
            contact.Text, address.Text, registrationId, notice.IsChecked);

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
            if (register is not null) register.IsEnabled = !_registering && svc.CanQuickRegister(f) && notice.IsChecked; //JJQR - AI (Nov 23, 2026) - privacy notice must be shown first //JJQR - END
        }
        register = StaffUi.Btn("Register and select", async () =>
        {
            if (_registering || !svc.CanQuickRegister(Form()) || !notice.IsChecked) return;
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
            catch (Exception ex)
            {
                //JJQR - AI (Nov 23, 2026) - owner (offline test): show the real reason when the app has one (server unreachable, register this computer first) and keep a record of every failure
                ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("register-patient (screen)", ex);
                Show(error, ClinicNode.Core.Diagnostics.ClinicErrorLog.Friendly(ex, "Could not register this patient. Try again."));
                //JJQR - END
            }
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
        notice.CheckedChanged += (_, _) => Sync(); //JJQR - AI (Nov 23, 2026) //JJQR - END
        //JJQR - END

        //JJQR - AI (Nov 21, 2026) - dialog body: alerts on top, then a two-column form, then the portal option
        var sexHost = new ContentView();
        void RenderSex() => sexHost.Content = ClinicNode.App.Views.Doctor.Extra.DoctorExtraUi.Segmented(["Male", "Female"], sexIndex, i => { sexIndex = i; RenderSex(); Sync(); });
        RenderSex();
        var dobBox = ClinicPickers.DateBox(this, dob, DateTime.Today.AddYears(-30), fill: true);
        first.Placeholder = "e.g. Maria Teresa"; last.Placeholder = "e.g. Dela Cruz"; middle.Placeholder = "optional";
        contact.Placeholder = "e.g. 0917 123 4567"; address.Placeholder = "Street, barangay, city (optional)";
        var form = new Grid { ColumnSpacing = 16, RowSpacing = 14, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        form.Add(StaffUi.Field("First name *", first), 0, 0);
        form.Add(StaffUi.Field("Last name *", last), 1, 0);
        form.Add(StaffUi.Field("Middle name", middle), 0, 1);
        form.Add(StaffUi.Field("Date of birth *", dobBox), 1, 1);
        form.Add(StaffUi.Field("Sex *", sexHost), 0, 2);
        form.Add(StaffUi.Field("Contact number", contact), 1, 2);
        var addr = StaffUi.Field("Address", address);
        form.Add(addr, 0, 3);
        Grid.SetColumnSpan(addr, 2);
        var body = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                error, problem, form,
                ClinicPickers.CheckRow(portal, "Prepare portal account", "Creates login access; the patient sets a password on first login."), portalNote,
                //JJQR - AI (Nov 23, 2026) - Data Privacy Act (PH) notice, same wording as the web registration
                new Border
                {
                    Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(14, 12),
                    Content = new VerticalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            ClinicTheme.Text("Privacy notice (Data Privacy Act)", "LabelLg"),
                            ClinicTheme.Text("Before registering, show the patient the clinic's privacy notice: what personal and health information is collected, why, who can see it, and their rights to access, correct and object. The patient gives their own consent in the patient portal.", "MutedBody"),
                            ClinicPickers.CheckRow(notice, "I have shown the patient the privacy notice *", "Required. This is recorded in the audit log."),
                        },
                    },
                },
                //JJQR - END
            },
        };
        var footer = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { cancel, register } };
        Content = ClinicModal.Build("Register a new patient", body, footer, async () => { if (_registering) return; _done.TrySetResult(null); await Navigation.PopModalAsync(); }, maxWidth: 780, maxBodyHeight: 640);
        //JJQR - END
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
