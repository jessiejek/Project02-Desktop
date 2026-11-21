//JJQR - AI (Nov 21, 2026) - Windows print dialog: rasterises the exact-size PDF (Windows.Data.Pdf, 300 dpi) and prints it through the
// WinRT PrintManager + WinUI PrintDocument (preview, printer and paper choice in the system dialog).
using ClinicNode.Core.Printing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Printing;
using Windows.Data.Pdf;
using Windows.Graphics.Printing;
using Windows.Graphics.Printing.OptionDetails;
using Windows.Storage;
using Windows.Storage.Streams;
using CorePrintDocument = ClinicNode.Core.PrintDocument;
using Image = Microsoft.UI.Xaml.Controls.Image;
using PrintDocument = Microsoft.UI.Xaml.Printing.PrintDocument;

namespace ClinicNode.App.WinUI.Printing;

public static class WindowsPdfPrintService
{
    const double Dpi = 300;

    /// <summary>UI thread only. Completes when the user prints or cancels.</summary>
    public static async Task PrintAsync(string pdfPath, CorePrintDocument document, CancellationToken ct = default)
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No app window for the print dialog.");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        var pdf = await PdfDocument.LoadFromFileAsync(await StorageFile.GetFileFromPathAsync(pdfPath));
        var pages = new List<Image>();
        for (uint i = 0; i < pdf.PageCount; i++)
        {
            using var page = pdf.GetPage(i);
            var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
            {
                DestinationWidth = (uint)Math.Round(page.Size.Width / 96.0 * Dpi),
                DestinationHeight = (uint)Math.Round(page.Size.Height / 96.0 * Dpi),
            });
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(stream);
            // page.Size is in DIPs (1/96 in) = the PDF's true size, so the form prints 1:1 at the top-left of the sheet.
            pages.Add(new Image { Source = bmp, Width = page.Size.Width, Height = page.Size.Height, Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill });
        }

        var printDoc = new PrintDocument();
        var source = printDoc.DocumentSource;
        printDoc.Paginate += (_, _) => printDoc.SetPreviewPageCount(pages.Count, PreviewPageCountType.Final);
        printDoc.GetPreviewPage += (_, e) => printDoc.SetPreviewPage(e.PageNumber, pages[e.PageNumber - 1]);
        printDoc.AddPages += (_, _) =>
        {
            foreach (var p in pages) printDoc.AddPage(p);
            printDoc.AddPagesComplete();
        };

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = PrintManagerInterop.GetForWindow(hwnd);
        void OnRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
        {
            var task = args.Request.CreatePrintTask(document.Title, req => req.SetSource(source));
            task.Options.MediaSize = MediaSizeFor(document);
            task.Options.Orientation = PrintOrientation.Portrait;
            if (PrintPaper.KindOf(document.Page) == PaperKind.Roll) task.Options.ColorMode = PrintColorMode.Monochrome;
            task.Completed += (_, e) =>
            {
                if (e.Completion == PrintTaskCompletion.Failed) done.TrySetException(new InvalidOperationException("Printing failed."));
                else done.TrySetResult();
            };
        }
        manager.PrintTaskRequested += OnRequested;
        try
        {
            if (!await PrintManagerInterop.ShowPrintUIForWindowAsync(hwnd)) return;
            await done.Task.WaitAsync(ct);
        }
        finally
        {
            manager.PrintTaskRequested -= OnRequested; // one registration per window at a time
        }
    }

    /// <summary>A5 forms ask for ISO A5; other sizes (4.3x6.4 in lab pad, thermal roll) keep the driver default / user choice.</summary>
    static PrintMediaSize MediaSizeFor(CorePrintDocument document) =>
        document.Page.Name == PrintPaper.A5.Name ? PrintMediaSize.IsoA5 : PrintMediaSize.Default;
}
//JJQR - END
