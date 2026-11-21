// JJQR - AI (Nov 21, 2026) - no-op file exchange so NodeBackupCoordinator can resolve in tests / hosts without a picker.
namespace ClinicNode.App.Services.Backup;

public sealed class NullBackupFileExchange : IBackupFileExchange
{
    public Task<string?> ExportAsync(string tempFile, string suggestedName, CancellationToken ct = default) =>
        Task.FromResult<string?>(null);

    public Task<string?> PickBackupAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
// JJQR - END
