//JJQR - AI (21 Nov 2026) - Admin "Sync computers": list the clinic's registered computers (nodes), rotate a node secret, revoke a node.
// A rotated secret for THIS computer goes straight into the OS credential vault and is never shown. For another computer it is shown once
// (so it can be typed into that computer) and dropped from memory when hidden. Secrets are never logged or written to a file.
using System.Collections.ObjectModel;
using System.Globalization;
using ClinicNode.Core.Admin;
using ClinicNode.Sync.Auth;

namespace ClinicNode.App.ViewModels.Admin;

public sealed record SyncComputerRow(
    Guid NodeId,
    string Name,
    string Details,
    string Status,
    string LastSeen,
    string Registered,
    bool IsThisComputer,
    bool CanManage,
    //JJQR - AI (21 Nov 2026) - server health fields; empty when not reported
    string LastPush = "",
    string Operations24h = "",
    string Rejected24h = "",
    bool HasRejections = false);
    //JJQR - END

public sealed class AdminSyncComputersViewModel : AdminViewModelBase
{
    readonly IAdminNodeService _svc;
    readonly INodeCredentialStore _credentials;

    public AdminSyncComputersViewModel(IAdminNodeService svc, INodeCredentialStore credentials)
    {
        _svc = svc;
        _credentials = credentials;
    }

    public ObservableCollection<SyncComputerRow> Items { get; } = [];
    public bool FromServer { get; private set; }
    public string ThisComputerLine { get; private set; } = "";

    string? _oneTimeSecret;
    public string? OneTimeSecret
    {
        get => _oneTimeSecret;
        private set { _oneTimeSecret = value; Raise(); Raise(nameof(HasOneTimeSecret)); }
    }
    public bool HasOneTimeSecret => !string.IsNullOrEmpty(OneTimeSecret);
    public string OneTimeSecretFor { get; private set; } = "";

    public void HideSecret()
    {
        OneTimeSecret = null;
        OneTimeSecretFor = "";
        Raise(nameof(OneTimeSecretFor));
    }

    static string When(DateTimeOffset? at) => at is { } v ? ClinicDateTime(v) : "Never";

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var list = await _svc.ListAsync();
            FromServer = list.FromServer;
            Items.Clear();
            foreach (var n in list.Items)
            {
                var details = string.Join(" · ", new[] { n.Platform, string.IsNullOrWhiteSpace(n.AppVersion) ? null : "v" + n.AppVersion }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                Items.Add(new SyncComputerRow(n.NodeId, n.Name, details, n.Status,
                    When(n.LastSeenAt),
                    n.RegisteredAt is { } r ? ClinicDateTime(r) : "—",
                    n.IsThisComputer,
                    list.FromServer && !string.Equals(n.Status, "Revoked", StringComparison.OrdinalIgnoreCase),
                    //JJQR - AI (21 Nov 2026)
                    list.FromServer ? When(n.LastPushAt) : "",
                    n.OperationCount24h is { } ops ? ops.ToString(CultureInfo.CurrentCulture) : "",
                    n.RejectedCount24h is { } rej ? rej.ToString(CultureInfo.CurrentCulture) : "",
                    n.RejectedCount24h > 0));
                    //JJQR - END
            }
            ThisComputerLine = "This computer's ID: " + list.ThisComputerId.ToString("D", CultureInfo.InvariantCulture)
                + " · Last successful sync: " + When(list.LastSuccessfulSyncAt);
            Raise(nameof(FromServer));
            Raise(nameof(ThisComputerLine));
            StatusMessage = !list.FromServer
                ? LocalAdminNodeService.NeedsServer
                : list.Items.Count == 0
                    ? "No computers are registered for sync yet."
                    : $"{list.Items.Count} computer{(list.Items.Count == 1 ? "" : "s")} registered";
        });
    }

    public async Task RotateAsync(SyncComputerRow row)
    {
        HideSecret();
        await RunAsync(async () =>
        {
            var res = await _svc.RotateSecretAsync(row.NodeId);
            if (!res.Succeeded || res.Value is null) { Error = res.Error ?? "The secret could not be changed."; return; }
            if (row.IsThisComputer)
            {
                await _credentials.SaveAsync(new NodeCredential(res.Value.NodeId, res.Value.NodeSecret));
                StatusMessage = "New secret saved to this computer's credential vault. Sync keeps working.";
            }
            else
            {
                OneTimeSecretFor = row.Name;
                Raise(nameof(OneTimeSecretFor));
                OneTimeSecret = res.Value.NodeSecret;
                StatusMessage = $"New secret for {row.Name}. It stops syncing until this secret is entered there.";
            }
        });
        if (!HasError) await ReloadQuietAsync();
    }

    public async Task RevokeAsync(SyncComputerRow row)
    {
        HideSecret();
        string? done = null;
        await RunAsync(async () =>
        {
            var res = await _svc.RevokeAsync(row.NodeId);
            if (!res.Succeeded) { Error = res.Error ?? "The computer could not be revoked."; return; }
            done = $"{row.Name} can no longer sync with the clinic server.";
        });
        if (done is not null)
        {
            await ReloadQuietAsync();
            StatusMessage = done;
        }
    }

    async Task ReloadQuietAsync()
    {
        var keep = StatusMessage;
        await LoadAsync();
        if (!string.IsNullOrWhiteSpace(keep)) StatusMessage = keep;
    }

    public static Task OpenRegisterAsync() => GoAsync("//admin-register-node");
}
//JJQR - END
