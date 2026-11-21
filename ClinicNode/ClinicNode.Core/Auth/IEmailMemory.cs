// JJQR - AI (Nov 21, 2026) - stores the remembered login email only (never the password).
namespace ClinicNode.Core.Auth;

public interface IEmailMemory
{
    Task<string?> GetAsync(CancellationToken ct = default);
    Task SetAsync(string? email, CancellationToken ct = default);
}

/// <summary>File-backed memory used by tests and as a fallback when MAUI Preferences are not available.</summary>
public sealed class FileEmailMemory : IEmailMemory
{
    private readonly string _path;
    public FileEmailMemory(string dataDirectory) =>
        _path = Path.Combine(dataDirectory, "remembered-email.txt");

    public Task<string?> GetAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return Task.FromResult<string?>(null);
        var value = File.ReadAllText(_path).Trim();
        return Task.FromResult(string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public Task SetAsync(string? email, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        if (string.IsNullOrWhiteSpace(email))
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        else
        {
            File.WriteAllText(_path, email.Trim().ToLowerInvariant());
        }
        return Task.CompletedTask;
    }
}
// JJQR - END
