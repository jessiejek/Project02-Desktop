//JJQR - AI (21 Nov 2026) - Receipt (OR) number uniqueness, approved by Jessie via allen0.
//   * IAdminOrNumberPrompt: asked when the server refuses an online confirm/waive with 409 { error: "or_number_duplicate", message, or_number }.
//   * IAdminOrClashService: read-only list of OR numbers shared by more than one payment (GET /api/payments/or-clashes, Admin). Online only.
// OR numbers are still allocated by IAdminOrNumberAllocator exactly as before; the prompt only supplies a replacement after a server refusal.
namespace ClinicNode.Core.Admin;

public interface IAdminOrNumberPrompt
{
    /// <summary>Ask for a different OR number. <paramref name="problem"/> is the server's message (or a length problem). Null = the user cancelled.</summary>
    Task<string?> AskForAnotherAsync(string rejectedOrNumber, string problem, CancellationToken ct = default);
}

public static class AdminOrNumberRules
{
    public const int MaxLength = 100;
    public const string DuplicateError = "or_number_duplicate";
    public const string TooLong = "OR number is too long (max 100 characters).";
    public const string Required = "Enter an OR number.";

    /// <summary>Null when the typed OR number is usable.</summary>
    public static string? Problem(string? orNumber) =>
        string.IsNullOrWhiteSpace(orNumber) ? Required
        : orNumber.Trim().Length > MaxLength ? TooLong
        : null;
}

public sealed record OrClashPayment(
    Guid PaymentId,
    Guid BookingId,
    string Status,
    decimal Amount,
    DateTimeOffset? ConfirmedAt,
    Guid? OriginNodeId,
    string Origin);

public sealed record OrClash(string OrNumber, int Count, IReadOnlyList<OrClashPayment> Payments);

public sealed record OrClashList(IReadOnlyList<OrClash> Items, bool FromServer);

public interface IAdminOrClashService
{
    Task<OrClashList> ListAsync(CancellationToken ct = default);
}

/// <summary>Offline side: the clash list only exists on the clinic server.</summary>
public sealed class LocalAdminOrClashService : IAdminOrClashService
{
    public const string NeedsInternet = "Receipt number clashes need an internet connection to the clinic server and an online sign-in.";

    public Task<OrClashList> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(new OrClashList([], false));
}
//JJQR - END
