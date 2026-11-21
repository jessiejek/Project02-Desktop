// JJQR - AI (Nov 21, 2026)
namespace ClinicNode.Core.Clinical;

public interface IConsultationService
{
    Task<ConsultationWorkspace> LoadByBookingAsync(Guid bookingId, CancellationToken ct = default);

    /// <summary>Validate required sections the same way the web page does (hidden Diagnosis is skipped).</summary>
    IReadOnlyList<string> MissingRequired(ConsultationDraft draft);

    Task<ConsultSaveResult> SaveDraftAsync(ConsultationDraft draft, CancellationToken ct = default);

    Task<ConsultSaveResult> CompleteAsync(ConsultationDraft draft, CancellationToken ct = default);

    // JJQR - AI (Nov 21, 2026) - amend a Completed/Amended visit (web ?mode=amend): clinical content only; fee, payment and booking status stay as completed.
    Task<ConsultSaveResult> AmendAsync(ConsultationDraft draft, CancellationToken ct = default);
    // JJQR - END
}
// JJQR - END
