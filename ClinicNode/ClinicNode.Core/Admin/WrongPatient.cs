//JJQR - AI (2026-11-26) - GAV-11: owner: the front desk tagged a different person as an existing patient; the doctor marks it "different" and carries
// this visit over. Same rules as the web (Project02-fe src/lib/wrong-patient.ts) and the server (POST /api/bookings/{id}/reassign-patient):
// ONE visit (and everything recorded in it, payment/OR included, also when finished or paid) moves to a new or an existing patient; the old
// patient's other visits never move. Allergies added during this visit move by default (the doctor can untick). A same-day visit of the chosen
// person and a name+birthday match are warnings first. Doctor and admin only (the server decides). ONLINE ONLY on this computer, and it syncs first:
// the move is one server transaction; a local copy could never be pushed safely (the server keeps the visit's patient as moved).
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;

namespace ClinicNode.Core.Admin;

public sealed record WrongPatientPerson(Guid PatientId, string PatientCode, string FirstName, string LastName, DateOnly? DateOfBirth = null)
{
    public string Name => $"{FirstName} {LastName}".Trim();
}

public sealed record WrongPatientAllergy(Guid Id, string Allergen, string? Reaction, string? Severity, bool IsNkda, bool AddedDuringVisit)
{
    public string Label => IsNkda ? "No known drug allergies" : string.IsNullOrWhiteSpace(Reaction) ? Allergen : $"{Allergen} ({Reaction})";
}

public sealed record WrongPatientSameDayVisit(Guid BookingId, string? QueueNumber, string Status);

public sealed record WrongPatientTarget(WrongPatientPerson Person, bool Unavailable, IReadOnlyList<WrongPatientSameDayVisit> VisitsThatDay)
{
    public bool HasVisitThatDay => VisitsThatDay.Count > 0;
}

public sealed record WrongPatientPreview(
    Guid BookingId, DateOnly AppointmentDate, string? QueueNumber, string Status,
    Guid? ConsultationId, string? ConsultationStatus, string? PaymentStatus, string? OrNumber,
    WrongPatientPerson Patient, IReadOnlyList<WrongPatientAllergy> Allergies, IReadOnlyDictionary<string, int> Counts, WrongPatientTarget? Target);

/// <summary>The new person typed in the dialog. <see cref="PatientId"/> is made once per dialog so a retried Confirm cannot create two patients.</summary>
public sealed record WrongPatientNewPerson(Guid PatientId, string FirstName, string? MiddleName, string LastName, SexType Sex, DateOnly? DateOfBirth, string? ContactNumber, string? Address);

public sealed record WrongPatientRequest(
    bool NewPatient, WrongPatientNewPerson? Person, Guid? TargetPatientId, string Reason, IReadOnlyList<Guid> MoveAllergyIds,
    bool AcknowledgeSameDayVisit = false, bool AcknowledgePossibleDuplicate = false);

public enum WrongPatientWarning { None, SameDayVisit, PossibleDuplicate }

public sealed record WrongPatientMoved(Guid BookingId, Guid FromPatientId, Guid ToPatientId, string PatientCode, string? QueueNumber, bool Already);

public sealed record WrongPatientOutcome(bool Succeeded, WrongPatientMoved? Moved, string? Error, WrongPatientWarning Warning = WrongPatientWarning.None, IReadOnlyList<WrongPatientPerson>? Matches = null)
{
    public static WrongPatientOutcome Ok(WrongPatientMoved moved) => new(true, moved, null);
    public static WrongPatientOutcome Fail(string error) => new(false, null, error);
    public static WrongPatientOutcome Warn(WrongPatientWarning w, string message, IReadOnlyList<WrongPatientPerson>? matches = null) => new(false, null, message, w, matches);
    public bool NeedsInternet => AdminOnlineOnly.IsNeedsInternet(Error);
}

/// <summary>Online: the clinic server. Offline (no connection / Offline mode): refused with <see cref="AdminOnlineOnly.NeedsInternet"/>, nothing saved.</summary>
public interface IVisitReassignService
{
    Task<AdminResult<WrongPatientPreview>> GetPreviewAsync(Guid bookingId, Guid? targetPatientId = null, CancellationToken ct = default);
    Task<WrongPatientOutcome> ReassignAsync(Guid bookingId, WrongPatientRequest request, CancellationToken ct = default);
}

/// <summary>The offline side of the online-first router: always "needs an internet connection".</summary>
public sealed class OfflineVisitReassignService : IVisitReassignService
{
    public Task<AdminResult<WrongPatientPreview>> GetPreviewAsync(Guid bookingId, Guid? targetPatientId = null, CancellationToken ct = default) =>
        Task.FromResult(AdminResult<WrongPatientPreview>.Fail(AdminOnlineOnly.NeedsInternet));
    public Task<WrongPatientOutcome> ReassignAsync(Guid bookingId, WrongPatientRequest request, CancellationToken ct = default) =>
        Task.FromResult(WrongPatientOutcome.Fail(AdminOnlineOnly.NeedsInternet));
}

