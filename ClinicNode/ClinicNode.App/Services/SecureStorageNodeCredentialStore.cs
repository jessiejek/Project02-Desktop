// JJQR - AI (Nov 21, 2026) - SecureStorage-backed INodeCredentialStore (Keychain / OS credential vault).
using ClinicNode.Sync.Auth;

namespace ClinicNode.App.Services;

public sealed class SecureStorageNodeCredentialStore : INodeCredentialStore
{
    const string NodeIdKey = "clinicnode.sync.node_id";
    const string SecretKey = "clinicnode.sync.node_secret";

    //JJQR - AI (Nov 21, 2026) - DEBUG builds only: an unsigned / ad-hoc signed Mac build has no Keychain entitlement (MissingEntitlement), so the node secret
    // falls back to an owner-only file in the app's data folder. RELEASE builds stay Keychain-only (they are signed with the entitlement).
#if DEBUG
    static readonly FileNodeCredentialStore Fallback =
        new(Path.Combine(FileSystem.AppDataDirectory, "ClinicNode", "cloud-node-credential.json"));
#endif

    public async Task<NodeCredential?> GetAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var id = await SecureStorage.Default.GetAsync(NodeIdKey);
            var secret = await SecureStorage.Default.GetAsync(SecretKey);
            if (Guid.TryParse(id, out var nodeId) && !string.IsNullOrWhiteSpace(secret))
                return new NodeCredential(nodeId, secret);
        }
#if DEBUG
        catch (Exception) { /* no Keychain access in this build: use the file below */ }
        return await Fallback.GetAsync(ct);
#else
        catch (Exception) { throw; }
        return null;
#endif
    }

    public async Task SaveAsync(NodeCredential credential, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            await SecureStorage.Default.SetAsync(NodeIdKey, credential.NodeId.ToString());
            await SecureStorage.Default.SetAsync(SecretKey, credential.NodeSecret);
        }
#if DEBUG
        catch (Exception) { await Fallback.SaveAsync(credential, ct); }
#else
        catch (Exception) { throw; }
#endif
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            SecureStorage.Default.Remove(NodeIdKey);
            SecureStorage.Default.Remove(SecretKey);
        }
        catch (Exception) { /* nothing stored there */ }
#if DEBUG
        await Fallback.ClearAsync(ct);
#else
        await Task.CompletedTask;
#endif
    }
    //JJQR - END
}
// JJQR - END
