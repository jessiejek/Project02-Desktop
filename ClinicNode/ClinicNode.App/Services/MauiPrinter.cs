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
            //JJQR - AI (Nov 21, 2026) - save the PDF to Downloads and open it; the doctor prints from the viewer (the direct print hand-off is the fragile part)
            if (_options.OpenAsPdf)
            {
                if (_options.AutoPrintInBrowser) OpenHtmlAndAutoPrint(document);
                else OpenPdfInViewer(document, pdf.ToArray());
                return;
            }
            //JJQR - END
            //JJQR - AI (Nov 22, 2026) - owner: native printing (the macOS print dialog opens inside the app). If macOS cannot show it, fall back to the browser page so a print is never lost.
            try
            {
                await Printing.MacPrintRenderer.PrintPdfAsync(pdf, document.Title, grayscale: PrintPaper.KindOf(document.Page) == PaperKind.Roll,
                    showDialog, printerName, resolved.PaperPoints);
            }
            catch (Exception)
            {
                OpenHtmlAndAutoPrint(document);
            }
            //JJQR - END
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

    //JJQR - AI (Nov 21, 2026) - write {Downloads}/Clinic Node/<time>-<title>.pdf and hand it to macOS to open in the default app
#if MACCATALYST
    /// <summary>Saves the document as a web page that prints itself on load and opens it in the default browser: the Print dialog appears on its own.</summary>
    static void OpenHtmlAndAutoPrint(PrintDocument document)
    {
        var dir = DownloadsFolder();
        var safe = string.Concat(document.Title.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        var path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{(safe.Length == 0 ? "document" : safe)}.html");
        //JJQR - AI (Nov 22, 2026) - the page opens as a BACKGROUND tab (Safari ignores print() there, so nothing appeared): print as soon as the tab is visible, and keep a Print / Save as PDF button on screen (hidden when printing) //JJQR - END
        const string auto =
            "<style>@media print{#cn-print-bar{display:none!important}}#cn-print-bar{position:fixed;top:12px;right:12px;z-index:99999;font:14px -apple-system,Segoe UI,sans-serif}" +
            "#cn-print-bar button{background:#0b5d4b;color:#fff;border:0;border-radius:8px;padding:10px 16px;font-size:14px;cursor:pointer;box-shadow:0 2px 8px rgba(0,0,0,.25)}</style>" +
            "<div id=\"cn-print-bar\"><button onclick=\"window.print()\">Print / Save as PDF</button></div>" +
            "<script>(function(){var done=false;function go(){if(done)return;done=true;setTimeout(function(){window.focus();window.print();},300);}" +
            "function ready(){if(document.visibilityState==='visible')go();}" +
            "window.addEventListener('load',ready);document.addEventListener('visibilitychange',ready);window.addEventListener('focus',go);})();</script>";
        var html = document.Html;
        var i = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        html = i >= 0 ? html.Insert(i, auto) : html + auto;
        File.WriteAllText(path, html, System.Text.Encoding.UTF8);
        UIKit.UIApplication.SharedApplication.OpenUrl(Foundation.NSUrl.FromFilename(path), new UIKit.UIApplicationOpenUrlOptions(), null);
    }

    static string DownloadsFolder()
    {
        try
        {
            var dl = Foundation.NSSearchPath.GetDirectories(Foundation.NSSearchPathDirectory.DownloadsDirectory, Foundation.NSSearchPathDomain.User, true).FirstOrDefault();
            var dir = Path.Combine(string.IsNullOrEmpty(dl) ? FileSystem.CacheDirectory : dl, "Clinic Node");
            Directory.CreateDirectory(dir);
            return dir;
        }
        catch (Exception)
        {
            var dir = Path.Combine(FileSystem.CacheDirectory, "Clinic Node");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    static void OpenPdfInViewer(PrintDocument document, byte[] pdf)
    {
        string dir;
        try
        {
            var dl = Foundation.NSSearchPath.GetDirectories(Foundation.NSSearchPathDirectory.DownloadsDirectory, Foundation.NSSearchPathDomain.User, true).FirstOrDefault();
            dir = Path.Combine(string.IsNullOrEmpty(dl) ? FileSystem.CacheDirectory : dl, "Clinic Node");
            Directory.CreateDirectory(dir);
        }
        catch (Exception)
        {
            dir = Path.Combine(FileSystem.CacheDirectory, "Clinic Node");
            Directory.CreateDirectory(dir);
        }
        var safe = string.Concat(document.Title.Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-');
        var path = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{(safe.Length == 0 ? "document" : safe)}.pdf");
        File.WriteAllBytes(path, pdf);
        UIKit.UIApplication.SharedApplication.OpenUrl(Foundation.NSUrl.FromFilename(path), new UIKit.UIApplicationOpenUrlOptions(), null);
    }
#endif
    //JJQR - END

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
