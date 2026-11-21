//JJQR - AI (21 Nov 2026) - remaining web consultation features, 1:1 with Project02-fe (no new endpoints):
//   • Renal cue (page.tsx renalCueText) above the Prescription pad and the Lab request section
//   • Drug-safety alerts (PrescriptionForm safetyAlerts / applySafetyBeforeAdd / allergyBlockReason + checkDrugSafety)
//   • "Refill last visit Rx" (PrescriptionForm handleRefillLastVisit, priorRxItems from the patient's newest other consultation)
//   • SOAP specialty filter chips (page.tsx soapSpecialtyFilter / filteredSoapTemplates; default from the doctor's specialization)
// Data: IConsultationLibraryService.GetPatientClinicalFlagsAsync / GetPriorVisitAsync / GetDoctorSpecializationAsync (online-first).
using ClinicNode.App.Controls;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Doctor;
using ClinicNode.Core.Practice;

namespace ClinicNode.App.Views.Doctor;

public partial class ConsultationPage
{
    private PatientClinicalFlags? _clinicalFlags;
    private IReadOnlyList<RxLine> _priorRxItems = [];
    private string _soapSpecialty = TemplateSpecialty.All;
    private string _safetyToastKey = "";

    async Task LoadSafetyDataAsync()
    {
        if (_workspace is null) return;
        try { _clinicalFlags = await _lib.GetPatientClinicalFlagsAsync(D.PatientId); } catch { _clinicalFlags = null; }
        try { _priorRxItems = (await _lib.GetPriorVisitAsync(D.PatientId, D.BookingId))?.RxItems ?? []; } catch { _priorRxItems = []; }
        try
        {
            if (await _lib.GetDoctorSpecializationAsync(D.DoctorId) is { Length: > 0 } spec) _soapSpecialty = TemplateSpecialty.DefaultFromDoctor(spec);
        }
        catch { /* keep "All" */ }
    }

