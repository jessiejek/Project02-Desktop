// JJQR - AI (Nov 21, 2026)
using Foundation;
using ObjCRuntime;
using UIKit;

namespace ClinicNode.App;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 3.4 (item 18, spike V7): the menu "Quit" (and Cmd-Q) is replaced by our own command, so the app can ask first when changes are not synced yet.
    /// <summary>Set by the app: returns true when it is fine to quit now (nothing waiting, or the user chose to quit anyway).</summary>
    public static Func<Task<bool>>? ConfirmQuitAsync { get; set; }

    [Export("quitClinicNode:")]
    public async void QuitClinicNode(NSObject? sender)
    {
        var ok = true;
        try { if (ConfirmQuitAsync is { } ask) ok = await ask(); } catch { ok = true; }   // never trap the user in the app
        if (ok) Environment.Exit(0);
    }

    public override void BuildMenu(IUIMenuBuilder builder)
    {
        base.BuildMenu(builder);
        if (builder.System != UIMenuSystem.MainSystem) return;
        var quit = UIKeyCommand.Create(new NSString("Quit Clinic Node"), null, new Selector("quitClinicNode:"), new NSString("q"), UIKeyModifierFlags.Command, null);
        // the system "Quit" menu is replaced by one that holds only our command
        //JJQR - AI (Nov 23, 2026) - the cloud build uses an older Mac toolchain whose UIMenu.Create takes the identifier as the UIMenuIdentifier value itself (not its NSString constant). Passing the value is accepted by both toolchains.
        builder.ReplaceMenu(UIMenuIdentifier.Quit.GetConstant()!, UIMenu.Create("", null, UIMenuIdentifier.None, UIMenuOptions.DisplayInline, [quit]));
        //JJQR - END
    }
    //JJQR - END
}
// JJQR - END
