//JJQR - AI (Nov 23, 2026) - one-click updates: the app no longer runs inside Apple's sandbox (it cannot replace itself otherwise), so its data folder moves from
// ~/Library/Containers/ph.clinicnode.app/Data/Library/... to the normal user folder. On the first start after that, copy the old ClinicNode folder over ONCE (database, computer registration, sign-in files) so nothing is lost.
namespace ClinicNode.App.Platforms.MacCatalyst;

internal static class LegacySandboxMigration
{
    public static void Run(string newDataDir)
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var old = Path.Combine(home, "Library", "Containers", "ph.clinicnode.app", "Data", "Library", "ClinicNode");
            // also the Application Support location MAUI used inside the container
            var oldAlt = Path.Combine(home, "Library", "Containers", "ph.clinicnode.app", "Data", "Library", "Application Support", "ph.clinicnode.app", "ClinicNode");
            if (Path.GetFullPath(old).StartsWith(Path.GetFullPath(newDataDir), StringComparison.Ordinal)) return; // still inside the container: nothing to do
            var marker = Path.Combine(newDataDir, ".migrated-from-sandbox");
            if (File.Exists(marker)) return;
            Directory.CreateDirectory(newDataDir);
            if (Directory.EnumerateFileSystemEntries(newDataDir).Any()) { File.WriteAllText(marker, "skipped: target not empty"); return; }
            var src = Directory.Exists(old) ? old : Directory.Exists(oldAlt) ? oldAlt : null;
            if (src is not null) Copy(src, newDataDir);
            File.WriteAllText(marker, src is null ? "nothing to copy" : "copied from " + src);
        }
        catch { /* never block start-up; worst case the computer is registered again */ }
    }

    static void Copy(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), overwrite: false);
        foreach (var d in Directory.GetDirectories(from)) Copy(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
//JJQR - END
