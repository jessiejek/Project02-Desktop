// JJQR - AI (Nov 21, 2026)
using ObjCRuntime;
using UIKit;

namespace ClinicNode.App;

public class Program
{
    static void Main(string[] args)
    {
        //JJQR - AI (Nov 22, 2026) - the release build quit with no readable reason. Write every managed error that reaches the system to startup-crash.log (next to the database) so it can be fixed. Costs nothing when nothing fails.
        static void Log(string where, object? what)
        {
            try
            {
                var dir = Path.Combine(Foundation.NSSearchPath.GetDirectories(Foundation.NSSearchPathDirectory.LibraryDirectory, Foundation.NSSearchPathDomain.User, true).First(), "ClinicNode");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "startup-crash.log"), $"{DateTimeOffset.Now:O} [{where}]\n{what}\n\n");
            }
            catch { /* nothing more we can do */ }
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("unhandled", e.ExceptionObject);
        Runtime.MarshalManagedException += (_, e) => Log("marshal", e.Exception);
        Runtime.MarshalObjectiveCException += (_, e) => Log("objc", e.Exception);
        //JJQR - END
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
// JJQR - END
