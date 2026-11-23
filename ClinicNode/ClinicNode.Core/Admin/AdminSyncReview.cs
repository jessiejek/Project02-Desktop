//JJQR - AI (Nov 23, 2026) - web parity: the admin "Sync review" screen (web /admin/sync-review + MergePatientsModal). Problems sent by the clinic computers that need a person (possible duplicate patient, receipt number clash, refused or stuck changes),
// with the manual merge of two patients. Online only (the server decides), admin only. Wording and rules are the same as the web (Project02-fe lib/sync-review.ts).
namespace ClinicNode.Core.Admin;

public sealed record SyncReviewItem(
    Guid Id, string Kind, string Status, string Title, IReadOnlyDictionary<string, string> Details,
    Guid? PatientId, Guid? OtherPatientId, string? Aggregate, string? EntityId,
    DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt, string? Resolution, string? ResolutionNote);

public sealed record SyncReviewList(IReadOnlyList<SyncReviewItem> Items, int Total, int OpenTotal, int Page, int PageSize, bool FromServer);

public sealed record MergeCard(
    Guid PatientId, string PatientCode, string FirstName, string? MiddleName, string LastName, string? DateOfBirth, string? Sex,
    string? ContactNumber, string? Email, string? Address, string? City, bool HasAccount, IReadOnlyDictionary<string, int> Counts);

public sealed record MergePreview(MergeCard Keep, MergeCard Merge, bool BothHaveAccounts);

public interface IAdminSyncReviewService
{
    Task<SyncReviewList> ListAsync(string? status, string? kind, int page, int pageSize, CancellationToken ct = default);
    /// <summary>resolution is "dismissed" or "fixed"; the note is required (see <see cref="SyncReviewRules.NoteProblem"/>).</summary>
    Task ResolveAsync(Guid id, string resolution, string note, CancellationToken ct = default);
    Task<MergePreview> PreviewMergeAsync(Guid keepPatientId, Guid mergePatientId, CancellationToken ct = default);
    Task MergeAsync(Guid keepPatientId, Guid mergePatientId, string note, CancellationToken ct = default);
}

/// <summary>Offline / local-only mode: the review items live on the clinic server, so nothing can be listed or changed here.</summary>
public sealed class LocalAdminSyncReviewService : IAdminSyncReviewService
{
    public const string NeedsInternet = "Sync review needs an internet connection to the clinic server and an online sign-in.";
    public Task<SyncReviewList> ListAsync(string? status, string? kind, int page, int pageSize, CancellationToken ct = default) =>
        Task.FromResult(new SyncReviewList([], 0, 0, 1, pageSize, false));
    public Task ResolveAsync(Guid id, string resolution, string note, CancellationToken ct = default) => throw new InvalidOperationException(NeedsInternet);
    public Task<MergePreview> PreviewMergeAsync(Guid keepPatientId, Guid mergePatientId, CancellationToken ct = default) => throw new InvalidOperationException(NeedsInternet);
    public Task MergeAsync(Guid keepPatientId, Guid mergePatientId, string note, CancellationToken ct = default) => throw new InvalidOperationException(NeedsInternet);
}

/// <summary>Plain rules and wording, kept apart from the screen so they can be tested (twin of the web's lib/sync-review.ts).</summary>
public static class SyncReviewRules
{
    public const string Duplicate = "possible_duplicate_patient";
    public static readonly string[] Kinds = [Duplicate, "or_number_clash", "change_rejected", "change_stuck"];
    public const int NoteMin = 5, NoteMax = 1000;

    public static string KindLabel(string kind) => kind switch
    {
        Duplicate => "Possible duplicate patient",
        "or_number_clash" => "Receipt number used twice",
        "change_rejected" => "Change refused by the server",
        "change_stuck" => "Change stuck on a computer",
        _ => kind,
    };

    public static string KindHelp(string kind) => kind switch
    {
        Duplicate => "A patient arrived from a computer with the same first name, last name and birthdate as someone already on file. Nothing was merged. Compare the two and merge only if they are the same person.",
        "or_number_clash" => "Two payments carry the same receipt number. Both were kept as printed. Check the receipt booklet, then mark this resolved.",
        "change_rejected" => "A clinic computer sent a change that the server could not accept. The work is still on that computer. Check it there, redo it on the web if needed, then mark this resolved.",
        "change_stuck" => "A clinic computer has tried to send a change many times without success. Check that computer, then mark this resolved.",
        _ => "",
    };

