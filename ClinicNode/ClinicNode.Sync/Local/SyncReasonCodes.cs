//JJQR - AI (Nov 21, 2026)
// Contract §3.3 reason codes -> text for the review list (used when the cloud sends no message).
using System.Text.RegularExpressions;

namespace ClinicNode.Sync.Local;

public static partial class SyncReasonCodes
{
    public const string IllegalTransition = "illegal_transition";
    public const string CloudEditOverwritten = "cloud_edit_overwritten";
    public const string InvalidOperation = "invalid_operation";
    public const string IdConflict = "id_conflict";
    //JJQR - AI (21 Nov 2026) - payment pushed with an OR that another payment already has: the cloud ACCEPTED it (printed OR kept) and flagged it for admin
    public const string OrNumberClash = "or_number_clash";
    /// <summary>Online confirm only: 409 {error:"or_number_duplicate", message, or_number} — nothing saved; ask for another OR.</summary>
    public const string OrNumberDuplicate = "or_number_duplicate";
    /// <summary>Push result: the booking was sent with no queue number and the cloud assigned one (not a clash; TASK 7, 21 Nov 2026).</summary>
    public const string QueueNumberReassigned = "queue_number_reassigned";
    /// <summary>NOT in API-SURFACE.md yet (expected parallel of or_number_clash once allen1 ships "accept the number and flag the clash"). Also the
    /// kind this computer records when it keeps its printed number against a different cloud number.</summary>
    public const string QueueNumberClash = "queue_number_clash";
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - TASK CLINIC-SETTINGS OUTBOX: clinic fee / settings changes are online-only (no replay, no push route)
    public const string OnlineOnly = "online_only";
    private static readonly HashSet<string> OnlineOnlyAggregates = new(StringComparer.OrdinalIgnoreCase)
    {
        "clinic_settings", "clinic_setting", "clinic-settings", "clinic-setting", "clinicsettings", "clinicsetting", "clinic_fee", "clinic_fees", "clinic-fee", "clinic-fees",
        //JJQR - AI (21 Nov 2026) - TASK INVITE/REVOKE OUTBOX (Jessie: "Staff invites and revokes: online-only.")
        "announcement", "staff_account", "doctor_schedule",
        //JJQR - END
    };
    public static bool IsOnlineOnlyAggregate(string? aggregate) => aggregate is not null && OnlineOnlyAggregates.Contains(aggregate.Trim());

    //JJQR - AI (21 Nov 2026) - TASK INVITE/REVOKE OUTBOX: review text per kind of online-only change
    public static string OnlineOnlyMessage(string? aggregate) => aggregate?.Trim().ToLowerInvariant() switch
    {
        "announcement" => "Announcement changes are online-only. This change was made on this computer and was NOT sent to the clinic server. Redo it while online.",
        "staff_account" => "Staff invites and revokes are online-only. This change was made on this computer and was NOT sent to the clinic server. Redo it while online.",
        "doctor_schedule" => "Doctor schedule changes are online-only. This change was made on this computer and was NOT sent to the clinic server. Redo it while online.",
        _ => Describe(OnlineOnly) ?? "This change is online-only and was not sent to the clinic server.",
    };
    //JJQR - END
    //JJQR - END

    private static readonly Dictionary<string, string> Text = new(StringComparer.OrdinalIgnoreCase)
    {
        ["unknown_aggregate"] = "The cloud does not know this kind of record.",
        ["cloud_owned"] = "This record is managed in the cloud and cannot be changed from this computer.",
        ["entity_id_mismatch"] = "The record id did not match its contents.",
        ["invalid_payload"] = "The cloud could not read this record.",
        [InvalidOperation] = "The cloud refused this change as invalid (unknown type, or its operation id was already used by another computer).",
        ["missing_dependency"] = "A related record has not reached the cloud yet.",
        //JJQR - AI (Nov 21, 2026)
        ["consultation_booking_clash"] = "The cloud has a different consultation for this visit. This computer's copy was kept for review.",
        ["payment_booking_clash"] = "The cloud has a different payment for this visit. This computer's payment was kept.",
        //JJQR - END
        ["finished_immutable"] = "The consultation is already finished in the cloud and cannot be changed.",
        [IllegalTransition] = "The cloud cannot move this record from its current status to the one recorded here. Review it.",
        ["cloud_further"] = "The cloud copy is further along; it was kept.",
        ["not_owner"] = "Another computer owns this payment.",
        ["refund_offline_forbidden"] = "Refunds cannot be recorded offline.",
        ["amount_mismatch"] = "The amount received must equal the amount due.",
        ["append_only"] = "This record can only be added, never changed.",
        ["already_recorded"] = "A different record for the same item already exists in the cloud.",
        ["delete_not_allowed"] = "This record cannot be deleted.",
        ["patient_code_taken"] = "The patient code was already used; the cloud issued a new one.",
        [CloudEditOverwritten] = "This computer's payment replaced a different edit made in the cloud. The cloud logged it for review.",
        //JJQR - AI (Nov 21, 2026) - contract v1.1
        //JJQR - AI (21 Nov 2026) - REPRINT COPY: no reprint suggestion; a printed number stays valid (this computer keeps it)
        [QueueNumberReassigned] = "This booking had no queue number; the clinic server assigned one. It is not a clash.", // TASK 7
        //JJQR - END
        ["stale_amendment"] = "The cloud has a newer copy of this visit. Your amendment was not applied: open the visit again and redo it (your text is kept in this review item).",
        ["payment_settled"] = "The amendment contradicts a payment that is already settled (waived fee cannot become a charge, paid fee cannot become a waiver). Your text is kept in this review item.",
        ["allergy_union"] = "The cloud's allergy list had changed; both lists were combined so no allergy is lost. Check the patient's allergies.",
        //JJQR - END
        //JJQR - AI (21 Nov 2026)
        [OrNumberClash] = "Flagged for admin: this OR number is also on another payment. The payment was accepted and the printed OR kept; an admin will review it.",
        [QueueNumberClash] = "Queue number clash: flagged for admin. The printed queue number was kept on this computer.",
        [OrNumberDuplicate] = "This OR number is already used on another payment. Enter a different OR number.",
        [OnlineOnly] = "Clinic fee and setting changes are online-only. This change was made on this computer and was NOT sent to the clinic server. Re-enter it in Settings while online.",
        //JJQR - END
        [IdConflict] = "That id already belongs to a different record in the cloud. Nothing was merged; review it.",
    };

    public static string? Describe(string? code) =>
        code is not null && Text.TryGetValue(code, out var t) ? t : null;

    /// <summary>Extracts <c>error</c> from a body like <c>HTTP 409: {"error":"id_conflict"}</c>.</summary>
    public static string? ErrorCodeOf(string? httpError)
    {
        if (httpError is null) return null;
        var m = ErrorRegex().Match(httpError);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex("\"error\"\\s*:\\s*\"([A-Za-z0-9_\\-]+)\"")]
    private static partial Regex ErrorRegex();

    /// <summary>Batch-level 4xx caused by one bad operation: isolate it instead of retrying the same batch forever.</summary>
    public static bool IsPoisonBatchError(string? code) =>
        code is InvalidOperation or IdConflict or "invalid_payload" or "batch_too_large";
}
//JJQR - END
