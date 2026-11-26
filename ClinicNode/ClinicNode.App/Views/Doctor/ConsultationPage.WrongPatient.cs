//JJQR - AI (2026-11-26) - GAV-11: "Wrong patient?" right after "Edit details" in the consultation header (web ConsultPatientBanner onWrongPatient).
// What is typed is saved first (web: the draft is saved before the dialog opens). After the move, the server's copy of this visit (booking →
// new patient, consultation and its children) is written into this computer right away, the screen reloads as the right patient, and
// "Reprint slip" is offered (owner: no automatic reprint). Redraws happen on the next UI turn, after the pop-up has closed.
using ClinicNode.App.Controls;
using ClinicNode.App.Doctor;
using ClinicNode.App.Views.Doctor.Extra;
using ClinicNode.App.Views.Shared.Controls;

namespace ClinicNode.App.Views.Doctor;

public partial class ConsultationPage
{
    bool _movingVisit;

    void InitWrongPatientButton() => WrongPatientDialog.Decorate(WrongPatientButton);

    async void OnWrongPatient(object? sender, EventArgs e)
    {
        if (_workspace is null || _movingVisit || _mergingPatient || _busy) return; //JJQR - AI (2026-11-26) - GAV-11/12 review 1.6: a fast click on both header buttons never opens both flows //JJQR - END
        _movingVisit = true;
        var label = WrongPatientButton.Text;
        WrongPatientButton.IsEnabled = false;
        WrongPatientButton.Text = "Checking…";
        try
        {
            if (_mode != "view" && Dirty && !await SaveAsync(goNext: false, silent: true))
            {
                ClinicToast.Show("Save this consultation first (it could not be saved), then try again. Nothing was moved.", ToastKind.Error);
                return;
            }
            var bookingId = D.BookingId;
            var moved = await WrongPatientDialog.ShowAsync(this, _serviceProvider, bookingId, D.PatientId);
            if (moved is null) return;
            // this computer's copy follows the server now (the next pull would do the same; the slip prints from this copy)
            try { await ConsultLocalWriteThrough.EnsureConsultationAsync(_serviceProvider, bookingId); }
            catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Consultation.WrongPatient.WriteThrough", ex); }
            (_serviceProvider.GetService(typeof(ClinicNode.Core.Sync.ISyncBridge)) as ClinicNode.Core.Sync.ISyncBridge)?.RequestSync();
            var reloaded = await _consults.LoadByBookingAsync(bookingId);
            UiTurn.Next(() =>
            {
                _workspace = reloaded;
                InitFormState();
                RenderAll();
                ShowOverlay("Visit moved", [new Label { Text = $"This visit is now under {_workspace!.PatientName} ({moved.PatientCode}). The other patient's details and other visits did not change.", Style = ClinicTheme.Style("BodyMd") }],
                    ("Reprint slip", () => OnPrintSlip(null, EventArgs.Empty), "SecondaryButton"),
                    ("Done", HideOverlay, "PrimaryButton"));
                ClinicToast.Show(WrongPatientDialog.MovedToast(moved), ToastKind.Success);
            });
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Consultation.WrongPatient", ex);
            ClinicToast.Show("The visit could not be moved. Check the patient on this visit before trying again.", ToastKind.Error);
        }
        finally
        {
            _movingVisit = false;
            WrongPatientButton.Text = label;
            WrongPatientButton.IsEnabled = true;
        }
    }
}
//JJQR - END
