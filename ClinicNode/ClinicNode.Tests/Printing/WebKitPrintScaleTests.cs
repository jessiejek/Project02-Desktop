//JJQR - AI (Nov 21, 2026) - margin / scale rules the Mac (WKWebView) renderer relies on; calibrated against boxes of known mm/px in the rendered PDF.
using ClinicNode.Core.Printing;

namespace ClinicNode.Tests.Printing;

public class WebKitPrintScaleTests
{
    const string Html = "<html><head><title>t</title></head><body>x</body></html>";

    [Fact]
    public void Zoom_restores_96dpi_from_webkit_print_layout() => Assert.Equal(0.9375, PrintPaper.WebKitPrintZoom, 6);

    [Fact]
    public void Printer_default_documents_get_the_web_16mm_margin_and_zoom()
    {
        var o = PrintPaper.WithWebPageMargin(Html, PrintPaper.PrinterDefault);
        Assert.Contains("padding: 16mm", o);
        Assert.Contains("zoom: 0.9375", o);
        Assert.Contains("@page { margin: 0; }", o);
        Assert.True(o.IndexOf("<style>", StringComparison.Ordinal) < o.IndexOf("</head>", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("A5")]
    [InlineData("Lab")]
    [InlineData("Thermal80")]
    public void Exact_size_forms_and_rolls_get_zoom_only_no_margin(string which)
    {
        var page = which switch { "A5" => PrintPaper.A5, "Lab" => PrintPaper.LabForm, _ => PrintPaper.Thermal80 };
        var o = PrintPaper.WithWebPageMargin(Html, page);
        Assert.Contains("zoom: 0.9375", o);
        Assert.DoesNotContain("padding", o);
    }

    [Fact]
    public void Web_default_margin_is_16mm() => Assert.Equal(16, PrintPaper.WebDefaultMarginMm);
}
//JJQR - END
