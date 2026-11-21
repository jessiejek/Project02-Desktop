// JJQR - AI (Nov 21, 2026) - remember email only, never the password, via MAUI Preferences.
using ClinicNode.Core.Auth;

namespace ClinicNode.App.Services;

public sealed class PreferencesEmailMemory : IEmailMemory
{
    public const string Key = "clinicnode.remembered_email";

    public Task<string?> GetAsync(CancellationToken ct = default)
    {
        var value = Preferences.Default.Get(Key, string.Empty);
        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public Task SetAsync(string? email, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email)) Preferences.Default.Remove(Key);
        else Preferences.Default.Set(Key, email.Trim().ToLowerInvariant());
        return Task.CompletedTask;
    }
}
// JJQR - END
