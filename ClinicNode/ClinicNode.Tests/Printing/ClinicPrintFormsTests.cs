//JJQR - AI (Nov 21, 2026) - content and paper-size tests for the four clinic prints (Rx A5, lab 4.3x6.4in, med cert A5, queue slip).
using ClinicNode.Core.Printing;
using static ClinicNode.Tests.Printing.PrintingTestData;

namespace ClinicNode.Tests.Printing;

public class ClinicPrintFormsTests
{
    static readonly TimeProvider Nov21 = new FixedClock(new DateTimeOffset(2026, 11, 21, 2, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Prescription_is_A5_with_web_page_rule_letterhead_and_patient_fields()
    {
        var doc = ClinicPrintForms.Prescription(Clinic, Doctor, Patient, [Paracetamol], "2026-11-21", clock: Nov21);
        Assert.Equal("Prescription", doc.Title);
        Assert.Same(PrintPaper.A5, doc.Page);
        Assert.Equal(PaperKind.Sheet, PrintPaper.KindOf(doc.Page));
        Assert.Contains("@page { size: 148mm 210mm; margin: 0; }", doc.Html);
        Assert.Contains("ALLYN GRACE T. ESPAÑA-GAVINO, MD", doc.Html);
        Assert.Contains("Juan Dela Cruz", doc.Html);
        Assert.Contains("<span class=\"u age\">36</span>", doc.Html);
        Assert.Contains("November 21, 2026", doc.Html);
        Assert.Contains("JANE DOE, MD", doc.Html);
        Assert.Contains("S2 No:", doc.Html);
        Assert.Contains("data:image/png;base64,", doc.Html);
        Assert.DoesNotContain("src=\"/forms/", doc.Html);
        Assert.DoesNotContain("form form-rx form-tall", doc.Html);
    }

    [Fact]
    public void Prescription_row_marks_saved_slots_meal_and_duration()
    {
        var html = ClinicPrintForms.Prescription(Clinic, Doctor, Patient, [Paracetamol], clock: Nov21).Html;
        Assert.Contains("<span class=\"slot pill\">BREAKFAST</span>", html);
        Assert.Contains("<span class=\"slot faint\">LUNCH</span>", html);
        Assert.Contains("<span class=\"slot pill\">DINNER</span>", html);
        Assert.Contains("<span class=\"slot pill\">after</span>", html);
        Assert.Contains("<span class=\"u dur-u\">5</span> Days", html);
        Assert.Contains("Paracetamol 500 mg", html);
    }

    [Fact]
    public void More_than_five_medicines_switches_to_the_tall_layout()
    {
        var html = ClinicPrintForms.Prescription(Clinic, Doctor, Patient, Enumerable.Repeat(Paracetamol, 6).ToList(), clock: Nov21).Html;
        Assert.Contains("form form-rx form-tall", html);
    }

    //JJQR - AI (Nov 24, 2026) - owner: the lab request is the SAME A5 sheet as the prescription and the medical certificate, lists only the clinic's tests (TSH, FT3, FT4, HbA1c added), and the license number is left blank
    [Fact]
    public void Lab_request_is_A5_like_the_other_forms_ticks_the_clinics_tests_and_lists_the_rest_under_Others()
    {
        var doc = ClinicPrintForms.LabRequest(Clinic, Doctor, Patient, ["CBC", "Urine test", "Serum creatinine", "HbA1c", "free T4", "TSH", "Vitamin D"], "2026-11-21",
            ["J06.9 — Acute URTI"], " r/o dengue ", Nov21);
        Assert.Equal("Laboratory Request", doc.Title);
        Assert.Same(PrintPaper.A5, doc.Page);
        Assert.Equal(PrintPaper.WidthInches(PrintPaper.A5), PrintPaper.WidthInches(doc.Page), 6);
        Assert.Contains("@page { size: 148mm 210mm; margin: 0; }", doc.Html);
        Assert.DoesNotContain("4.3in", doc.Html);
        foreach (var name in new[] { "CBC", "URINALYSIS", "CREA", "HbA1c", "FT4", "TSH" })
            Assert.Contains("<span class=\"tick\">✓</span>___</span><span class=\"lab-name\">" + name + "</span>", doc.Html);
        foreach (var name in new[] { "LIPID PROFILE", "SGPT", "FBS", "BUA", "FT3", "ECG 12L" })
            Assert.Contains("<span class=\"lab-blank\">___</span><span class=\"lab-name\">" + name + "</span>", doc.Html);
        Assert.Contains("<div class=\"lab-others-lbl\">Others:</div><div class=\"lab-extra\">Vitamin D</div>", doc.Html);
        Assert.Contains("J06.9 — Acute URTI — r/o dengue", doc.Html);
        // the clinic's lab does these tests and no others
        Assert.Equal(["CBC", "URINALYSIS", "LIPID PROFILE", "SGPT", "CREA", "FBS", "BUA", "TSH", "FT3", "FT4", "HbA1c", "DENGUE NS1, IgG, IgM", "ECG 12L"],
            ClinicFormsHtml.LabDefaultItems.ToArray());
    }

    [Fact]
    public void A5_is_the_default_paper_of_the_three_forms_and_a_saved_choice_still_wins()
    {
        var o = new PrintingOptions();
        foreach (var kind in new[] { ClinicPrintKind.Prescription, ClinicPrintKind.LabRequest, ClinicPrintKind.MedicalCertificate })
        {
            Assert.Equal(PrinterPaper.A5, o.Resolve(kind).Paper);                                  // nothing saved: A5
            var (w, h) = o.Resolve(kind).PaperPoints!.Value;                                        // 148 x 210 mm in points
            Assert.Equal(148 / 25.4 * 72, w, 3); Assert.Equal(210 / 25.4 * 72, h, 3);
        }
        Assert.Equal(PrinterPaper.Auto, o.Resolve(ClinicPrintKind.QueueSlip).Paper);               // the slip and the receipt are not A5 forms
        Assert.Equal(PrinterPaper.Auto, o.Resolve(ClinicPrintKind.Receipt).Paper);
        o.For(ClinicPrintKind.Prescription).Paper = PrinterPaper.A4;                               // a choice saved on this computer wins
        o.For(ClinicPrintKind.LabRequest).Paper = PrinterPaper.Auto;
        Assert.Equal(PrinterPaper.A4, o.Resolve(ClinicPrintKind.Prescription).Paper);
        Assert.Equal(PrinterPaper.Auto, o.Resolve(ClinicPrintKind.LabRequest).Paper);
    }

    [Fact]
    public void An_old_Lab_pad_paper_choice_no_longer_shrinks_the_A5_lab_request()
    {
        var o = new PrintingOptions();
        o.For(ClinicPrintKind.LabRequest).Paper = PrinterPaper.LabPad;
        o.For(ClinicPrintKind.Prescription).Paper = PrinterPaper.LabPad;   // only the lab request is migrated
        Assert.Equal(PrinterPaper.Auto, o.Resolve(ClinicPrintKind.LabRequest).Paper);
        Assert.Equal(PrinterPaper.LabPad, o.Resolve(ClinicPrintKind.Prescription).Paper);
    }

    [Fact]
    public void License_and_PTR_numbers_print_when_the_doctor_filled_them_in_and_stay_blank_when_empty()
    {
        ClinicNode.Core.PrintDocument[] Forms(PrintDoctor d) =>
        [
            ClinicPrintForms.Prescription(Clinic, d, Patient, [Paracetamol], "2026-11-21", clock: Nov21),
            ClinicPrintForms.MedicalCertificate(Clinic, d, Patient, new MedCertData { IssueDate = "2026-11-21" }, Nov21),
            ClinicPrintForms.LabRequest(Clinic, d, Patient, ["CBC"], "2026-11-21", null, null, Nov21),
        ];
        foreach (var doc in Forms(Doctor with { LicenseNumber = "0125232", PtrNumber = "7788" }))
        {
            Assert.Contains("License No:</span><span class=\"u sig-u\">0125232</span>", doc.Html);
            Assert.Contains("PTR No:</span><span class=\"u sig-u\">7788</span>", doc.Html);
        }
        foreach (var doc in Forms(Doctor with { LicenseNumber = null, PtrNumber = "" }))
        {
            Assert.Contains("License No:</span><span class=\"u sig-u\"></span>", doc.Html);   // a blank line to write on
            Assert.Contains("PTR No:</span><span class=\"u sig-u\"></span>", doc.Html);
        }
    }

    [Fact]
    public void Medical_certificate_is_A5_circles_title_and_cleans_address()
    {
        var doc = ClinicPrintForms.MedicalCertificate(Clinic, Doctor with { S2Number = null }, Patient with { Sex = "Female" }, new MedCertData
        {
            IssueDate = "2026-11-21", PatientAddressSnapshot = "residing at Pusok, Lapu-Lapu, ", ExaminationDateFrom = "2026-11-20",
            DiagnosisText = "Acute\n  URTI", PurposeException = "legal",
        }, Nov21);
        Assert.Equal("Medical Certificate", doc.Title);
        Assert.Same(PrintPaper.A5, doc.Page);
        Assert.Contains("MEDICAL CERTIFICATE", doc.Html);
        Assert.Contains("<span class=\"t-opt t-hit\">Ms.</span>", doc.Html);
        Assert.Contains("<span class=\"u grow\">Pusok, Lapu-Lapu</span>", doc.Html);
        Assert.Contains("<span class=\"u grow\">Grace Medical Clinic</span>", doc.Html); // examined at defaults to clinic
        Assert.Contains("Acute URTI", doc.Html);
        Assert.Contains("mc-purpose", doc.Html);
        Assert.DoesNotContain("S2 No:", doc.Html);
    }

    [Theory]
    [InlineData("2026-11-21", "November 21, 2026")]
    [InlineData("2026-01-02", "January 2, 2026")]
    [InlineData("2026-11-20T17:30:00Z", "November 21, 2026")] // 1:30 AM in Manila
    [InlineData(null, "")]
    public void FormatDate_matches_en_PH_long_in_Manila(string? input, string expected) =>
        Assert.Equal(expected, ClinicPrintForms.FormatDate(input));

    [Theory]
    [InlineData("1990-11-21", "36")]
    [InlineData("1990-11-22", "35")]
    [InlineData("2030-01-01", "")]
    [InlineData("", "")]
    public void AgeFromDob_counts_birthdays_in_Manila(string dob, string expected) =>
        Assert.Equal(expected, ClinicPrintForms.AgeFromDob(dob, new DateOnly(2026, 11, 21)));
}
//JJQR - END