    /// <summary>Pill tone name used by the desktop badges.</summary>
    public static string KindTone(string kind) => kind switch { Duplicate => "Warning", "or_number_clash" => "Danger", "change_rejected" => "Danger", "change_stuck" => "Warning", _ => "Neutral" };

    public static bool IsDuplicate(string kind) => kind == Duplicate;

    public static string? NoteProblem(string? note)
    {
        var t = (note ?? "").Trim();
        if (t.Length < NoteMin) return $"Write a note of at least {NoteMin} characters.";
        if (t.Length > NoteMax) return $"The note is too long ({NoteMax} characters at most).";
        return null;
    }

    static readonly Dictionary<string, (string One, string Many)> CountLabel = new()
    {
        ["bookings"] = ("visit", "visits"), ["consultations"] = ("consultation", "consultations"), ["medical_certificates"] = ("medical certificate", "medical certificates"),
        ["vitals"] = ("vitals reading", "vitals readings"), ["follow_ups"] = ("follow-up", "follow-ups"), ["lab_orders"] = ("lab order", "lab orders"), ["lab_values"] = ("lab value", "lab values"),
        ["lab_results"] = ("lab result", "lab results"), ["documents"] = ("document", "documents"), ["vaccinations"] = ("vaccination", "vaccinations"),
        ["prescriptions"] = ("prescription", "prescriptions"), ["consents"] = ("consent", "consents"), ["data_requests"] = ("data request", "data requests"),
        ["reviews"] = ("review", "reviews"), ["allergies"] = ("allergy", "allergies"),
    };

    /// <summary>"3 visits, 1 consultation" for what a patient holds (nothing at all: "No records").</summary>
    public static string CountsText(IReadOnlyDictionary<string, int>? counts)
    {
        var parts = (counts ?? new Dictionary<string, int>()).Where(kv => kv.Value > 0).Select(kv =>
        {
            var (one, many) = CountLabel.TryGetValue(kv.Key, out var l) ? l : (kv.Key.Replace('_', ' '), kv.Key.Replace('_', ' '));
            return $"{kv.Value} {(kv.Value == 1 ? one : many)}";
        }).ToList();
        return parts.Count > 0 ? string.Join(", ", parts) : "No records";
    }

    public static int TotalRecords(IReadOnlyDictionary<string, int>? counts) => (counts ?? new Dictionary<string, int>()).Values.Sum();

    /// <summary>What the admin is told before confirming.</summary>
    public static string MergeSummary(string keepName, string keepCode, string mergeName, string mergeCode, IReadOnlyDictionary<string, int> mergeCounts)
    {
        var moved = TotalRecords(mergeCounts) > 0
            ? $"Everything recorded for {mergeName} ({mergeCode}) — {CountsText(mergeCounts)} — will move to {keepName} ({keepCode}). "
            : $"{mergeName} ({mergeCode}) has no records to move. ";
        return $"{moved}{keepName}'s own details stay as they are; only fields that are empty on {keepName} are filled from {mergeName}. {mergeName} is then marked as merged and disappears from the lists. This cannot be undone from this page.";
    }

    public static string FullName(MergeCard c) => string.Join(" ", new[] { c.FirstName, c.MiddleName, c.LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));

    public sealed record FieldRow(string Label, string A, string B, bool Differs);

    static string Dash(string? v) => string.IsNullOrWhiteSpace(v) ? "—" : v!;

    /// <summary>The two records side by side, one row per field; Differs marks the rows to look at.</summary>
    public static IReadOnlyList<FieldRow> CompareRows(MergeCard a, MergeCard b)
    {
        static FieldRow Row(string label, string? x, string? y) => new(label, Dash(x), Dash(y), !string.Equals(Dash(x), Dash(y), StringComparison.OrdinalIgnoreCase));
        return
        [
            Row("Patient code", a.PatientCode, b.PatientCode), Row("First name", a.FirstName, b.FirstName), Row("Middle name", a.MiddleName, b.MiddleName),
            Row("Last name", a.LastName, b.LastName), Row("Birthdate", a.DateOfBirth, b.DateOfBirth), Row("Sex", a.Sex, b.Sex),
            Row("Contact number", a.ContactNumber, b.ContactNumber), Row("Email", a.Email, b.Email), Row("Address", a.Address, b.Address), Row("City", a.City, b.City),
        ];
    }
}
//JJQR - END