/// <summary>"Sync first": this computer's unsent work for the visit must reach the server before the move (otherwise it could land on the wrong person).</summary>
public interface IVisitSyncGate
{
    /// <summary>Runs a sync now. Returns null when nothing of this visit is still waiting on this computer; otherwise the reason to stop.</summary>
    Task<string?> SyncFirstAsync(Guid bookingId, CancellationToken ct = default);
    /// <summary>No sync, just the check (used again right before Confirm).</summary>
    Task<string?> PendingProblemAsync(Guid bookingId, CancellationToken ct = default);
}

public static class WrongPatientRules
{
    public const int ReasonMin = 5, ReasonMax = 500;
    public const string PendingMessage = "This visit has changes on this computer that have not reached the clinic server yet. Sync, then try again. Nothing was moved.";

    public static string? ReasonProblem(string? reason)
    {
        var r = (reason ?? "").Trim();
        if (r.Length < ReasonMin) return "Write why (at least 5 characters), e.g. \"Different person, same name\".";
        if (r.Length > ReasonMax) return "The reason is too long (500 characters at most).";
        return null;
    }

    public static string? NewPersonProblem(WrongPatientNewPerson? p, DateOnly today)
    {
        if (p is null) return "Enter the new patient's details.";
        if (string.IsNullOrWhiteSpace(p.FirstName)) return "First name is required.";
        if (string.IsNullOrWhiteSpace(p.LastName)) return "Last name is required.";
        if (p.DateOfBirth is not { } dob) return "Birthday is required.";
        if (dob > today) return "Birthday cannot be in the future.";
        return null;
    }

    /// <summary>Everything the dialog checks before it calls the server (null = ready).</summary>
    public static string? Problem(WrongPatientRequest r, Guid currentPatientId, DateOnly today)
    {
        if (r.NewPatient) { if (NewPersonProblem(r.Person, today) is { } bad) return bad; }
        else if (r.TargetPatientId is not Guid t || t == Guid.Empty) return "Choose the patient this visit belongs to.";
        else if (t == currentPatientId) return "That is the patient the visit is already under. Choose the right person.";
        return ReasonProblem(r.Reason);
    }

    /// <summary>Ticked by default: the allergies recorded during this visit.</summary>
    public static IReadOnlyList<Guid> DefaultAllergyIds(WrongPatientPreview p) => p.Allergies.Where(a => a.AddedDuringVisit).Select(a => a.Id).ToList();

    public static string MovesSummary(WrongPatientPreview p)
    {
        var label = new (string Key, string Text)[]
        {
            ("consultations", "consultation notes"), ("prescriptions", "prescription"), ("lab_orders", "lab orders"), ("medical_certificates", "medical certificate"),
            ("follow_ups", "follow-up"), ("vitals", "vitals"), ("vaccinations", "vaccinations"), ("documents", "uploaded files"), ("lab_results", "lab results"), ("reviews", "review"),
        };
        var parts = label.Where(l => p.Counts.TryGetValue(l.Key, out var n) && n > 0).Select(l => l.Text).ToList();
        if (!string.IsNullOrEmpty(p.PaymentStatus) && p.PaymentStatus != "Unpaid") parts.Add(string.IsNullOrWhiteSpace(p.OrNumber) ? "payment" : $"payment (OR {p.OrNumber})");
        else if (p.Counts.TryGetValue("payments", out var pay) && pay > 0) parts.Add("bill");
        return parts.Count > 0 ? $"Moves with it: {string.Join(", ", parts)}." : "Only the visit itself moves (nothing recorded yet).";
    }

    /// <summary>The server's new_patient (a Patient row, so the JSON is the same as every other patient write). Names title-cased like Edit details.</summary>
    public static Patient NewPatientRow(WrongPatientNewPerson p) => new()
    {
        PatientId = p.PatientId,
        FirstName = PatientDetailsRules.TitleCase(p.FirstName),
        MiddleName = string.IsNullOrWhiteSpace(p.MiddleName) ? null : PatientDetailsRules.TitleCase(p.MiddleName),
        LastName = PatientDetailsRules.TitleCase(p.LastName),
        Sex = p.Sex,
        DateOfBirth = p.DateOfBirth ?? default,
        ContactNumber = string.IsNullOrWhiteSpace(p.ContactNumber) ? null : p.ContactNumber.Trim(),
        Address = string.IsNullOrWhiteSpace(p.Address) ? null : p.Address.Trim(),
        Email = "",
    };

    /// <summary>409 answers the dialog shows as a question ("Move anyway?") instead of an error.</summary>
    public static WrongPatientWarning WarningFor(string? errorCode) => errorCode switch
    {
        "target_has_visit_that_day" => WrongPatientWarning.SameDayVisit,
        "possible_duplicate" => WrongPatientWarning.PossibleDuplicate,
        _ => WrongPatientWarning.None,
    };

    public static string SameDayText(WrongPatientTarget t, DateOnly day)
    {
        var qs = string.Join(", ", t.VisitsThatDay.Select(v => v.QueueNumber).Where(q => !string.IsNullOrWhiteSpace(q)));
        return $"{t.Person.Name} already has a visit on {day:MMM d, yyyy}{(qs.Length > 0 ? $" ({qs})" : "")}. You can still move this one.";
    }
}
//JJQR - END
