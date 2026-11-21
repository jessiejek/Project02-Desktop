//JJQR - AI (21 Nov 2026) - web lib/validation.ts (B-33 / B-01) for admin forms: phoneProblem, lengthProblem, futureDateProblem. Same wording as the web.
using System.Text.RegularExpressions;
using ClinicApp.Domain;

namespace ClinicNode.App.ViewModels.Admin;

public static partial class AdminWebRules
{
    [GeneratedRegex(@"^[0-9+()\-.\s]{7,20}$")]
    private static partial Regex PhoneChars();

    public static string? PhoneProblem(string? value, string label = "Contact number")
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return null;
        return !PhoneChars().IsMatch(v) || v.Count(char.IsDigit) < 7
            ? $"{label} must be a phone number (digits, spaces, + - ( ) only)."
            : null;
    }

    public static string? LengthProblem(string? value, int max, string label) =>
        (value ?? "").Trim().Length > max ? $"{label} is too long (max {max} characters)." : null;

    public static string? FutureDateProblem(DateOnly? value, string label = "Date of birth") =>
        value is { } d && d > ClinicClock.Today ? $"{label} cannot be in the future." : null;

    /// <summary>Web accountStatus(user_id, is_guest).</summary>
    public static string AccountStatus(Guid? userId, bool isGuest) =>
        userId is not null ? "LinkedAccount" : isGuest ? "NoAccount" : "AccountUnknown";
}
//JJQR - END
