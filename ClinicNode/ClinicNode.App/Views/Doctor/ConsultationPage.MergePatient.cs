//JJQR - AI (2026-11-26) - GAV-12: owner (t118u): while consulting the duplicate ("jese rubi", made again on Nov 25) the doctor remembers the patient
// ("jessie rubi", consulted Nov 23) and clicks "Merge to existing patient" (header, right after "Wrong patient?"). What is typed is saved first; the
// dialog syncs this patient first and merges on the server (the existing record is kept). This visit then belongs to the kept patient: the server's
// copy is written into this computer right away, a sync is asked for (the pull retires the old record here too), and the screen reloads as the kept
// patient. Redraws happen on the next UI turn, after the pop-up has closed.
using ClinicNode.App.Controls;
using ClinicNode.App.Doctor;
using ClinicNode.App.Views.Doctor.Extra;
using ClinicNode.App.Views.Shared.Controls;

namespace ClinicNode.App.Views.Doctor;

public partial class ConsultationPage
{
    bool _mergingPatient;

    void InitMergeExistingButton() => MergePatientDialog.Decorate(MergeExistingButton);

    async void OnMergeExisting(object? sender, EventArgs e)
    {
        if (_workspace is null || _mergingPatient || _movingVisit || _busy) return;
        _mergingPatient = true;
        var label = MergeExistingButton.Text;
        MergeExistingButton.IsEnabled = false;
        MergeExistingButton.Text = "Checking…";
        try
        {
            if (_mode != "view" && Dirty && !await SaveAsync(goNext: false, silent: true))
            {
                ClinicToast.Show("Save this consultation first (it could not be saved), then try again. Nothing was merged.", ToastKind.Error);
                return;
            }
            var bookingId = D.BookingId;
            var goneName = _workspace.PatientName;
            var merged = await MergePatientDialog.ShowAsync(this, _serviceProvider, D.PatientId, $"{goneName}");
            if (merged is null) return;
            try { await ConsultLocalWriteThrough.EnsureConsultationAsync(_serviceProvider, bookingId); }
            catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Consultation.MergePatient.WriteThrough", ex); }
            (_serviceProvider.GetService(typeof(ClinicNode.Core.Sync.ISyncBridge)) as ClinicNode.Core.Sync.ISyncBridge)?.RequestSync();
            var reloaded = await _consults.LoadByBookingAsync(bookingId);
            UiTurn.Next(() =>
            {
                _workspace = reloaded;
                InitFormState();
                RenderAll();
                ShowOverlay("Patients merged", [new Label { Text = MergePatientDialog.DoneSentence(merged, _workspace!.PatientName, goneName), Style = ClinicTheme.Style("BodyMd") }],
                    ("Open chart", () => _ = DoctorPatientChartPage.OpenAsync(this, merged.KeptPatientId), "SecondaryButton"),
                    ("Done", HideOverlay, "PrimaryButton"));
                ClinicToast.Show(MergePatientDialog.MergedToast(merged), ToastKind.Success);
            });
        }
        catch (Exception ex)
        {
            ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Consultation.MergePatient", ex);
            ClinicToast.Show("The patients could not be merged. Check both records before trying again.", ToastKind.Error);
        }
        finally
        {
            _mergingPatient = false;
            MergeExistingButton.Text = label;
            MergeExistingButton.IsEnabled = true;
        }
    }
}
//JJQR - END
