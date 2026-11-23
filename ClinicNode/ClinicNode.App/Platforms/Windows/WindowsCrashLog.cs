//JJQR - AI (Nov 23, 2026) - owner: "APP CRASH" during a consultation left no trace (no errors.log line, nothing in Event Viewer).
// WINDOWS ONLY: this file is under Platforms/Windows, so the Mac build never compiles it. It only WRITES the real reason of a crash to errors.log
// (next to the database, same file the app already uses); it does not stop or change the crash. Registered from NativeUiEnhancements.Register.
namespace ClinicNode.App.WinUI;

public static class WindowsCrashLog
{
    static readonly object Gate = new();

    public static void Register()
    {
        try
        {
            if (Microsoft.UI.Xaml.Application.Current is { } app)
                app.UnhandledException += (_, e) =>
                {
                    Write("Windows error (UI, app kept open)", e.Exception);
                    e.Handled = true; // owner: a failure in one screen must not close the whole app in the middle of a consultation
                };
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("Windows crash (unhandled)", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) => Write("Windows background error", e.Exception);
        }
        catch { /* logging must never break start-up */ }
    }

    static void Write(string kind, Exception? ex)
    {
        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "ClinicNode");
            Directory.CreateDirectory(dir);
            lock (Gate)
                File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTimeOffset.Now:O} [{kind}: {ex?.Message}]\n{ex}\n\n");
        }
        catch { /* nothing more we can do */ }
    }
}
//JJQR - END
