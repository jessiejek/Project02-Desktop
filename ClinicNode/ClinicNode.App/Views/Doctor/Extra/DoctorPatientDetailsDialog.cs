//JJQR - AI (2026-11-26) - GAV-10: owner: the doctor can edit ALL patient details, also while consulting. One dialog, opened from "Edit details" beside the
// name in the consultation header and on the patient chart's left card (same as the web's EditPatientDetailsDialog). Reuses the admin "Edit patient
// details" pop-up (AdminDialog.ShowSubmitAsync: Save runs while it stays open, "Saving…", a refusal shows inside it; it closes first, then answers).
// Saves through IAdminPatientService.UpdateDetailsAsync: online = PUT /api/patients/{id}; offline = this computer + sync (existing outbox).
// Every control is created new each time the dialog opens (never reused with an old parent).
using System.Globalization;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Admin;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Admin;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Views.Doctor.Extra;

public static class DoctorPatientDetailsDialog
{
    /// <summary>Shows the dialog; returns the saved details (dialog already closed), or null when cancelled / not loaded.</summary>
    public static async Task<PatientDetails?> ShowAsync(Page page, IServiceProvider sp, Guid patientId)
    {
        var svc = sp.GetRequiredService<IAdminPatientService>();
        PatientDetails current;
        try
        {
            var got = await svc.GetDetailsAsync(patientId);
            if (!got.Succeeded || got.Value is null) { ClinicToast.Show(got.Error ?? "The patient's details could not be loaded. Nothing has changed; try again.", ToastKind.Error); return null; }
            current = got.Value;
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("DoctorPatientDetailsDialog.Load", ex);
            ClinicToast.Show("The patient's details could not be loaded. Nothing has changed; try again.", ToastKind.Error);
            return null;
        }

        var today = ClinicApp.Domain.ClinicClock.Today;
        Entry Box(string? text, Keyboard? kb = null, int max = 100) => new() { Text = text ?? "", Keyboard = kb ?? Keyboard.Default, MaxLength = max };
        var first = Box(current.FirstName);
        var middle = Box(current.MiddleName);
        var last = Box(current.LastName);
        var sex = new Picker { ItemsSource = new List<string> { "Male", "Female" }, SelectedItem = current.Sex == SexType.Female ? "Female" : "Male" };
        var dob = new DatePicker { Date = current.DateOfBirth.ToDateTime(TimeOnly.MinValue), MaximumDate = today.ToDateTime(TimeOnly.MinValue), MinimumDate = new DateTime(1900, 1, 1) };
        var age = new Label { Style = ClinicTheme.Style("BodyMd"), VerticalOptions = LayoutOptions.Center, Margin = new Thickness(0, 10, 0, 0) };
        void ShowAge() => age.Text = dob.Date is { } d ? $"{PatientDetailsRules.AgeOn(DateOnly.FromDateTime(d), today)} years" : "—";
        ShowAge();
        dob.DateSelected += (_, _) => ShowAge();
        Picker Choice(string? value, string[] options)
        {
            var items = new List<string> { "Not set" };
            if (!string.IsNullOrWhiteSpace(value) && !options.Contains(value)) items.Add(value!); // keep a value typed elsewhere (e.g. registration)
            items.AddRange(options);
            return new Picker { ItemsSource = items, SelectedItem = string.IsNullOrWhiteSpace(value) ? "Not set" : value };
        }
        static string? Picked(Picker p) => p.SelectedItem as string is { } s && s != "Not set" ? s : null;
        var civil = Choice(current.CivilStatus, PatientDetailsRules.CivilStatusOptions);
        var blood = Choice(current.BloodType, PatientDetailsRules.BloodTypeOptions);
        var contact = Box(current.ContactNumber, Keyboard.Telephone, 20);
        var address = Box(current.Address, max: 300);
        var city = Box(current.City);
        var zip = Box(current.ZipCode, max: 20);
        var philhealth = Box(current.PhilhealthNumber, max: 30);
        var hmo = Box(current.HmoProvider);
        var hmoCard = Box(current.HmoCardNumber, max: 50);
        var ecName = Box(current.EmergencyContactName, max: 150);
        var ecNumber = Box(current.EmergencyContactNumber, Keyboard.Telephone, 20);
        var ecRel = Box(current.EmergencyContactRelationship);

        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
        var row = 0;
        void Section(string title)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = new Label { Text = title.ToUpperInvariant(), Style = ClinicTheme.Style("TableHeader"), Margin = new Thickness(0, row == 0 ? 0 : 6, 0, 0) };
            grid.Add(l, 0, row); Grid.SetColumnSpan(l, 2); row++;
        }
        void Pair(View left, View? right = null)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            grid.Add(left, 0, row); if (right is not null) grid.Add(right, 1, row); row++;
        }
        Section("Name and birthday");
        Pair(ClinicUi.Field("First name", first, required: true), ClinicUi.Field("Middle name", middle));
        Pair(ClinicUi.Field("Last name", last, required: true), ClinicUi.Field("Sex", ClinicPickers.PickerBox(null, sex)));
        Pair(ClinicUi.Field("Birthday", ClinicPickers.DateBox(null, dob, null, true), required: true), ClinicUi.Field("Age", age));
        Pair(ClinicUi.Field("Civil status", ClinicPickers.PickerBox(null, civil)), ClinicUi.Field("Blood type", ClinicPickers.PickerBox(null, blood)));
        Section("Contact and address");
        Pair(ClinicUi.Field("Contact number", contact), ClinicUi.Field("Address", address));
        Pair(ClinicUi.Field("City", city), ClinicUi.Field("Zip code", zip));
        Section("IDs and HMO");
        Pair(ClinicUi.Field("PhilHealth number", philhealth), ClinicUi.Field("HMO provider", hmo));
        Pair(ClinicUi.Field("HMO card number", hmoCard));
        Section("Emergency contact");
        Pair(ClinicUi.Field("Name", ecName), ClinicUi.Field("Contact number", ecNumber));
        Pair(ClinicUi.Field("Relationship", ecRel));
        var body = new ScrollView { Content = grid, MaximumHeightRequest = 560 };

        PatientDetails? saved = null;
        var ok = await AdminDialog.ShowSubmitAsync(page, "Edit patient details", body, "Save changes", async () =>
        {
            if (dob.Date is null) return "Birthday is required.";
            var edit = new PatientDetails(
                first.Text ?? "", middle.Text, last.Text ?? "", DateOnly.FromDateTime(dob.Date.Value),
                sex.SelectedItem as string == "Female" ? SexType.Female : SexType.Male,
                Picked(civil), contact.Text, address.Text, city.Text, zip.Text, Picked(blood), philhealth.Text, hmo.Text, hmoCard.Text,
                ecName.Text, ecNumber.Text, ecRel.Text, current.PatientCode);
            if (PatientDetailsRules.Problem(edit, today) is { } bad) return bad;
            var result = await svc.UpdateDetailsAsync(patientId, edit);
            if (!result.Succeeded || result.Value is null) return result.Error ?? "Could not save the patient's details. Your changes are still here; try again.";
            saved = result.Value;
            return null;
        }, busyText: "Saving…");
        return ok ? saved : null;
    }

    /// <summary>The "Edit details" button (pencil) used on both screens. A new Button every call.</summary>
    public static Button EditButton(Func<Task> onClick)
    {
        var b = new Button { Text = "Edit details", Style = ClinicTheme.Style("SecondaryButton") };
        b.ImageSource = ClinicIcons.FontImage("edit", 14, ClinicTheme.Color("Primary")); // web <Icon name="edit"> (pencil)
        b.Clicked += async (_, _) => await BusyButton.RunBusyAsync(b, onClick);
        AutomationProperties.SetName(b, "Edit details");
        return b;
    }

    public static string SavedToast() => $"Patient details saved at {DateTime.Now.ToString("h:mm:ss tt", CultureInfo.InvariantCulture)}.";
}
//JJQR - END
