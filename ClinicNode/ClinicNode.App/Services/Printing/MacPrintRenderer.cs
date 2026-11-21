//JJQR - AI (Nov 21, 2026) - Mac Catalyst print path: off-screen WKWebView lays out the web form HTML with print CSS, UIPrintPageRenderer
// draws it into a PDF whose page is exactly the form size (A5, 4.3x6.4 in, thermal roll), and UIPrintInteractionController prints that PDF
// with the closest paper the printer offers. Pure UIKit/WebKit (no MAUI types) so packaging/print-smoke can run it outside the app.
#if MACCATALYST
using ClinicNode.Core;
using ClinicNode.Core.Printing;
using CoreGraphics;
using Foundation;
using UIKit;
using WebKit;

namespace ClinicNode.App.Services.Printing;

public static class MacPrintRenderer
{
    /// <summary>Renders the document to PDF bytes. Must be called on the main thread.</summary>
    public static async Task<NSData> RenderPdfAsync(PrintDocument document)
    {
        var page = document.Page;
        var kind = PrintPaper.KindOf(page);
        var widthIn = kind == PaperKind.PrinterDefault ? PrintPaper.WidthInches(PrintPaper.PrinterDefault) : PrintPaper.WidthInches(page);

        // CSS px == UIKit points inside WKWebView at zoom 1, so lay out at 96 px per inch of paper width.
        var cssWidth = PrintPaper.InchesToDips(widthIn);
        var config = new WKWebViewConfiguration();
        var web = new WKWebView(new CGRect(-20000, 0, cssWidth, 1200), config);
        var host = KeyWindow();
        host?.AddSubview(web);
        try
        {
            var nav = new LoadWaiter();
            web.NavigationDelegate = nav;
            web.LoadHtmlString(PrintPaper.WithWebPageMargin(document.Html, page), null);
            await nav.Task.WaitAsync(TimeSpan.FromSeconds(20));
            // let data: images decode and fonts settle
            await EvalAsync(web, "document.fonts ? document.fonts.ready.then(function(){return 1}) : 1");
            await Task.Delay(50);

            double heightIn;
            if (kind == PaperKind.Roll)
            {
                var px = await EvalNumberAsync(web, "(function(){var e=document.body.firstElementChild||document.body;return Math.ceil(e.getBoundingClientRect().bottom);})()");
                heightIn = PrintPaper.RollHeightInches(px);
            }
            else
            {
                heightIn = kind == PaperKind.PrinterDefault ? PrintPaper.HeightInches(PrintPaper.PrinterDefault) : PrintPaper.HeightInches(page);
            }

            var paper = new CGRect(0, 0, PrintPaper.InchesToPoints(widthIn), PrintPaper.InchesToPoints(heightIn));
            var renderer = new UIPrintPageRenderer();
            var formatter = web.ViewPrintFormatter;
            formatter.PerPageContentInsets = UIEdgeInsets.Zero;
            formatter.ContentInsets = UIEdgeInsets.Zero;
            renderer.AddPrintFormatter(formatter, 0);
            renderer.SetValueForKey(NSValue.FromCGRect(paper), new NSString("paperRect"));
            // printableRect = full paper: WebKit then lays out 1:1 (a smaller printableRect narrows the layout by an extra ~17 pt and ignores CSS @page).
            // The web's @page { margin: 16mm } for printer-default jobs is applied as html padding instead (PrintPaper.WithWebPageMargin).
            renderer.SetValueForKey(NSValue.FromCGRect(paper), new NSString("printableRect"));

            var pages = (int)renderer.NumberOfPages;
            // Fixed forms (and thermal rolls) are one sheet; only the long "form-tall" prescription and printer-default jobs may flow.
            var flows = kind == PaperKind.PrinterDefault || document.Html.Contains("form form-rx form-tall", StringComparison.Ordinal);
            var drawPages = flows ? Math.Max(1, pages) : 1;

            var data = new NSMutableData();
            UIGraphics.BeginPDFContext(data, paper, null);
            renderer.PrepareForDrawingPages(new NSRange(0, Math.Max(1, pages)));
            var bounds = UIGraphics.PDFContextBounds;
            for (var i = 0; i < drawPages; i++)
            {
                UIGraphics.BeginPDFPage();
                if (i < pages) renderer.DrawPage(i, bounds);  // bounds = paper; the renderer applies printableRect itself
            }
            UIGraphics.EndPDFContent();
            return data;
        }
        finally
        {
            web.StopLoading();
            web.NavigationDelegate = null!;
            web.RemoveFromSuperview();
            web.Dispose();
        }
    }

    /// <summary>Page size (points) of the first page of a PDF; used to pick paper.</summary>
    public static CGSize FirstPageSize(NSData pdf)
    {
        using var provider = new CGDataProvider(pdf);
        using var doc = new CGPDFDocument(provider);
        using var p = doc.GetPage(1);
        return p?.GetBoxRect(CGPDFBox.Media).Size ?? CGSize.Empty;
    }

