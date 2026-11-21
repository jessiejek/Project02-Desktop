//JJQR - AI (Nov 21, 2026) - paper size / unit maths used by the native renderers.
using ClinicNode.Core.Printing;

namespace ClinicNode.Tests.Printing;

public class PrintPaperTests
{
    [Fact]
    public void Sheet_sizes_in_points_match_physical_paper()
    {
        Assert.Equal(419.53, PrintPaper.InchesToPoints(PrintPaper.WidthInches(PrintPaper.A5)), 2);
        Assert.Equal(595.28, PrintPaper.InchesToPoints(PrintPaper.HeightInches(PrintPaper.A5)), 2);
        Assert.Equal(309.6, PrintPaper.InchesToPoints(PrintPaper.WidthInches(PrintPaper.LabForm)), 6);
        Assert.Equal(460.8, PrintPaper.InchesToPoints(PrintPaper.HeightInches(PrintPaper.LabForm)), 6);
    }

    [Fact]
    public void Roll_height_follows_content_with_tail_and_clamps()
    {
        Assert.Equal(PaperKind.Roll, PrintPaper.KindOf(PrintPaper.Thermal80));
        Assert.Equal(400.0 / 96 + 6 / 25.4, PrintPaper.RollHeightInches(400), 6);
        Assert.Equal(40 / 25.4, PrintPaper.RollHeightInches(0), 6);
        Assert.Equal(600 / 25.4, PrintPaper.RollHeightInches(1_000_000), 6);
        var (w, h) = PrintPaper.PageInches(PrintPaper.Thermal58, 300);
        Assert.Equal(58 / 25.4, w, 6);
        Assert.True(h > 3);
    }

    [Fact]
    public void ChoosePaper_prefers_exact_then_smallest_fitting_then_closest_width()
    {
        var letter = (612.0, 792.0);
        var a4 = (595.28, 841.89);
        var a5 = (419.53, 595.28);
        Assert.Equal(2, PrintPaper.ChoosePaperIndex([letter, a4, a5], 419.5, 595.3));
        Assert.Equal(0, PrintPaper.ChoosePaperIndex([letter, a4], 309.6, 460.8)); // lab pad on letter (smallest area that fits)
        Assert.Equal(1, PrintPaper.ChoosePaperIndex([(164.0, 300.0), (226.8, 300.0)], 226.77, 900)); // 80 mm roll by width
        Assert.Equal(-1, PrintPaper.ChoosePaperIndex([], 1, 1));
    }

    [Fact]
    public void Printing_options_round_trip_through_printing_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cn-print-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(QueueSlipStyle.Web, PrintingOptions.Load(dir).QueueSlipStyle);
            new PrintingOptions { QueueSlipStyle = QueueSlipStyle.Thermal58, ThermalTransport = ThermalTransport.EscPosNetwork, ThermalPrinterHost = "10.0.0.9" }.Save(dir);
            var o = PrintingOptions.Load(dir);
            Assert.Equal(QueueSlipStyle.Thermal58, o.QueueSlipStyle);
            Assert.Equal(ThermalTransport.EscPosNetwork, o.ThermalTransport);
            Assert.Equal("10.0.0.9", o.ThermalPrinterHost);
            Assert.Same(PrintPaper.Thermal58, o.QueueSlipPaper);
            File.WriteAllText(Path.Combine(dir, PrintingOptions.FileName), "{ not json");
            Assert.Equal(QueueSlipStyle.Web, PrintingOptions.Load(dir).QueueSlipStyle);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
//JJQR - END
