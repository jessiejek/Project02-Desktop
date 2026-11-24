// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core;
using ClinicNode.Core.Print;

namespace ClinicNode.App.Services;

public static class PrintActions
{
    public static async Task RunAsync(Page page, Func<Task<PrintDocument>> build)
    {
        ClinicNode.App.Controls.PrintBusy.Begin(); //JJQR - AI (Nov 24, 2026) - loading message until the print window shows //JJQR - END
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
            ClinicNode.App.Controls.PrintBusy.End();
            await page.DisplayAlertAsync("Print", ex.Message, "OK");
        }
        finally { ClinicNode.App.Controls.PrintBusy.End(); }
    }
}
// JJQR - END
