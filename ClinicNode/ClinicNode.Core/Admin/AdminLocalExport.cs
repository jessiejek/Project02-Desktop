// JJQR - AI (Nov 21, 2026) - fallback writer for admin CSV/HTML exports under Documents/ClinicNode.
namespace ClinicNode.Core.Admin;

/// <summary>
/// Local file helper for admin exports when no save dialog is available.
/// App <c>IFileSaver</c> (Services/Files) should be tried first; this is the fallback path.
/// </summary>
public static class AdminLocalExport
{
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ClinicNode");

    public static string WriteUtf8(string fileName, string contents, string? folder = null)
    {
        var dir = string.IsNullOrWhiteSpace(folder) ? Folder : folder;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, contents);
        return path;
    }
}
// JJQR - END
