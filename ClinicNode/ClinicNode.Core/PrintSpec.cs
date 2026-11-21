// JJQR - AI (Nov 21, 2026) - print page sizes ported from Project02-fe print-forms.ts.
namespace ClinicNode.Core;

/// <summary>Physical sizes for MAUI platform print. Letterhead is pre-printed stationery.</summary>
public static class PrintSpec
{
    public static readonly PrintPageSize Prescription = PrintPageSize.A5;
    public static readonly PrintPageSize MedicalCertificate = PrintPageSize.A5;
    public static readonly PrintPageSize LabRequest = PrintPageSize.LabForm;
    public static readonly PrintPageSize QueueSlip = PrintPageSize.Slip;
}

public sealed record PrintPageSize(string Name, double Width, double Height, string Unit)
{
    public static readonly PrintPageSize A5 = new("A5", 148, 210, "mm");
    public static readonly PrintPageSize LabForm = new("Lab request", 4.3, 6.4, "in");
    public static readonly PrintPageSize Slip = new("Queue slip", 80, 100, "mm");

    public string CssSize => Unit == "mm" ? $"{Width}mm {Height}mm" : $"{Width}in {Height}in";
    public string CssWidth => Unit == "mm" ? $"{Width}mm" : $"{Width}in";
    public string CssHeight => Unit == "mm" ? $"{Height}mm" : $"{Height}in";
    public double WidthPoints => ToPoints(Width, Unit);
    public double HeightPoints => ToPoints(Height, Unit);

    static double ToPoints(double value, string unit) => unit == "mm" ? value / 25.4 * 72.0 : value * 72.0;
}

public sealed record PrintDocument(string Title, PrintPageSize Page, string Html);

public interface IPrinter
{
    Task PrintAsync(PrintDocument document, CancellationToken ct = default);
}
// JJQR - END