    /// <summary>
    /// Prints a rendered PDF. <paramref name="printerUrl"/> set and <paramref name="showDialog"/> false = silent print to that printer.
    /// Must be called on the main thread.
    /// </summary>
    public static Task PrintPdfAsync(NSData pdf, string jobName, bool grayscale, bool showDialog = true, string? printerUrl = null,
        (double Width, double Height)? preferredPaper = null) //JJQR - AI (Nov 21, 2026) - preferredPaper: Settings paper choice (points; height 0 = roll)
    {
        var tcs = new TaskCompletionSource();
        var size = FirstPageSize(pdf);
        var info = UIPrintInfo.PrintInfo;
        info.JobName = jobName;
        info.OutputType = grayscale ? UIPrintInfoOutputType.Grayscale : UIPrintInfoOutputType.General;
        info.Orientation = size.Height >= size.Width ? UIPrintInfoOrientation.Portrait : UIPrintInfoOrientation.Landscape;
        info.Duplex = UIPrintInfoDuplex.None;

        var controller = UIPrintInteractionController.SharedPrintController;
        //JJQR - AI (Nov 21, 2026) - a chosen sheet (e.g. A5 Rx on A4 paper) wins over the PDF size; rolls keep the PDF height
        var want = preferredPaper is { } pp ? new CGSize(pp.Width, pp.Height > 0 ? pp.Height : size.Height) : size;
        var del = new PaperPicker(want);
        //JJQR - END
        _activeDelegate = del; // the controller holds its delegate weakly
        controller.Delegate = del;
        controller.PrintInfo = info;
        controller.PrintFormatter = null;
        controller.PrintPageRenderer = null;
        controller.PrintingItem = pdf;
        controller.ShowsPaperSelectionForLoadedPapers = true;
        controller.ShowsNumberOfCopies = true;
        controller.ShowsPageRange = false;

        void Done(UIPrintInteractionController? c, bool completed, NSError? error)
        {
            _activeDelegate = null;
            if (error is not null) tcs.TrySetException(new InvalidOperationException(error.LocalizedDescription));
            else tcs.TrySetResult();
        }

        if (!showDialog && !string.IsNullOrWhiteSpace(printerUrl))
        {
            var printer = UIPrinter.FromUrl(new NSUrl(printerUrl));
            if (!controller.PrintToPrinter(printer, (c, ok, err) => Done(c, ok, err)))
                tcs.TrySetException(new InvalidOperationException($"Could not print to {printerUrl}."));
        }
        else if (!controller.Present(true, (c, ok, err) => Done(c, ok, err)))
        {
            tcs.TrySetException(new InvalidOperationException("The print dialog could not be shown."));
        }
        return tcs.Task;
    }

    static PaperPicker? _activeDelegate;

    sealed class PaperPicker(CGSize size) : UIPrintInteractionControllerDelegate
    {
        // Closest paper the printer has to the form size (A5 pad, 4.3x6.4 in lab pad, 80/58 mm roll).
        public override UIPrintPaper ChoosePaper(UIPrintInteractionController printInteractionController, UIPrintPaper[] paperList)
        {
            var candidates = paperList.Select(p => (p, w: (double)p.PaperSize.Width, h: (double)p.PaperSize.Height)).ToList();
            var idx = PrintPaper.ChoosePaperIndex(candidates.Select(c => (c.w, c.h)).ToList(), size.Width, size.Height);
            return idx >= 0 ? paperList[idx] : paperList[0];
        }
    }

    sealed class LoadWaiter : WKNavigationDelegate
    {
        readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Task => _tcs.Task;
        public override void DidFinishNavigation(WKWebView webView, WKNavigation navigation) => _tcs.TrySetResult();
        public override void DidFailNavigation(WKWebView webView, WKNavigation navigation, NSError error) =>
            _tcs.TrySetException(new InvalidOperationException(error.LocalizedDescription));
        public override void DidFailProvisionalNavigation(WKWebView webView, WKNavigation navigation, NSError error) =>
            _tcs.TrySetException(new InvalidOperationException(error.LocalizedDescription));
    }

    static async Task<NSObject?> EvalAsync(WKWebView web, string js)
    {
        try { return await web.EvaluateJavaScriptAsync(js); }
        catch (NSErrorException) { return null; }
    }

    static async Task<double> EvalNumberAsync(WKWebView web, string js) =>
        await EvalAsync(web, js) is NSNumber n ? n.DoubleValue : 0;

    static UIWindow? KeyWindow()
    {
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
            if (scene is UIWindowScene ws)
                foreach (var w in ws.Windows)
                    if (w.IsKeyWindow) return w;
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
            if (scene is UIWindowScene ws && ws.Windows.Length > 0) return ws.Windows[0];
        return null;
    }
}
#endif
//JJQR - END
