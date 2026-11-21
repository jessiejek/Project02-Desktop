//JJQR - AI (Nov 21, 2026) - per-document printer / paper resolution and printing.json persistence.
using ClinicNode.Core.Printing;

namespace ClinicNode.Tests.NodeSettings;

public class DocumentPrintSettingsTests
{
    [Fact]
    public void Unconfigured_documents_fall_back_to_the_global_printer_for_their_kind()
    {
        var o = new PrintingOptions { FormsPrinterName = "HP-Laser", ThermalPrinterName = "ipp://slip", ShowDialog = false };
        var rx = o.Resolve(ClinicPrintKind.Prescription);
        Assert.Equal("HP-Laser", rx.PrinterName);
        Assert.False(rx.ShowDialog);
        Assert.Equal(PrinterPaper.Auto, rx.Paper);
        Assert.Null(rx.PaperPoints);
        Assert.Equal("ipp://slip", o.Resolve(ClinicPrintKind.QueueSlip).PrinterName);
    }

    [Fact]
    public void A_document_setting_beats_the_global_one()
    {
        var o = new PrintingOptions { FormsPrinterName = "HP-Laser", ShowDialog = true };
        o.For(ClinicPrintKind.Receipt).PrinterName = "Epson";
        o.For(ClinicPrintKind.Receipt).ShowDialog = false;
        o.For(ClinicPrintKind.Receipt).Paper = PrinterPaper.A5;
        var r = o.Resolve(ClinicPrintKind.Receipt);
        Assert.Equal("Epson", r.PrinterName);
        Assert.False(r.ShowDialog);
        Assert.Equal(PrinterPaper.A5, r.Paper);
        Assert.Equal("HP-Laser", o.Resolve(ClinicPrintKind.Prescription).PrinterName);
    }

    [Fact]
    public void Without_any_printer_name_the_dialog_is_always_shown_even_if_direct_printing_was_chosen()
    {
        var o = new PrintingOptions { ShowDialog = false };
        o.For(ClinicPrintKind.LabRequest).ShowDialog = false;
        Assert.True(o.Resolve(ClinicPrintKind.LabRequest).ShowDialog);
        Assert.Null(o.Resolve(ClinicPrintKind.LabRequest).PrinterName);
    }

    [Fact]
    public void Blank_printer_names_count_as_unset()
    {
        var o = new PrintingOptions { FormsPrinterName = "  " };
        o.For(ClinicPrintKind.MedicalCertificate).PrinterName = " ";
        Assert.Null(o.Resolve(ClinicPrintKind.MedicalCertificate).PrinterName);
    }

    [Theory]
    [InlineData(PrinterPaper.A5, 148, 210)]
    [InlineData(PrinterPaper.A4, 210, 297)]
    [InlineData(PrinterPaper.LabPad, 109.22, 162.56)]
    public void Paper_points_match_the_sheet(PrinterPaper p, double wMm, double hMm)
    {
        var (w, h) = DocumentPrintSettingsExtensions.PaperPoints(p)!.Value;
        Assert.Equal(wMm / 25.4 * 72, w, 1);
        Assert.Equal(hMm / 25.4 * 72, h, 1);
    }

    [Fact]
    public void Rolls_have_a_width_and_no_fixed_height()
    {
        Assert.Equal((80 / 25.4 * 72, 0), DocumentPrintSettingsExtensions.PaperPoints(PrinterPaper.Roll80)!.Value);
        Assert.Equal(0, DocumentPrintSettingsExtensions.PaperPoints(PrinterPaper.Roll58)!.Value.Height);
    }

    [Fact]
    public void Every_user_facing_kind_is_configurable_once()
    {
        var kinds = DocumentPrintSettingsExtensions.ConfigurableKinds.Select(k => k.Kind).ToList();
        Assert.Equal(kinds.Distinct().Count(), kinds.Count);
        Assert.Contains(ClinicPrintKind.QueueSlip, kinds);
        Assert.Contains(ClinicPrintKind.Receipt, kinds);
        Assert.DoesNotContain(ClinicPrintKind.Other, kinds);
    }

    [Fact]
    public void Printing_json_roundtrips_the_per_document_settings_and_old_files_still_load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cn-print-json-" + Guid.NewGuid().ToString("N"));
        try
        {
            var o = new PrintingOptions { FormsPrinterName = "HP", QueueSlipStyle = QueueSlipStyle.Thermal80 };
            o.For(ClinicPrintKind.Prescription).PrinterName = "A5-tray";
            o.For(ClinicPrintKind.Prescription).Paper = PrinterPaper.A5;
            o.For(ClinicPrintKind.QueueSlip).ShowDialog = false;
            o.Save(dir);
            var text = File.ReadAllText(Path.Combine(dir, "printing.json"));
            Assert.Contains("\"Prescription\"", text);
            Assert.Contains("\"Thermal80\"", text);
            Assert.Contains("\"A5\"", text);
            var back = PrintingOptions.Load(dir);
            Assert.Equal(QueueSlipStyle.Thermal80, back.QueueSlipStyle);
            Assert.Equal("A5-tray", back.Documents[ClinicPrintKind.Prescription].PrinterName);
            Assert.Equal(PrinterPaper.A5, back.Documents[ClinicPrintKind.Prescription].Paper);
            Assert.False(back.Documents[ClinicPrintKind.QueueSlip].ShowDialog);

            File.WriteAllText(Path.Combine(dir, "printing.json"), "{ \"formsPrinterName\": \"Old\", \"queueSlipStyle\": \"Web\" }");
            var old = PrintingOptions.Load(dir);
            Assert.Equal("Old", old.FormsPrinterName);
            Assert.Empty(old.Documents);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
//JJQR - END
