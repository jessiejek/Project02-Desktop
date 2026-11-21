//JJQR - AI (Nov 21, 2026) - paper sizes and unit maths shared by the Mac Catalyst and Windows renderers (unit-tested; no UI types).
namespace ClinicNode.Core.Printing;

public enum PaperKind
{
    /// <summary>Fixed cut sheet: the PDF page is exactly this size.</summary>
    Sheet,
    /// <summary>Thermal roll: fixed width, height follows the content.</summary>
    Roll,
    /// <summary>Whatever paper the printer has (web printHtml() without @page size, e.g. the laser queue slip).</summary>
    PrinterDefault,
}

public static class PrintPaper
{
    /// <summary>Prescription / medical certificate pad, 148 x 210 mm (web PAGE_A5 "148mm 210mm").</summary>
    public static readonly PrintPageSize A5 = PrintPageSize.A5;
    /// <summary>Lab request pad, 4.3 x 6.4 in (web PAGE_LAB "4.3in 6.4in").</summary>
    public static readonly PrintPageSize LabForm = PrintPageSize.LabForm;
    /// <summary>80 mm thermal roll (72 mm printable on most ESC/POS heads).</summary>
    public static readonly PrintPageSize Thermal80 = new("Thermal 80mm", 80, 0, "mm");
    /// <summary>58 mm thermal roll (48 mm printable).</summary>
    public static readonly PrintPageSize Thermal58 = new("Thermal 58mm", 58, 0, "mm");
    /// <summary>A4 placeholder for "printer default" jobs (the user picks the real paper in the dialog).</summary>
    public static readonly PrintPageSize PrinterDefault = new("Printer default", 210, 297, "mm");

    /// <summary>Web chrome (print.ts non-bare) sets <c>@page { margin: 16mm; }</c>; "printer default" jobs (queue slip, receipt) honour it.</summary>
    public const double WebDefaultMarginMm = 16;

    /// <summary>Printable-area inset in points on every side: 16 mm for printer-default jobs, 0 for exact-size forms and rolls.</summary>
    public static double PrintableInsetPoints(PrintPageSize page) =>
        KindOf(page) == PaperKind.PrinterDefault ? WebDefaultMarginMm / 25.4 * 72.0 : 0;

    /// <summary>
    /// WKWebView's print layout puts 1.25 CSS px on every point (0.8 pt/px) instead of the true 96 dpi (0.75 pt/px), so every mm/in/px length comes out
    /// 6.67 % too big (measured with calibration boxes). Zooming the document by 0.9375 restores true physical sizes.
    /// </summary>
    public const double WebKitPrintZoom = 0.75 / 0.8;

    /// <summary>
    /// Prepares web-print HTML for the WKWebView renderer: restores true physical size (<see cref="WebKitPrintZoom"/>) and, for printer-default
    /// documents (queue slip, receipt), applies the web's <c>@page { margin: 16mm }</c> as html padding because WebKit's print path only partly honours it.
    /// Exact-size forms and thermal rolls get the zoom only.
    /// </summary>
    public static string WithWebPageMargin(string html, PrintPageSize page)
    {
        var z = WebKitPrintZoom.ToString("0.#####", System.Globalization.CultureInfo.InvariantCulture);
        var style = KindOf(page) == PaperKind.PrinterDefault
            // zoom scales the element's own padding too, so 16mm here is 16 true mm of paper (calibrated)
            ? $"<style>@page {{ margin: 0; }} html {{ zoom: {z}; box-sizing: border-box; padding: {WebDefaultMarginMm.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}mm; }}</style>"
            : $"<style>html {{ zoom: {z}; }}</style>";
        var i = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? style + html : html.Insert(i, style);
    }

    public const string WebPageA5 = "148mm 210mm";
    public const string WebPageLab = "4.3in 6.4in";

