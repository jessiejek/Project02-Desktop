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

    //JJQR - AI (2026-11-26) - GAV-14 bigger print: up to six ordinary lines stay on one A5 page (roomy size for 1-3), more flow onto a 2nd page at the same size
    [Fact]
    public void More_than_six_medicines_switches_to_the_tall_layout()
    {
        var html = ClinicPrintForms.Prescription(Clinic, Doctor, Patient, Enumerable.Repeat(Paracetamol, 7).ToList(), clock: Nov21).Html;
        Assert.Contains("form form-rx form-tall", html);
    }

    [Fact]
    public void Six_short_medicines_stay_on_one_page_and_three_get_the_roomy_size()
    {
        var six = ClinicPrintForms.Prescription(Clinic, Doctor, Patient, Enumerable.Repeat(Paracetamol, 6).ToList(), clock: Nov21).Html;
        Assert.Contains("<div class=\"form form-rx\">", six);
        var three = ClinicPrintForms.Prescription(Clinic, Doctor, Patient, Enumerable.Repeat(Paracetamol, 3).ToList(), clock: Nov21).Html;
        Assert.Contains("<div class=\"form form-rx rx-roomy\">", three);
    }
    //JJQR - END

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
        Assert.Contains("Acute<br>URTI", doc.Html); //JJQR - AI (2026-11-27) - why: each line of the Diagnosis box now prints on its own ruled line (was "Acute URTI") //JJQR - END
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

    //JJQR - AI (2026-11-27) - why: Dr. Allyn wants each diagnosis on its own ruled line of the medical certificate
    [Fact]
    public void Diagnosis_splits_on_new_lines_semicolons_and_number_markers_but_not_doses_or_words()
    {
        Assert.Equal(["1) UPPER RESPIRATORY TRACT INFECTION", "2) DENGUE FEVER"], ClinicFormsHtml.SplitDiagnosisLines("1) UPPER RESPIRATORY TRACT INFECTION;2) DENGUE FEVER"));
        Assert.Equal(["1. URTI", "2. Dengue fever", "3. AGE"], ClinicFormsHtml.SplitDiagnosisLines("1. URTI 2. Dengue fever 3. AGE"));
        Assert.Equal(["1) URTI", "2) AGE"], ClinicFormsHtml.SplitDiagnosisLines("1) URTI\r\n\n2) AGE"));
        Assert.Equal(["1) a", "2) b", "3) c"], ClinicFormsHtml.SplitDiagnosisLines("1) a, 2) b, 3) c"));
        Assert.Equal(["Paracetamol 500mg 2.5 ml, Type 2 diabetes, COVID-19"], ClinicFormsHtml.SplitDiagnosisLines("Paracetamol 500mg 2.5 ml, Type 2 diabetes, COVID-19"));
        Assert.Empty(ClinicFormsHtml.SplitDiagnosisLines("  ; \n ;"));
        Assert.Empty(ClinicFormsHtml.SplitDiagnosisLines(null));
    }
    //JJQR - END

    //JJQR - AI (2026-11-27) - why: "Each on its own line" rows <-> saved diagnosis text (same cases as the web print-forms.test.mjs)
    [Fact]
    public void Diagnosis_rows_join_numbered_drop_blanks_and_never_double_a_typed_number()
    {
        Assert.Equal("1) UPPER RESPIRATORY TRACT INFECTION\n2) DENGUE FEVER", ClinicFormsHtml.DiagnosisTextFromRows(["UPPER RESPIRATORY TRACT INFECTION", "", "  DENGUE   FEVER ", "", ""]));
        Assert.Equal("1) URTI\n2) AGE\n3) HPN", ClinicFormsHtml.DiagnosisTextFromRows(["1) URTI", "2. AGE", "5) HPN"]));
        Assert.Equal("1) 2.5 ml syrup reaction\n2) Type 2 diabetes", ClinicFormsHtml.DiagnosisTextFromRows(["2.5 ml syrup reaction", "Type 2 diabetes"]));
        Assert.Equal("", ClinicFormsHtml.DiagnosisTextFromRows(["", " ", null]));
    }

    [Fact]
    public void Diagnosis_text_splits_into_rows_without_markers_padded_to_five()
    {
        Assert.Equal(["UPPER RESPIRATORY TRACT INFECTION", "DENGUE FEVER"], ClinicFormsHtml.DiagnosisRowsFromText("1) UPPER RESPIRATORY TRACT INFECTION;2) DENGUE FEVER"));
        Assert.Equal(["URTI", "AGE", "HPN"], ClinicFormsHtml.DiagnosisRowsFromText("1. URTI 2. AGE\n3) HPN"));
        Assert.Equal(["Acute URTI", "Hypertension"], ClinicFormsHtml.DiagnosisRowsFromText("Acute URTI; Hypertension"));
        Assert.Equal(["URTI", "AGE", "", "", ""], ClinicFormsHtml.DiagnosisRowsForEdit("1) URTI\n2) AGE"));
        Assert.Equal(7, ClinicFormsHtml.DiagnosisRowsForEdit(string.Join("\n", Enumerable.Range(1, 7).Select(i => i + ") X"))).Count);
        Assert.Equal(["1) URTI", "2) AGE"], ClinicFormsHtml.SplitDiagnosisLines(ClinicFormsHtml.DiagnosisTextFromRows(["URTI", "", "1) AGE"])));
    }
    //JJQR - END

    //JJQR - AI (2026-11-27) - why: "List style": bullets or numbers (same cases as the web print-forms.test.mjs)
    [Fact]
    public void Diagnosis_rows_bullets_style_saves_bullet_lines_numbers_is_default()
    {
        Assert.Equal("• URTI\n• DENGUE FEVER\n• AGE", ClinicFormsHtml.DiagnosisTextFromRows(["URTI", "", "2) DENGUE FEVER", "• AGE"], bullets: true));
        Assert.Equal("1) URTI\n2) AGE", ClinicFormsHtml.DiagnosisTextFromRows(["• URTI", "• AGE"]));
    }

    [Fact]
    public void Bullet_and_number_markers_both_split_and_strip_and_the_style_is_read_back()
    {
        Assert.Equal(["URTI", "DENGUE FEVER"], ClinicFormsHtml.DiagnosisRowsFromText("• URTI\n• DENGUE FEVER"));
        Assert.Equal(["URTI", "AGE", "HPN"], ClinicFormsHtml.DiagnosisRowsFromText("• URTI • AGE; • HPN"));
        Assert.Equal(["• URTI", "• AGE"], ClinicFormsHtml.SplitDiagnosisLines("• URTI • AGE"));
        Assert.True(ClinicFormsHtml.DiagnosisIsBullets("\n  • URTI\n• AGE"));
        Assert.False(ClinicFormsHtml.DiagnosisIsBullets("1) URTI\n2) AGE"));
        Assert.False(ClinicFormsHtml.DiagnosisIsBullets("URTI"));
        Assert.False(ClinicFormsHtml.DiagnosisIsBullets(""));
    }
    //JJQR - END

    //JJQR - AI (2026-11-27) - why: owner: "give me paragraph style diagnosis": "Print as: Paragraph" (same cases as the web print-forms.test.mjs)
    [Fact]
    public void Paragraph_lines_keep_her_lines_squeeze_spaces_and_blank_lines()
    {
        Assert.Equal(["Acute URTI.", "Rule out dengue."], ClinicFormsHtml.DiagnosisParagraphLines("  Acute   URTI.\r\nRule out dengue.  "));
        Assert.Equal(["First.", "", "Second."], ClinicFormsHtml.DiagnosisParagraphLines("\n\nFirst.\n\n\n\nSecond.\n\n"));
        Assert.Equal(["1) URTI; 2) AGE"], ClinicFormsHtml.DiagnosisParagraphLines("1) URTI; 2) AGE"));
        Assert.Empty(ClinicFormsHtml.DiagnosisParagraphLines(" \n "));
        Assert.Empty(ClinicFormsHtml.DiagnosisParagraphLines(null));
    }

    [Fact]
    public void Layout_is_read_back_from_the_text()
    {
        Assert.Equal(DiagnosisLayout.Lines, ClinicFormsHtml.DiagnosisLayoutFromText(""));
        Assert.Equal(DiagnosisLayout.Lines, ClinicFormsHtml.DiagnosisLayoutFromText(null));
        Assert.Equal(DiagnosisLayout.Lines, ClinicFormsHtml.DiagnosisLayoutFromText("1) URTI\n2) AGE"));
        Assert.Equal(DiagnosisLayout.Lines, ClinicFormsHtml.DiagnosisLayoutFromText("1) URTI;2) AGE"));
        Assert.Equal(DiagnosisLayout.Lines, ClinicFormsHtml.DiagnosisLayoutFromText("\n• URTI"));
        Assert.Equal(DiagnosisLayout.OneLine, ClinicFormsHtml.DiagnosisLayoutFromText("URTI; AGE"));
        Assert.Equal(DiagnosisLayout.Paragraph, ClinicFormsHtml.DiagnosisLayoutFromText("Patient seen for fever. Impression is URTI."));
        Assert.Equal(DiagnosisLayout.Paragraph, ClinicFormsHtml.DiagnosisLayoutFromText("2.5 days of fever.\nImpression: URTI"));
    }

    [Fact]
    public void Print_layout_pick_wins_over_the_text_and_is_per_visit()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Assert.Null(MedCertPrintLayout.Get(a));
        Assert.Equal(DiagnosisLayout.Paragraph, MedCertPrintLayout.For(a, "Plain text."));
        MedCertPrintLayout.Set(a, DiagnosisLayout.OneLine);
        Assert.Equal(DiagnosisLayout.OneLine, MedCertPrintLayout.For(a, "Plain text."));
        Assert.Equal(DiagnosisLayout.Lines, MedCertPrintLayout.For(b, "1) URTI"));
    }

    [Fact]
    public void Paragraph_prints_the_text_as_typed_over_enough_ruled_lines()
    {
        const string para = "Patient presented with 3 days of fever, cough and body malaise; no difficulty of breathing. Clinical impression is acute upper respiratory tract infection, r/o dengue fever. Advised rest & hydration <2 days>.";
        string Html(string dx, bool paragraph = true, bool oneLine = false) => ClinicFormsHtml.BuildMedicalCertificateHtml(new MedCertHtmlOptions("", "148mm 210mm",
            new PrintDoctor("Jane Doe", null, null), new PrintPatient("Juan"), "", "", "", "", "", dx, "", "", "", oneLine, paragraph));
        int Rules(string h) => System.Text.RegularExpressions.Regex.Matches(h, "<div class=\"wl\"></div>").Count - 3;
        var p = Html(para);
        Assert.Contains("Diagnosis/ Impressions:</span>Patient presented with 3 days of fever, cough and body malaise; no difficulty of breathing. Clinical impression is acute upper respiratory tract infection, r/o dengue fever. Advised rest &amp; hydration &lt;2 days&gt;.</div>", p);
        Assert.Equal(3, Rules(p));
        Assert.True(Rules(Html(para + " " + para)) > 3);
        Assert.Contains("--dx-fs:6pt", Html(string.Concat(Enumerable.Repeat(para, 5))));
        Assert.Contains("</span>1) URTI<br><br>Rule out dengue.</div>", Html("1) URTI\n\nRule out dengue."));
        Assert.Contains("</span>1) URTI; 2) AGE</div>", Html("1) URTI\n2) AGE", paragraph: false, oneLine: true));
        Assert.Contains("</span>1) URTI<br>2) AGE</div>", Html("1) URTI\n2) AGE", paragraph: false));
    }
    //JJQR - END
}
//JJQR - END
