//JJQR - AI (Nov 23, 2026) - owner (offline test): "Could not register this patient" appeared and nothing recorded WHY. Screens that used to swallow an error now write it here (next to the database: errors.log), so a failure leaves evidence.
namespace ClinicNode.Core.Diagnostics;

public static class ClinicErrorLog
{
    /// <summary>The folder next to the database; set once at start-up. Null (tests) = nothing is written.</summary>
    public static string? Directory { get; set; }

    public static void Write(string where, Exception ex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Directory)) return;
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, "errors.log");
            // keep it small: past 1 MB the current file becomes errors.old.log (one generation)
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, Path.Combine(Directory, "errors.old.log"), overwrite: true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{where}]\n{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n\n");
        }
        catch { /* never fail because the log could not be written */ }
    }

    /// <summary>The app's own exceptions already carry a sentence meant for the person (the clinic server cannot be reached, register this computer first, ...): show it. Anything else gets the generic text.</summary>
    public static string Friendly(Exception ex, string generic) => ex switch
    {
        ClinicNode.Core.Sync.OnlineWriteFailedException or ClinicNode.Core.Sync.LocalWriteBlockedException => ex.Message,
        _ => generic,
    };
}
//JJQR - END
