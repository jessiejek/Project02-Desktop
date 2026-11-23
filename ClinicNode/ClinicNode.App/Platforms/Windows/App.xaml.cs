// JJQR - AI (Nov 21, 2026)
using Microsoft.UI.Xaml;

namespace ClinicNode.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
        //JJQR - AI (Nov 23, 2026) - owner (Windows): the app closed with no trace (no errors.log, nothing in Event Viewer). Write every crash to errors.log next to the database before Windows closes the app.
        UnhandledException += (_, e) => ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Windows crash: " + e.Message, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Crash (background)", ex);
        };
        //JJQR - END
    }

    protected override MauiApp CreateMauiApp() => ClinicNode.App.MauiProgram.CreateMauiApp();
}
// JJQR - END