    // ---------- renal cue ----------
    void RenderRenalCue()
    {
        if (RenalCue.Text(_clinicalFlags) is not { } text) return;
        SectionHost.Children.Add(new Border
        {
            Stroke = ClinicTheme.Color("OnSurfaceVariant"), StrokeThickness = 0.5, BackgroundColor = ClinicTheme.Color("SurfaceContainerLow"),
            Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 0, 8),
            Content = ClinicTheme.Text(text, "MutedSm"),
        });
    }

    // ---------- drug safety ----------
    IEnumerable<string> PatientAllergenNames() => _allergies.Where(a => !a.IsNkda).Select(a => a.Allergen);

    IReadOnlyList<DrugSafetyAlert> SafetyAlerts(string? extraGeneric = null) =>
        DrugSafety.Check(extraGeneric is null ? D.Prescriptions.Select(r => r.GenericName) : D.Prescriptions.Select(r => r.GenericName).Append(extraGeneric),
            pregnant: _clinicalFlags?.IsPregnant == true || _isPregnant);

    /// <summary>web allergy check + applySafetyBeforeAdd: false (and an error toast) when the medicine must not be added; a warning toast otherwise.</summary>
    bool RxSafeToAdd(string genericName)
    {
        if (RxAllergy.BlockReason(genericName, PatientAllergenNames()) is { } allergen)
        {
            ShowToast($"Allergy conflict: {genericName} ↔ {allergen}", null, ++_noticeSeq);
            return false;
        }
        var alerts = SafetyAlerts(genericName);
        if (alerts.FirstOrDefault(a => a.Level == "block") is { } block) { ShowToast(block.Message, null, ++_noticeSeq); return false; }
        if (alerts.FirstOrDefault(a => a.Level == "warn") is { } warn) ShowToast(warn.Message, null, ++_noticeSeq);
        return true;
    }

    void RenderSafetyAlerts()
    {
        var alerts = SafetyAlerts();
        // web: toast the first warning whenever the set of warnings changes
        var warns = alerts.Where(a => a.Level == "warn").Select(a => a.Message).ToList();
        var key = string.Join("|", warns);
        if (key.Length > 0 && key != _safetyToastKey) { _safetyToastKey = key; ShowToast(warns[0], null, ++_noticeSeq); }
        if (alerts.Count == 0) return;
        var list = new VerticalStackLayout { Spacing = 2 };
        foreach (var a in alerts)
        {
            var l = ClinicTheme.Text(a.Message, "LabelSm");
            l.TextColor = ClinicTheme.Color("StatusWarning");
            list.Children.Add(l);
        }
        SectionHost.Children.Add(new Border
        {
            Stroke = ClinicTheme.Color("StatusWarning"), StrokeThickness = 1, BackgroundColor = ClinicTheme.Color("StatusWarningSurface"),
            Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 0, 8), Content = list,
        });
    }

    // ---------- Refill last visit Rx ----------
    void RenderRefillLastVisit()
    {
        if (ReadOnly || _priorRxItems.Count == 0) return;
        SectionHost.Children.Add(Btn("Refill last visit Rx", RefillLastVisit));
    }

    void RefillLastVisit()
    {
        var added = 0;
        foreach (var item in _priorRxItems)
        {
            if (!RxSafeToAdd(item.GenericName)) continue;
            if (RxIsDuplicate(item.GenericName)) continue;
            var copy = Clone(item);
            copy.Id = null;
            D.Prescriptions.Add(copy);
            added++;
        }
        if (added == 0) return;
        D.NoMedsConfirmed = false;
        RenderSection(); Changed();
    }

    // ---------- Rx template "Use template" (web rx-template-apply.ts planTemplateApply + describeTemplateApply) ----------
    void ApplyRxTemplateSafely(string title, IReadOnlyList<RxLine> templateLines)
    {
        var names = D.Prescriptions.Select(r => r.GenericName).ToList();
        var toAdd = new List<RxLine>();
        var skipped = new List<(string Name, string Reason, string Message)>();
        var allergens = PatientAllergenNames().ToList();
        var pregnant = _clinicalFlags?.IsPregnant == true || _isPregnant;
        foreach (var item in templateLines)
        {
            var key = (item.GenericName ?? "").Trim().ToLowerInvariant();
            if (RxAllergy.BlockReason(item.GenericName, allergens) is { } allergen)
            { skipped.Add((item.GenericName!, "allergy", $"Allergy conflict: {item.GenericName} ↔ {allergen}")); continue; }
            var alerts = DrugSafety.Check(names.Append(item.GenericName), pregnant);
            if (alerts.FirstOrDefault(a => a.Level == "block") is { } block) { skipped.Add((item.GenericName!, "safety", block.Message)); continue; }
            if (names.Any(n => (n ?? "").Trim().ToLowerInvariant() == key)) { skipped.Add((item.GenericName!, "duplicate", $"{item.GenericName} is already on the list")); continue; }
            var copy = Clone(item);
            copy.Id = null;
            if (string.IsNullOrWhiteSpace(copy.Route)) copy.Route = "PO";
            toAdd.Add(copy);
            names.Add(item.GenericName!);
        }
        foreach (var l in toAdd) D.Prescriptions.Add(l);
        if (toAdd.Count > 0) D.NoMedsConfirmed = false;

        static string Med(int c) => $"{c} medicine{(c == 1 ? "" : "s")}";
        string message;
        if (templateLines.Count == 0) message = $"\"{title}\" has no medicines yet. Edit the template to add some.";
        else if (toAdd.Count > 0)
            message = $"Added {Med(toAdd.Count)} from {title}" + (skipped.Count > 0
                ? $"; skipped {skipped.Count} ({string.Join("; ", skipped.Select(x => $"{x.Name}: {(x.Reason == "duplicate" ? "already on the list" : x.Message)}"))})" : "");
        else if (skipped.All(x => x.Reason == "duplicate")) message = $"Nothing added: all {Med(skipped.Count)} from {title} are already on the list.";
        else message = $"Nothing added from {title}. {string.Join("; ", skipped.Select(x => x.Message))}";
        ShowToast(message, null, ++_noticeSeq);
    }

    // ---------- SOAP specialty filter ----------
    IReadOnlyList<SoapTemplateItem> FilteredSoapTemplates() =>
        _soapTemplates.Where(t => TemplateSpecialty.MatchesSoap(t.Title, _soapSpecialty)).ToList();

    FlexLayout BuildSoapSpecialtyChips() //JJQR - AI (Nov 21, 2026) - returns the row so the caller can group it //JJQR - END
    {
        var row = new FlexLayout { Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (id, label) in TemplateSpecialty.ChipLabels)
        {
            var chip = id;
            row.Children.Add(Pill(label, _soapSpecialty == chip, () => { _soapSpecialty = chip; RenderSection(); }));
        }
        return row;
    }
}
//JJQR - END
