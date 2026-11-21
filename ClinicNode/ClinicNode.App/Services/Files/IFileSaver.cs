// JJQR - AI (Nov 21, 2026) - platform save dialog for admin CSV/report/print exports.
namespace ClinicNode.App.Services.Files;

/// <summary>
/// Shows a native save dialog (Mac Catalyst NSSavePanel, Windows FileSavePicker).
/// When a picker is not available, writes under Documents/ClinicNode (same as <c>AdminLocalExport</c>).
/// Returns null if the user cancels the dialog.
/// Registered as a singleton in MauiProgram.cs (//JJQR - AI (21 Nov 2026) allen0 I-1 //JJQR - END).
/// </summary>
public interface IFileSaver
{
    Task<string?> SaveAsync(string suggestedFileName, string contents, string contentType = "text/plain", CancellationToken ct = default);
}
// JJQR - END
