//JJQR - AI (21 Nov 2026) - web components/ui/StatusPill.tsx tone + label table in full (adds the account / staff vocabulary that ClinicTheme.StatusPill
// does not cover: Linked account, No account, Account unknown, Inactive, On leave). Admin screens only; shared ClinicTheme is untouched.
using ClinicNode.App.Controls;

namespace ClinicNode.App.Views.Admin;

public static class AdminPill
{
    static readonly Dictionary<string, string> Tone = new()
    {
        ["Pending"] = "Warning", ["ProofSubmitted"] = "Info", ["Confirmed"] = "Info", ["CheckedIn"] = "Info", ["InProgress"] = "Warning",
        ["OnHold"] = "Warning", ["Cancelled"] = "Danger", ["Completed"] = "Success", ["Expired"] = "Danger", ["NoShow"] = "Danger",
        ["Rescheduled"] = "Info", ["Unpaid"] = "Danger", ["Paid"] = "Success", ["Waived"] = "Info", ["Refunded"] = "Danger",
        ["Available"] = "Success", ["RunningLate"] = "Warning", ["UnavailableToday"] = "Danger",
        ["LinkedAccount"] = "Success", ["NoAccount"] = "Neutral", ["AccountUnknown"] = "Warning",
        ["Active"] = "Success", ["Inactive"] = "Neutral", ["OnLeave"] = "Warning", ["Invited"] = "Info",
    };

    static readonly Dictionary<string, string> Label = new()
    {
        ["ProofSubmitted"] = "Proof submitted", ["CheckedIn"] = "Checked in", ["InProgress"] = "In progress", ["OnHold"] = "On hold",
        ["NoShow"] = "No-show", ["RunningLate"] = "Running late", ["UnavailableToday"] = "Unavailable today",
        ["LinkedAccount"] = "Linked account", ["NoAccount"] = "No account", ["AccountUnknown"] = "Account unknown", ["OnLeave"] = "On leave",
    };

    /// <summary>Web statusLabel(): table label, else "CamelCase" → "Camel case".</summary>
    public static string Text(string status)
    {
        if (string.IsNullOrEmpty(status)) return "";
        if (Label.TryGetValue(status, out var l)) return l;
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < status.Length; i++)
        {
            var c = status[i];
            if (i > 0 && char.IsUpper(c) && char.IsLower(status[i - 1])) { sb.Append(' '); sb.Append(char.ToLowerInvariant(c)); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public static View Make(string status, string? tone = null, string? label = null)
    {
        var t = tone ?? (Tone.TryGetValue(status ?? "", out var x) ? x : "Neutral");
        return new Border
        {
            Style = ClinicTheme.Style("Pill" + t),
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center,
            Content = ClinicTheme.Text(label ?? Text(status ?? ""), "PillLabel" + t),
        };
    }
}
//JJQR - END
