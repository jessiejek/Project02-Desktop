//JJQR - AI (Nov 21, 2026)
// Holds the cloud node secret (contract §2.1). The default store is a 0600 JSON file in the
// node's data folder, next to node-secret.key. The App can replace INodeCredentialStore with a
// Keychain / DPAPI (MAUI SecureStorage) implementation; nothing else changes.
using System.Text.Json;
using ClinicNode.Data;

namespace ClinicNode.Sync.Auth;

public sealed record NodeCredential(Guid NodeId, string NodeSecret);

public interface INodeCredentialStore
{
    Task<NodeCredential?> GetAsync(CancellationToken ct = default);
    Task SaveAsync(NodeCredential credential, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

public sealed class FileNodeCredentialStore : INodeCredentialStore
{
    private readonly string _path;

    public FileNodeCredentialStore(NodeOptions options)
        : this(Path.Combine(options.DataDirectory, "cloud-node-credential.json")) { }

    public FileNodeCredentialStore(string path) => _path = path;

    public async Task<NodeCredential?> GetAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path)) return null;
        await using var s = File.OpenRead(_path);
        var c = await JsonSerializer.DeserializeAsync<NodeCredential>(s, cancellationToken: ct);
        return c is { NodeSecret.Length: > 0 } ? c : null;
    }

    public async Task SaveAsync(NodeCredential credential, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(credential), ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, _path, overwrite: true);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_path)) File.Delete(_path);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory store for tests or for an App-provided secret.</summary>
public sealed class InMemoryNodeCredentialStore : INodeCredentialStore
{
    private NodeCredential? _c;
    public InMemoryNodeCredentialStore(NodeCredential? initial = null) => _c = initial;
    public Task<NodeCredential?> GetAsync(CancellationToken ct = default) => Task.FromResult(_c);
    public Task SaveAsync(NodeCredential credential, CancellationToken ct = default) { _c = credential; return Task.CompletedTask; }
    public Task ClearAsync(CancellationToken ct = default) { _c = null; return Task.CompletedTask; }
}
//JJQR - END
