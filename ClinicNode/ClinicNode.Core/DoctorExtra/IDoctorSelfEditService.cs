//JJQR - AI (Nov 21, 2026) - doctor self-edit write path (SYNC-API-CONTRACT §5.7): own profile fields, weekly availability and blocked dates,
// written locally and queued for the cloud with actor_user_id + field_updated_at. Fee / licence / PTR / S2 / slot / daily-limit stay cloud-owned and are never sent.
namespace ClinicNode.Core.DoctorExtra;

/// <summary>Outcome of a self-edit save: <c>Ok</c> or an error message that can be shown as-is.</summary>
public sealed record SelfEditResult(bool Ok, string? Error = null, Guid? Id = null)
{
    public static SelfEditResult Success(Guid? id = null) => new(true, null, id);
    public static SelfEditResult Failure(string error) => new(false, error);
}

/// <summary>The four profile fields a doctor may change offline (the desired values, like the web form). Blank bio / contact clears the field.</summary>
public sealed record DoctorSelfProfileEdit(string? FullName, string? Specialization, string? Bio, string? ContactNumber);

/// <summary>One weekday of the weekly availability (0 = Sunday … 6 = Saturday).</summary>
public sealed record DoctorScheduleDayEdit(short DayOfWeek, bool IsActive, TimeOnly Start, TimeOnly End);

public interface IDoctorSelfEditService
{
    /// <summary>Saves specialization / bio / full name / contact number; queues ONE <c>doctor</c> update carrying only the fields that changed.</summary>
    Task<SelfEditResult> SaveProfileAsync(Guid doctorId, DoctorSelfProfileEdit edit, CancellationToken ct = default);

    /// <summary>Upserts one <c>doctor_schedule</c> row per weekday that changed (create for a new weekday, update otherwise); one outbox op per changed row.</summary>
    Task<SelfEditResult> SaveWeeklyScheduleAsync(Guid doctorId, IReadOnlyList<DoctorScheduleDayEdit> days, CancellationToken ct = default);

    Task<SelfEditResult> AddBlockedDateAsync(Guid doctorId, DateOnly date, string? reason, CancellationToken ct = default);

    Task<SelfEditResult> UpdateBlockedDateReasonAsync(Guid blockedDateId, string? reason, CancellationToken ct = default);

    Task<SelfEditResult> RemoveBlockedDateAsync(Guid blockedDateId, CancellationToken ct = default);
}
//JJQR - END
