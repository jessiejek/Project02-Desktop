//JJQR - AI (2026-11-26) - GAV-10: "Edit details" beside the patient's name in the consultation header (web ConsultPatientBanner onEditDetails).
// Opens the patient details pop-up; on save only the header (name · sex · age) is redrawn, on the next UI turn after the pop-up has closed.
// The consultation form (notes, vitals, Rx, labs…) is not touched, so nothing typed is lost.
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Doctor.Extra;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.Views.Doctor;

public partial class ConsultationPage
{
    bool _editingPatient;

    void InitEditPatientButton() =>
        EditPatientButton.ImageSource = ClinicNode.App.Controls.ClinicIcons.FontImage("edit", 14, ClinicTheme.Color("Primary")); // web <Icon name="edit"> (pencil)

    async void OnEditPatientDetails(object? sender, EventArgs e)
    {
        if (_workspace is null || _editingPatient) return;
        _editingPatient = true;
        try
        {
            var saved = await DoctorPatientDetailsDialog.ShowAsync(this, _serviceProvider, D.PatientId);
            if (saved is null || _workspace is null) return;
            _workspace = _workspace with
            {
                PatientName = PatientDetailsRules.DisplayName(saved),
                PatientDateOfBirth = saved.DateOfBirth,
                PatientSex = saved.Sex.ToString(),
            };
            ClinicNode.App.Controls.UiTurn.Next(() => { if (_workspace is not null) RenderHeader(); });
            ClinicToast.Show(DoctorPatientDetailsDialog.SavedToast(), ToastKind.Success);
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Consultation.EditPatient", ex);
            ClinicToast.Show("The patient's details could not be saved. Try again.", ToastKind.Error);
        }
        finally { _editingPatient = false; }
    }
}
//JJQR - END
