//JJQR - AI (Nov 21, 2026) - Windows: hidden CoreWebView2 (Chromium, same engine as the web app in Edge/Chrome) lays out the form HTML and
// PrintToPdfAsync writes a PDF at the exact form size (A5, 4.3x6.4 in, thermal roll). Silent printing uses CoreWebView2.PrintAsync.
// Not compiled on the Mac; build and test on a Windows PC (packaging/README.md).
using ClinicNode.Core;
using ClinicNode.Core.Printing;
using Microsoft.Web.WebView2.Core;

namespace ClinicNode.App.WinUI.Printing;

public static class WindowsPrintRenderer
{
    static CoreWebView2Environment? _env;

    static string WorkDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ClinicNode", "print");
        Directory.CreateDirectory(dir);
        return dir;
    }

    static async Task<CoreWebView2Environment> EnvAsync()
    {
        // Unpackaged builds would otherwise try to write WebView2 data next to the exe (Program Files).
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClinicNode", "WebView2");
        return _env ??= await CoreWebView2Environment.CreateWithOptionsAsync(null, data, new CoreWebView2EnvironmentOptions());
    }

    static IntPtr MainHwnd()
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("No app window to host the print renderer.");
        return WinRT.Interop.WindowNative.GetWindowHandle(window);
    }

    /// <summary>Loads the HTML in a hidden WebView2 and runs <paramref name="action"/>. UI thread only.</summary>
    static async Task<T> WithWebViewAsync<T>(string html, Func<CoreWebView2Environment, CoreWebView2, Task<T>> action)
    {
        var env = await EnvAsync();
        var controller = await env.CreateCoreWebView2ControllerAsync(CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)MainHwnd()));
        try
        {
            controller.IsVisible = false;
            var core = controller.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            if (html.Length < 1_500_000) core.NavigateToString(html); // NavigateToString limit is 2 MB
            else
            {
                var file = Path.Combine(WorkDir(), $"{Guid.NewGuid():N}.html");
                await File.WriteAllTextAsync(file, html);
                core.Navigate(new Uri(file).AbsoluteUri);
            }
            if (!await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20)))
                throw new InvalidOperationException("The print document could not be laid out.");
            await core.ExecuteScriptAsync("document.fonts ? document.fonts.ready.then(()=>1) : 1");
            return await action(env, core);
        }
        finally
        {
            controller.Close();
        }
    }

    static async Task<CoreWebView2PrintSettings> SettingsAsync(CoreWebView2Environment env, CoreWebView2 core, PrintDocument document)
    {
        var page = document.Page;
        var kind = PrintPaper.KindOf(page);
        var s = env.CreatePrintSettings();
        s.ShouldPrintBackgrounds = true;
        s.ShouldPrintHeaderAndFooter = false;
        s.ShouldPrintSelectionOnly = false;
        s.ScaleFactor = 1.0;
        s.Orientation = CoreWebView2PrintOrientation.Portrait;
        if (kind == PaperKind.PrinterDefault)
        {
            s.MarginTop = s.MarginBottom = s.MarginLeft = s.MarginRight = 16 / 25.4; // web chrome @page { margin: 16mm }
            return s;
        }
        s.MarginTop = s.MarginBottom = s.MarginLeft = s.MarginRight = 0;
        double? measured = null;
        if (kind == PaperKind.Roll)
        {
            var json = await core.ExecuteScriptAsync("(function(){var e=document.body.firstElementChild||document.body;return Math.ceil(e.getBoundingClientRect().bottom);})()");
            measured = double.TryParse(json, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var px) ? px : 0;
        }
        var (w, h) = PrintPaper.PageInches(page, measured);
        s.PageWidth = w;
        s.PageHeight = h;
        return s;
    }

    /// <summary>Renders to a PDF file and returns its path. UI thread only.</summary>
    public static Task<string> RenderPdfAsync(PrintDocument document, CancellationToken ct = default) =>
        WithWebViewAsync(document.Html, async (env, core) =>
        {
            var path = Path.Combine(WorkDir(), $"{Guid.NewGuid():N}.pdf");
            var settings = await SettingsAsync(env, core, document);
            if (!await core.PrintToPdfAsync(path, settings))
                throw new InvalidOperationException("Could not create the print PDF.");
            return path;
        });

    /// <summary>Prints straight to a named Windows printer with no dialog (front-desk thermal printers, kiosk mode). UI thread only.</summary>
    public static Task PrintSilentlyAsync(PrintDocument document, string printerName, CancellationToken ct = default) =>
        WithWebViewAsync(document.Html, async (env, core) =>
        {
            var settings = await SettingsAsync(env, core, document);
            settings.PrinterName = printerName;
            settings.Copies = 1;
            var status = await core.PrintAsync(settings);
            if (status != CoreWebView2PrintStatus.Succeeded)
                throw new InvalidOperationException($"Printing to \"{printerName}\" failed ({status}).");
            return true;
        });
}
//JJQR - END
