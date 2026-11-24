// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain.Enums;
using ClinicNode.Core;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Print;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Visits;

namespace ClinicNode.Tests;

public class PrintDocumentTests
{
    [Fact]
    public void Page_sizes_match_the_web_forms()
    {
        Assert.Equal("148mm 210mm", PrintSpec.Prescription.CssSize);
        Assert.Equal("148mm 210mm", PrintSpec.MedicalCertificate.CssSize);
        Assert.Equal("148mm 210mm", PrintSpec.LabRequest.CssSize); //JJQR - AI (Nov 24, 2026) - the lab request is now the same A5 sheet as the other two forms //JJQR - END
    }

    [Fact]
    public async Task Queue_slip_and_clinical_forms_render_the_required_sizes()
    {
        await using var host = await TestHost.StartAsync();
        var doctor = await host.LoginDoctorAsync();
        var patients = host.Get<IPatientService>();
        var visits = host.Get<IVisitStarter>();
        var consults = host.Get<IConsultationService>();
        var print = host.Get<IPrintDocumentFactory>();

        var patient = await patients.CreateAsync(new NewPatientRequest("Printa", "Cruz", new DateOnly(1990, 1, 15), SexType.Female, Address: "Quezon City"));
        var bookingId = await visits.StartWalkInConsultAsync(patient.PatientId, doctor.DoctorId!.Value, "Senior");
        var workspace = await consults.LoadByBookingAsync(bookingId);
        var draft = workspace.Draft;
        draft.ChiefComplaint = "Fever";
        foreach (var v in draft.Vitals)
        {
            v.Value = v.FormKey == "blood_pressure" ? "110/70" : "80";
        }
        draft.Prescriptions.Add(new RxLine { GenericName = "Paracetamol", Dosage = "500 mg", Quantity = "10", Instruction = "1 tab every 6 hours" });
        draft.LabOrders.Add(new LabLine { TestName = "CBC", Reason = "Fever work-up" });
        draft.FollowUpMode = FollowUpMode.None;
        draft.CertificateExists = true;
        draft.McDiagnosis = "Viral illness";
        draft.McRecommendations = "Rest and fluids";
        draft.PfDecision = "Charge";
        draft.PfAmount = workspace.ComputedFee > 0 ? workspace.ComputedFee : 400m;
        var saved = await consults.SaveDraftAsync(draft);
        Assert.True(saved.Succeeded, saved.Error);

        var slip = await print.QueueSlipAsync(bookingId);
        Assert.Equal(PrintSpec.QueueSlip, slip.Page);
        Assert.Contains("80mm 100mm", slip.Html);
        Assert.Contains("Printa Cruz", slip.Html);
        Assert.Contains("S-", slip.Html);

        var rx = await print.PrescriptionAsync(bookingId);
        Assert.Equal("148mm 210mm", rx.Page.CssSize);
        Assert.Contains("148mm 210mm", rx.Html);
        Assert.Contains("Paracetamol", rx.Html);

        var lab = await print.LabRequestAsync(bookingId);
        Assert.Equal("148mm 210mm", lab.Page.CssSize); //JJQR - AI (Nov 24, 2026) - A5 like the prescription and the medical certificate //JJQR - END
        Assert.Contains("148mm 210mm", lab.Html);
        Assert.Contains("CBC", lab.Html);

        var cert = await print.MedicalCertificateAsync(bookingId);
        Assert.Contains("148mm 210mm", cert.Html);
        Assert.Contains("Viral illness", cert.Html);
    }
}
// JJQR - END
