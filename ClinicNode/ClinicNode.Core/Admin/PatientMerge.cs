//JJQR - AI (2026-11-26) - GAV-12: owner (t118u): "jessie rubi made a consultation Nov 23; he came back Nov 25 but was created as a new account 'jese rubi'.
// The doctor noticed: oh, I remember you ... so the doctor will click MERGE TO EXISTING ACCOUNT". Same rules as the web (Project02-fe src/lib/merge-patient.ts)
// and the server (POST /api/patients/merge): the record the doctor PICKS (the existing, older one) is KEPT and its details win; only its blank fields are
// filled; every visit and record of the current one moves to it; the current one is retired (merged_into + hidden, never deleted). Doctor and admin
// (the server decides). An open visit today is a warning, not a stop. Both with a portal account = refused. ONLINE ONLY on this computer, syncs first.
namespace ClinicNode.Core.Admin;

public sealed record MergeActiveVisit(Guid BookingId, string? QueueNumber, string Status);

public sealed record MergeSide(
    Guid PatientId, string PatientCode, string FirstName, string? MiddleName, string LastName, DateOnly? DateOfBirth, string? Sex,
    string? ContactNumber, string? Email, string? Address, string? City, string? CivilStatus, string? BloodType, string? PhilhealthNumber, string? HmoProvider,
    bool HasAccount, DateTimeOffset? CreatedAt, IReadOnlyDictionary<string, int> Counts)
{
    public string Name => string.Join(" ", new[] { FirstName, MiddleName, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
}

public sealed record MergeToExistingPreview(MergeSide Keep, MergeSide Merge, bool BothHaveAccounts, IReadOnlyList<MergeActiveVisit> KeepActiveToday, IReadOnlyList<MergeActiveVisit> MergeActiveToday);

public sealed record PatientMerged(Guid KeptPatientId, string KeptPatientCode, Guid MergedPatientId, string MergedPatientCode, IReadOnlyDictionary<string, int> Moved, IReadOnlyList<string> FilledFields);

public sealed record PatientMergeOutcome(bool Succeeded, PatientMerged? Merged, string? Error)
{
    public static PatientMergeOutcome Ok(PatientMerged m) => new(true, m, null);
    public static PatientMergeOutcome Fail(string error) => new(false, null, error);
    public bool NeedsInternet => AdminOnlineOnly.IsNeedsInternet(Error);
}

/// <summary>Online: the clinic server. Offline: refused with <see cref="AdminOnlineOnly.NeedsInternet"/>, nothing saved.</summary>
public interface IPatientMergeService
{
    Task<AdminResult<MergeToExistingPreview>> PreviewAsync(Guid keepPatientId, Guid mergePatientId, CancellationToken ct = default);
    Task<PatientMergeOutcome> MergeAsync(Guid keepPatientId, Guid mergePatientId, string note, CancellationToken ct = default);
}

public sealed class OfflinePatientMergeService : IPatientMergeService
{
    public Task<AdminResult<MergeToExistingPreview>> PreviewAsync(Guid keepPatientId, Guid mergePatientId, CancellationToken ct = default) =>
        Task.FromResult(AdminResult<MergeToExistingPreview>.Fail(AdminOnlineOnly.NeedsInternet));
    public Task<PatientMergeOutcome> MergeAsync(Guid keepPatientId, Guid mergePatientId, string note, CancellationToken ct = default) =>
        Task.FromResult(PatientMergeOutcome.Fail(AdminOnlineOnly.NeedsInternet));
}

/// <summary>"Sync first" for a merge: every unsent change of the current patient (its record, its visits, payments, consultations, vitals) must reach the server first.</summary>
public interface IPatientSyncGate
{
    Task<string?> SyncFirstAsync(Guid patientId, CancellationToken ct = default);
    Task<string?> PendingProblemAsync(Guid patientId, CancellationToken ct = default);
}

public static class PatientMergeRules
{
    public const int NoteMin = 5, NoteMax = 1000;
    public const string PendingMessage = "This patient has changes on this computer that have not reached the clinic server yet. Sync, then try again. Nothing was merged.";

    public static string? Problem(Guid currentPatientId, Guid? targetId, string? note, bool bothHaveAccounts = false)
    {
        if (targetId is not Guid t || t == Guid.Empty) return "Search and choose the existing patient first.";
        if (t == currentPatientId) return "That is this same record. Choose the other (older) record.";
        if (bothHaveAccounts) return "Both patients have a patient-portal account, so they cannot be merged here.";
        var n = (note ?? "").Trim();
        if (n.Length < NoteMin) return "Write a note of at least 5 characters, e.g. \"Same person, registered twice\".";
        if (n.Length > NoteMax) return "The note is too long (1000 characters at most).";
        return null;
    }

    public static string? ActiveVisitWarning(MergeToExistingPreview p)
    {
        static string List(IReadOnlyList<MergeActiveVisit> v) => string.Join(", ", v.Select(x => string.IsNullOrWhiteSpace(x.QueueNumber) ? x.Status : x.QueueNumber));
        var parts = new List<string>();
        if (p.MergeActiveToday.Count > 0) parts.Add($"{p.Merge.Name} has a visit open today ({List(p.MergeActiveToday)}); it moves to {p.Keep.Name} and this screen reloads as {p.Keep.Name}.");
        if (p.KeepActiveToday.Count > 0) parts.Add($"{p.Keep.Name} also has a visit open today ({List(p.KeepActiveToday)}).");
        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    public static string Summary(MergeToExistingPreview p)
    {
        var what = SyncReviewRules.CountsText(p.Merge.Counts);
        return $"{p.Merge.Name} ({p.Merge.PatientCode}) will be merged into {p.Keep.Name} ({p.Keep.PatientCode}). "
            + (what == "No records" ? "" : $"Everything recorded for {p.Merge.Name} ({what}) moves to {p.Keep.Name}. ")
            + $"{p.Keep.Name}'s details stay as they are; only blank fields are filled from {p.Merge.Name}. {p.Merge.Name} ({p.Merge.PatientCode}) is then hidden from the lists (never deleted); an admin can unmerge it.";
    }

    /// <summary>Rows of the side-by-side compare: (label, kept value, current value, differs).</summary>
    public static IReadOnlyList<(string Label, string Keep, string Merge, bool Differs)> CompareRows(MergeSide k, MergeSide m)
    {
        static string D(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v.Trim();
        static string Day(DateOnly? d) => d is { } x ? x.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) : "—";
        var rows = new (string, string, string)[]
        {
            ("Patient code", D(k.PatientCode), D(m.PatientCode)), ("First name", D(k.FirstName), D(m.FirstName)), ("Middle name", D(k.MiddleName), D(m.MiddleName)),
            ("Last name", D(k.LastName), D(m.LastName)), ("Birthday", Day(k.DateOfBirth), Day(m.DateOfBirth)), ("Sex", D(k.Sex), D(m.Sex)),
            ("Contact number", D(k.ContactNumber), D(m.ContactNumber)), ("Address", D(k.Address), D(m.Address)), ("City", D(k.City), D(m.City)),
            ("Civil status", D(k.CivilStatus), D(m.CivilStatus)), ("Blood type", D(k.BloodType), D(m.BloodType)), ("PhilHealth no.", D(k.PhilhealthNumber), D(m.PhilhealthNumber)),
            ("HMO", D(k.HmoProvider), D(m.HmoProvider)),
            ("Registered", k.CreatedAt is { } kc ? Day(DateOnly.FromDateTime(kc.ToOffset(ClinicApp.Domain.ClinicClock.Offset).DateTime)) : "—", m.CreatedAt is { } mc ? Day(DateOnly.FromDateTime(mc.ToOffset(ClinicApp.Domain.ClinicClock.Offset).DateTime)) : "—"),
            ("Records held", SyncReviewRules.CountsText(k.Counts), SyncReviewRules.CountsText(m.Counts)),
        };
        return rows.Select(r => (r.Item1, r.Item2, r.Item3, !string.Equals(r.Item2, r.Item3, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public static string DoneText(PatientMerged r, string keepName, string goneName)
    {
        var moved = SyncReviewRules.CountsText(r.Moved);
        return $"{goneName} ({r.MergedPatientCode}) is now merged into {keepName} ({r.KeptPatientCode})." + (moved == "No records" ? "" : $" Moved: {moved}.")
            + (r.FilledFields.Count > 0 ? $" Filled blank fields: {r.FilledFields.Count}." : "");
    }
}
//JJQR - END
