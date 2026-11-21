//JJQR - AI (21 Nov 2026) - IAdminOrNumberPrompt for the desktop (Jessie: "Add a field so the desk can type a new receipt number after a clash"):
// an OR entry field prefilled with the clashing number; required, trimmed, max 100 characters. Asks on the UI thread
// after the clinic server refused an online confirm/waive with 409 or_number_duplicate. Nothing is logged.
using ClinicNode.Core.Admin;

namespace ClinicNode.App.Views.Admin;

public sealed class AdminOrNumberPrompt : IAdminOrNumberPrompt
{
    public Task<string?> AskForAnotherAsync(string rejectedOrNumber, string problem, CancellationToken ct = default) =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;
            if (page is null) return null;
            var typed = await page.DisplayPromptAsync(
                "Receipt number already used",
                $"{problem}\nType another OR number (max {AdminOrNumberRules.MaxLength} characters).",
                "Use this number", "Cancel",
                placeholder: "OR number",
                maxLength: AdminOrNumberRules.MaxLength,
                keyboard: Keyboard.Text,
                initialValue: rejectedOrNumber ?? "");
            // required + trimmed (the caller re-asks when empty or over 100 characters); null = Cancel
            return typed?.Trim();
        });
}
//JJQR - END
