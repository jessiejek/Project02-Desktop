// JJQR - AI (Nov 21, 2026) - local outbox row; written in the same transaction as the clinical change.
namespace ClinicNode.Data;

public sealed class SyncOutboxEntry
{
    public Guid OperationId { get; init; }
    public string Aggregate { get; init; } = "";
    public Guid EntityId { get; init; }
    public string OperationType { get; init; } = ""; // create | update | delete | replace
    public string PayloadJson { get; init; } = "{}";
    public DateTimeOffset CreatedAt { get; init; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? AckedAt { get; set; }
    public bool IsPending => AckedAt is null;
}
// JJQR - END
