//JJQR - AI (Nov 21, 2026) - D-TEMPLATES: consultation templates (web doctor/templates/page.tsx), READ-ONLY on the node: soap_template, soap_phrase, doctor_diagnosis_template, prescription_template, medical_certificate_template are cloud-owned (SYNC-API-CONTRACT §6), so CRUD stays on the web portal.
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra;

public sealed class DoctorTemplatesPage : StaffLivePage
{
    private readonly VerticalStackLayout _tab = StaffUi.Column(8);
    private DoctorTemplates? _t;
    private string _selected = "soap";
    private bool _linked = true;

    public DoctorTemplatesPage(IServiceProvider sp) : base(sp, "Templates", live: false)
    {
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Header("Consultation templates", "SOAP phrases, diagnoses, prescriptions, and certificates you reuse during consultations."),
                StaffUi.Card(ClinicTheme.Text(TemplateTabs.ReadOnlyNotice, "MutedBody")),
                LoadError,
                DoctorExtraUi.Tabs(TemplateTabs.All, () => _selected, id => { _selected = id; Render(); }),
                StaffUi.Card(_tab),
            },
        });
    }

    protected override async Task LoadAsync()
    {
        var doctorId = DoctorExtraUi.DoctorId(Sp);
        _linked = doctorId is not null;
        _t = doctorId is null ? null : await Svc<IDoctorTemplatesService>().GetAsync(doctorId.Value);
        Render();
    }

    private void Item(string title, string? tag, params string?[] lines)
    {
        var col = StaffUi.Column(4);
        col.Children.Add(ClinicTheme.Text(title, "LabelLg"));
        if (tag is not null) col.Children.Add(ClinicTheme.Text(tag, "MutedSm"));
        foreach (var l in lines) if (!string.IsNullOrWhiteSpace(l)) col.Children.Add(ClinicTheme.Text(l, "BodyMd"));
        _tab.Children.Add(StaffUi.Row(col));
    }

    private void Render()
    {
        _tab.Children.Clear();
        if (!_linked) { _tab.Children.Add(DoctorExtraUi.NotLinked()); return; }
        if (_t is not { } t) return;
        static string Kind(bool system) => system ? "Built-in" : "Yours";
        switch (_selected)
        {
            case "soap":
                if (t.Soap.Count == 0) _tab.Children.Add(ClinicTheme.Text("No SOAP templates yet.", "MutedBody"));
                foreach (var s in t.Soap) Item(s.Title, Kind(s.IsSystem), Pre("CC", s.ChiefComplaint), Pre("S", s.Subjective), Pre("O", s.Objective), Pre("A", s.Assessment), Pre("P", s.Plan));
                break;
            case "diagnoses":
                if (t.Diagnoses.Count == 0) _tab.Children.Add(ClinicTheme.Text("No diagnosis templates yet.", "MutedBody"));
                foreach (var d in t.Diagnoses) Item(d.Label, d.Icd10Code is null ? null : "ICD-10: " + d.Icd10Code, d.Body);
                break;
            case "rx":
                if (t.Rx.Count == 0) _tab.Children.Add(ClinicTheme.Text("No prescription templates yet.", "MutedBody"));
                foreach (var r in t.Rx) Item(r.Title, Kind(r.IsSystem), r.Lines.Select(l => $"{l.GenericName} {l.Dosage} · #{l.Quantity}").ToArray());
                break;
            case "favorites":
                if (t.Favorites.Count == 0) _tab.Children.Add(ClinicTheme.Text("No favorite medicines yet.", "MutedBody"));
                foreach (var f in t.Favorites) Item(f.GenericName, null, $"{f.Dosage} · #{f.Quantity}", f.Instruction);
                break;
            case "phrases":
                if (t.Phrases.Count == 0) _tab.Children.Add(ClinicTheme.Text("No SOAP phrases yet.", "MutedBody"));
                foreach (var p in t.Phrases) Item(p.Label, DoctorTemplatesService.FieldLabel(p.Field), p.Body);
                break;
            case "certs":
                if (t.Certificates.Count == 0) _tab.Children.Add(ClinicTheme.Text("No certificate templates yet.", "MutedBody"));
                foreach (var c in t.Certificates) Item(c.Title, Kind(c.IsSystem), Pre("Diagnosis", c.DiagnosisText), Pre("Recommendations", c.Recommendations), Pre("Purpose", c.PurposeException));
                break;
        }
    }

    private static string? Pre(string label, string? value) => string.IsNullOrWhiteSpace(value) ? null : $"{label}: {value}";
}
//JJQR - END
