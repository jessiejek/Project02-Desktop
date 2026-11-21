//JJQR - AI (21 Nov 2026) - Api implementation of IAdminNodeService (Admin only on the server, SyncController):
//   GET /api/sync/nodes · POST /api/sync/nodes/{id}/rotate-secret → { node_id, node_secret } · POST /api/sync/nodes/{id}/revoke
// "This computer" is the local node identity (the id this app registers with), read through the local service.
// A rotated secret is returned to the screen once and never logged here.
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminNodeService(IApiClient api, LocalAdminNodeService local) : IAdminNodeService
{
    private sealed class NodeRow
    {
        public Guid NodeId { get; set; }
        public string? Name { get; set; }
        public string? Platform { get; set; }
        public string? AppVersion { get; set; }
        public string? Status { get; set; }
        public DateTimeOffset? RegisteredAt { get; set; }
        public DateTimeOffset? LastSeenAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        //JJQR - AI (21 Nov 2026) - new node health fields (the snake_case policy would read "operation_count24h", so name them)
        public DateTimeOffset? LastPushAt { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("operation_count_24h")]
        public int? OperationCount24h { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("rejected_count_24h")]
        public int? RejectedCount24h { get; set; }
        //JJQR - END
    }

    private sealed class SecretRow
    {
        public Guid NodeId { get; set; }
        public string? NodeSecret { get; set; }
    }

    public async Task<SyncComputerList> ListAsync(CancellationToken ct = default)
    {
        var rows = await api.GetAsync<List<NodeRow>>("/api/sync/nodes", null, ct) ?? [];
        var self = await local.ListAsync(ct);
        var items = rows
            .Select(r => new SyncComputerItem(r.NodeId, string.IsNullOrWhiteSpace(r.Name) ? "Unnamed computer" : r.Name!, r.Platform, r.AppVersion,
                string.IsNullOrWhiteSpace(r.Status) ? "Active" : r.Status!, r.RegisteredAt, r.LastSeenAt, r.RevokedAt, r.NodeId == self.ThisComputerId,
                //JJQR - AI (21 Nov 2026)
                r.LastPushAt, r.OperationCount24h, r.RejectedCount24h))
                //JJQR - END
            .OrderByDescending(i => i.IsThisComputer)
            .ThenBy(i => i.Status == "Revoked")
            .ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new SyncComputerList(items, self.ThisComputerId, true, self.LastSuccessfulSyncAt);
    }

    public async Task<AdminResult<SyncComputerSecret>> RotateSecretAsync(Guid nodeId, CancellationToken ct = default)
    {
        try
        {
            var res = await api.SendAsync<SecretRow>(HttpMethod.Post, $"/api/sync/nodes/{nodeId:D}/rotate-secret", null, null, ct);
            if (res is null || string.IsNullOrEmpty(res.NodeSecret))
                return AdminResult<SyncComputerSecret>.Fail("The server did not return a new secret.");
            return AdminResult<SyncComputerSecret>.Ok(new SyncComputerSecret(res.NodeId == Guid.Empty ? nodeId : res.NodeId, res.NodeSecret!));
        }
        catch (ApiNotFoundException) { return AdminResult<SyncComputerSecret>.Fail("That computer is not registered with the clinic server."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<SyncComputerSecret>.Fail(ex.Message); }
    }

    public async Task<AdminResult> RevokeAsync(Guid nodeId, CancellationToken ct = default)
    {
        try
        {
            await api.SendAsync(HttpMethod.Post, $"/api/sync/nodes/{nodeId:D}/revoke", null, null, ct);
            return AdminResult.Ok();
        }
        catch (ApiNotFoundException) { return AdminResult.Fail("That computer is not registered with the clinic server."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult.Fail(ex.Message); }
    }
}
//JJQR - END
