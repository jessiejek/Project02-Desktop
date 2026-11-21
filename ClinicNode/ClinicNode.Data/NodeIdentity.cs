// JJQR - AI (Nov 21, 2026) - first-run node id and secret.
namespace ClinicNode.Data;

public sealed class NodeIdentity
{
    public Guid NodeId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastSuccessfulSyncAt { get; set; }
    public string? CloudBaseUrl { get; set; }
}
// JJQR - END
