//JJQR - AI (Nov 21, 2026) - printer settings for a clinic node. Loaded from {DataDirectory}/printing.json so the clinic can point the
// queue slip at a thermal printer without a rebuild. Every field is optional; defaults reproduce the web behaviour (system dialog, laser slip).
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicNode.Core.Printing;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum QueueSlipStyle
{
    /// <summary>Exactly the web WalkInWizard slip (dashed card on the printer's default paper).</summary>
    Web,
    /// <summary>80 mm thermal roll layout.</summary>
    Thermal80,
    /// <summary>58 mm thermal roll layout.</summary>
    Thermal58,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThermalTransport
{
    /// <summary>Through the OS print system (printer driver), like any other document.</summary>
    Driver,
    /// <summary>Raw ESC/POS bytes over TCP (port 9100) to <see cref="PrintingOptions.ThermalPrinterHost"/>. Works on Mac and Windows.</summary>
    EscPosNetwork,
    /// <summary>Raw ESC/POS bytes through the Windows spooler to <see cref="PrintingOptions.ThermalPrinterName"/> (USB thermal printers).</summary>
    EscPosWindowsSpooler,
}

public sealed class PrintingOptions
{
    public const string FileName = "printing.json";

    public QueueSlipStyle QueueSlipStyle { get; set; } = QueueSlipStyle.Web;
    public ThermalTransport ThermalTransport { get; set; } = ThermalTransport.Driver;

    /// <summary>Windows printer name, or on Mac the printer URL (ipp://…) for silent slip printing. Null = ask with the dialog.</summary>
    public string? ThermalPrinterName { get; set; }
    public string? ThermalPrinterHost { get; set; }
    public int ThermalPrinterPort { get; set; } = 9100;
    /// <summary>Cut the paper after an ESC/POS slip.</summary>
    public bool ThermalCut { get; set; } = true;

    /// <summary>Printer for Rx / lab / med cert when <see cref="ShowDialog"/> is false. Null = ask.</summary>
    public string? FormsPrinterName { get; set; }
    /// <summary>Show the system print dialog (default). False prints straight to the named printer when one is set.</summary>
    public bool ShowDialog { get; set; } = true;
    /// <summary>Keep the rendered PDF of every job under {DataDirectory}/print-pdf (field debugging; off by default because PDFs hold PHI).</summary>
    public bool KeepPdfCopies { get; set; }

    //JJQR - AI (Nov 21, 2026) - default: "Print" saves the document as a PDF and OPENS it in the computer's PDF viewer / browser, and you print from there (Cmd+P). Set false in printing.json to send straight to the macOS print system.
    public bool OpenAsPdf { get; set; } = true;
    //JJQR - AI (Nov 21, 2026) - with OpenAsPdf on: true (default) = open the document in the browser and pop the Print dialog automatically; false = just open the PDF file.
    public bool AutoPrintInBrowser { get; set; } = true;
    //JJQR - END
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - per-document printer/paper (Settings > This computer); missing kinds fall back to the fields above.
    public Dictionary<ClinicPrintKind, DocumentPrintSettings> Documents { get; set; } = new();
    //JJQR - END

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        //JJQR - AI (Nov 21, 2026) - enum keys/values as names ("Prescription": {...}, "paper": "A5"); names stay PascalCase like the existing QueueSlipStyle values
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: true) },
        //JJQR - END
    };

    public static PrintingOptions Load(string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory)) return new PrintingOptions();
        var path = Path.Combine(dataDirectory, FileName);
        if (!File.Exists(path)) return new PrintingOptions();
        try
        {
            return JsonSerializer.Deserialize<PrintingOptions>(File.ReadAllText(path), Json) ?? new PrintingOptions();
        }
        catch (JsonException)
        {
            return new PrintingOptions();
        }
    }

    public void Save(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, FileName), JsonSerializer.Serialize(this, Json));
    }

    public PrintPageSize QueueSlipPaper => QueueSlipStyle switch
    {
        QueueSlipStyle.Thermal80 => PrintPaper.Thermal80,
        QueueSlipStyle.Thermal58 => PrintPaper.Thermal58,
        _ => PrintPaper.PrinterDefault,
    };
}
//JJQR - END