    public static PaperKind KindOf(PrintPageSize page) =>
        ReferenceEquals(page, PrinterDefault) || page.Name == PrinterDefault.Name ? PaperKind.PrinterDefault
        : page.Height <= 0 ? PaperKind.Roll
        : PaperKind.Sheet;

    public static double ToInches(double value, string unit) => unit switch
    {
        "mm" => value / 25.4,
        "in" => value,
        "pt" => value / 72.0,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unit must be mm, in or pt."),
    };

    public static double WidthInches(PrintPageSize page) => ToInches(page.Width, page.Unit);
    public static double HeightInches(PrintPageSize page) => ToInches(page.Height, page.Unit);

    /// <summary>PostScript points (UIKit / PDF user space).</summary>
    public static double InchesToPoints(double inches) => inches * 72.0;
    /// <summary>Device-independent pixels (WinUI / CSS px): 96 per inch.</summary>
    public static double InchesToDips(double inches) => inches * 96.0;
    public static double CssPxToInches(double px) => px / 96.0;
    public static double CssPxToPoints(double px) => px * 0.75;

    /// <summary>Printable width of a thermal head in mm (80 → 72, 58 → 48); sheets return their width.</summary>
    public static double PrintableWidthMm(PrintPageSize page)
    {
        var mm = page.Unit == "mm" ? page.Width : page.Width * 25.4;
        if (KindOf(page) != PaperKind.Roll) return mm;
        return mm >= 76 ? 72 : mm >= 56 ? 48 : mm - 8;
    }

    /// <summary>ESC/POS characters per line in Font A (12-dot) for the roll width.</summary>
    public static int EscPosColumns(PrintPageSize page) => PrintableWidthMm(page) >= 70 ? 48 : 32;

    /// <summary>
    /// Final PDF page height for a roll job from the measured content height (CSS px), with a small tail so the
    /// cutter does not clip the last line. Clamped to [40 mm, 600 mm].
    /// </summary>
    public static double RollHeightInches(double contentHeightCssPx, double tailMm = 6)
    {
        var inches = CssPxToInches(Math.Max(0, contentHeightCssPx)) + tailMm / 25.4;
        return Math.Clamp(inches, 40 / 25.4, 600 / 25.4);
    }

    /// <summary>Sheet size in inches for a render (roll height supplied by the renderer after measuring).</summary>
    public static (double Width, double Height) PageInches(PrintPageSize page, double? measuredContentCssPx = null) =>
        KindOf(page) switch
        {
            PaperKind.Roll => (WidthInches(page), RollHeightInches(measuredContentCssPx ?? 0)),
            _ => (WidthInches(page), HeightInches(page)),
        };

    /// <summary>
    /// Pick the printer paper for a form (sizes in points): an exact match (±3 pt, either orientation) wins; otherwise the smallest paper
    /// the form fits on; otherwise (roll printers report short sheets) the paper whose width is closest. -1 when the list is empty.
    /// </summary>
    public static int ChoosePaperIndex(IReadOnlyList<(double Width, double Height)> papers, double width, double height)
    {
        if (papers.Count == 0) return -1;
        const double tol = 3;
        for (var i = 0; i < papers.Count; i++)
        {
            var (w, h) = papers[i];
            if ((Math.Abs(w - width) <= tol && Math.Abs(h - height) <= tol) || (Math.Abs(w - height) <= tol && Math.Abs(h - width) <= tol))
                return i;
        }
        var best = -1;
        for (var i = 0; i < papers.Count; i++)
        {
            var (w, h) = papers[i];
            if (w + tol >= width && h + tol >= height && (best < 0 || w * h < papers[best].Width * papers[best].Height)) best = i;
        }
        if (best >= 0) return best;
        best = 0;
        for (var i = 1; i < papers.Count; i++)
            if (Math.Abs(papers[i].Width - width) < Math.Abs(papers[best].Width - width)) best = i;
        return best;
    }

    public static bool IsPortrait(PrintPageSize page) => KindOf(page) == PaperKind.Roll || page.Height >= page.Width;
}
//JJQR - END
