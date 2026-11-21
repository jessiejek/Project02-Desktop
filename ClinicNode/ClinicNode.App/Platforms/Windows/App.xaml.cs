// JJQR - AI (Nov 21, 2026)
using Microsoft.UI.Xaml;

namespace ClinicNode.App.WinUI;

public partial class App : MauiWinUIApplication
{
    public App() => InitializeComponent();

    protected override MauiApp CreateMauiApp() => ClinicNode.App.MauiProgram.CreateMauiApp();
}
// JJQR - END
