//JJQR - AI (Nov 21, 2026) - IPrinter for the clinic node. Mac Catalyst: HTML → exact-size PDF (WKWebView) → UIPrintInteractionController.
// Windows: HTML → exact-size PDF (WebView2) → PrintManager/PrintDocument (Platforms/Windows/Printing). Thermal queue slips can go raw
// ESC/POS (network on both, Windows spooler for USB) per PrintingOptions ({DataDirectory}/printing.json).
using ClinicNode.Core;
using ClinicNode.Core.Printing;

namespace ClinicNode.App.Services;

public sealed class MauiPrinter : IPrinter, IReceiptPrinter
{
    readonly PrintingOptions _options;

    public MauiPrinter(PrintingOptions? options = null) => _options = options ?? new PrintingOptions();

    public async Task PrintAsync(PrintDocument document, CancellationToken ct = default)
    {
        var info = document.Info();
        if (info.Kind == ClinicPrintKind.QueueSlip && info.QueueSlip is not null && await TryPrintRawSlipAsync(document, info.QueueSlip, ct))
            return;

        //JJQR - AI (Nov 21, 2026) - per-document printer / paper / dialog from Settings (falls back to the global printer for the kind)
        var resolved = _options.Resolve(info.Kind == ClinicPrintKind.Other ? ClinicPrintKind.Prescription : info.Kind);
        var printerName = resolved.PrinterName;
        var showDialog = resolved.ShowDialog;
        //JJQR - END
#if MACCATALYST
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var pdf = await Printing.MacPrintRenderer.RenderPdfAsync(document);
            KeepCopy(document, pdf.ToArray());
            await Printing.MacPrintRenderer.PrintPdfAsync(pdf, document.Title, grayscale: PrintPaper.KindOf(document.Page) == PaperKind.Roll,
                showDialog, printerName, resolved.PaperPoints);
        });
#elif WINDOWS
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var pdfPath = await ClinicNode.App.WinUI.Printing.WindowsPrintRenderer.RenderPdfAsync(document, ct);
            KeepCopy(document, await File.ReadAllBytesAsync(pdfPath, ct));
            if (!showDialog)
                await ClinicNode.App.WinUI.Printing.WindowsPrintRenderer.PrintSilentlyAsync(document, printerName!, ct);
            else
                await ClinicNode.App.WinUI.Printing.WindowsPdfPrintService.PrintAsync(pdfPath, document, ct);
        });
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException("Printing is implemented for Mac Catalyst and Windows.");
#endif
    }

    /// <summary>Official Receipt (web template) with a PENDING SYNC mark when <see cref="ReceiptPrintModel.IsPendingSync"/> is true.</summary>
    public Task PrintReceiptAsync(ReceiptPrintModel model, CancellationToken ct = default) =>
        PrintAsync(ReceiptDocument.Build(model), ct);

    /// <summary>Renders the document to PDF bytes without printing (export / diagnostics).</summary>
    public async Task<byte[]> RenderPdfAsync(PrintDocument document, CancellationToken ct = default)
    {
#if MACCATALYST
        return await MainThread.InvokeOnMainThreadAsync(async () => (await Printing.MacPrintRenderer.RenderPdfAsync(document)).ToArray());
#elif WINDOWS
        return await MainThread.InvokeOnMainThreadAsync(async () =>
            await File.ReadAllBytesAsync(await ClinicNode.App.WinUI.Printing.WindowsPrintRenderer.RenderPdfAsync(document, ct), ct));
#else
        await Task.CompletedTask;
        throw new PlatformNotSupportedException();
#endif
    }

    async Task<bool> TryPrintRawSlipAsync(PrintDocument document, QueueSlipData slip, CancellationToken ct)
    {
        var roll = PrintPaper.KindOf(document.Page) == PaperKind.Roll ? document.Page : PrintPaper.Thermal80;
        switch (_options.ThermalTransport)
        {
            case ThermalTransport.EscPosNetwork when !string.IsNullOrWhiteSpace(_options.ThermalPrinterHost):
                await EscPosNetwork.SendAsync(_options.ThermalPrinterHost!, _options.ThermalPrinterPort, QueueSlip.EscPos(slip, roll, _options.ThermalCut), ct);
                return true;
#if WINDOWS
            case ThermalTransport.EscPosWindowsSpooler when !string.IsNullOrWhiteSpace(_options.ThermalPrinterName):
                ClinicNode.App.WinUI.Printing.WindowsRawPrinter.Send(_options.ThermalPrinterName!, document.Title, QueueSlip.EscPos(slip, roll, _options.ThermalCut));
                return true;
#endif
            default:
                return false;
        }
    }

    void KeepCopy(PrintDocument document, byte[] pdf)
    {
        if (!_options.KeepPdfCopies) return;
        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "print-pdf");
            Directory.CreateDirectory(dir);
            var safe = string.Concat(document.Title.Select(c => char.IsLetterOrDigit(c) ? c : '-'));
            File.WriteAllBytes(Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{safe}.pdf"), pdf);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
//JJQR - END
