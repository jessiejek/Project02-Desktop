//JJQR - AI (Nov 21, 2026) - per-document printer + paper choice (node Settings page), stored in printing.json under "documents".
using System.Text.Json.Serialization;

namespace ClinicNode.Core.Printing;

/// <summary>Sheet the printer should feed for a document. Auto = the form's own size (A5 pad, 4.3x6.4 in lab pad, roll).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PrinterPaper { Auto, A5, A4, Letter, Legal, LabPad, Roll80, Roll58 }

public sealed class DocumentPrintSettings
{
    /// <summary>Printer (Windows name / Mac ipp URL). Null = the global default for that kind (FormsPrinterName / ThermalPrinterName).</summary>
    public string? PrinterName { get; set; }
    public PrinterPaper Paper { get; set; } = PrinterPaper.Auto;
    /// <summary>Null = follow the global ShowDialog.</summary>
    public bool? ShowDialog { get; set; }
}

/// <summary>What the printer service uses for one job.</summary>
public sealed record ResolvedPrintSettings(string? PrinterName, bool ShowDialog, PrinterPaper Paper)
{
    /// <summary>Paper size in points for the dialog / paper picker; null = use the rendered PDF's size.</summary>
    public (double Width, double Height)? PaperPoints => DocumentPrintSettingsExtensions.PaperPoints(Paper);
}

public static class DocumentPrintSettingsExtensions
{
    /// <summary>The document kinds a user can configure, in Settings order, with their labels.</summary>
    public static readonly IReadOnlyList<(ClinicPrintKind Kind, string Label)> ConfigurableKinds =
    [
        (ClinicPrintKind.Prescription, "Prescription"),
        (ClinicPrintKind.LabRequest, "Lab request"),
        (ClinicPrintKind.MedicalCertificate, "Medical certificate"),
        (ClinicPrintKind.QueueSlip, "Queue slip"),
        (ClinicPrintKind.Receipt, "Official receipt"),
    ];

    public static (double Width, double Height)? PaperPoints(PrinterPaper paper) => paper switch
    {
        PrinterPaper.A5 => (148 / 25.4 * 72, 210 / 25.4 * 72),
        PrinterPaper.A4 => (210 / 25.4 * 72, 297 / 25.4 * 72),
        PrinterPaper.Letter => (8.5 * 72, 11 * 72),
        PrinterPaper.Legal => (8.5 * 72, 14 * 72),
        PrinterPaper.LabPad => (4.3 * 72, 6.4 * 72),
        PrinterPaper.Roll80 => (80 / 25.4 * 72, 0),
        PrinterPaper.Roll58 => (58 / 25.4 * 72, 0),
        _ => null,
    };

    public static DocumentPrintSettings For(this PrintingOptions o, ClinicPrintKind kind)
    {
        if (!o.Documents.TryGetValue(kind, out var s)) o.Documents[kind] = s = new DocumentPrintSettings();
        return s;
    }

    /// <summary>Per-document value, else the global printer for that kind (thermal for slips, forms for the rest).</summary>
    public static ResolvedPrintSettings Resolve(this PrintingOptions o, ClinicPrintKind kind)
    {
        o.Documents.TryGetValue(kind, out var s);
        var fallback = kind == ClinicPrintKind.QueueSlip ? o.ThermalPrinterName : o.FormsPrinterName;
        var printer = string.IsNullOrWhiteSpace(s?.PrinterName) ? fallback : s!.PrinterName!.Trim();
        var dialog = (s?.ShowDialog ?? o.ShowDialog) || string.IsNullOrWhiteSpace(printer);
        //JJQR - AI (Nov 24, 2026) - the lab request is now the same A5 sheet as the other forms: an old "Lab pad" paper choice saved on a computer must not shrink it back to 4.3 x 6.4 in
        var paper = s?.Paper ?? PrinterPaper.Auto;
        if (kind == ClinicPrintKind.LabRequest && paper == PrinterPaper.LabPad) paper = PrinterPaper.Auto;
        //JJQR - END
        return new ResolvedPrintSettings(string.IsNullOrWhiteSpace(printer) ? null : printer, dialog, paper);
    }
}
//JJQR - END
