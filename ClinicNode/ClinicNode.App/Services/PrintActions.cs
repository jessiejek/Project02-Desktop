// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core;
using ClinicNode.Core.Print;

namespace ClinicNode.App.Services;

public static class PrintActions
{
    public static async Task RunAsync(Page page, Func<Task<PrintDocument>> build)
    {
        try
        {
            var doc = await build();
            var printer = page.Handler?.MauiContext?.Services.GetService<IPrinter>()
                ?? Application.Current?.Handler?.MauiContext?.Services.GetRequiredService<IPrinter>();
            if (printer is null)
            {
                await page.DisplayAlertAsync("Print", "The printer service is not available.", "OK");
                return;
            }
            await printer.PrintAsync(doc);
        }
        catch (Exception ex)
        {
            await page.DisplayAlertAsync("Print", ex.Message, "OK");
        }
    }
}
// JJQR - END
