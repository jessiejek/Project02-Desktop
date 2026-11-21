// JJQR - AI (Nov 21, 2026) - typed access to the web clinical_precision tokens in C# (consultation stepper, sidebar, dialogs).
using ClinicApp.Domain.Enums;

namespace ClinicNode.App.Controls;

public static class ClinicTheme
{
    public const string ClinicName = "Dr. Grace Gavino Medical Clinic";
    public const string Font = "Inter";
    public const string FontMedium = "InterMedium";
    public const string FontSemiBold = "InterSemiBold";
    public const double SidebarWidth = 220; //JJQR - AI (Nov 21, 2026) desktop: narrower sidebar //JJQR - END
    public const double MinTouch = 38; //JJQR - AI (Nov 21, 2026) desktop: mouse targets //JJQR - END
    public const double RadiusLg = 16;
    public const double RadiusXl = 24;
    public const double RadiusNav = 8;

    public static Color Color(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color color
            ? color
            : Colors.Black;

    public static Style? Style(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var value) == true ? value as Style : null;

    public static T Apply<T>(this T view, string styleKey) where T : VisualElement
    {
        var style = Style(styleKey);
        if (style is not null) view.Style = style;
        return view;
    }

    public static Label Text(string text, string styleKey) =>
        new Label { Text = text }.Apply(styleKey);

    /// <summary>Web StatusPill tones for booking/payment vocabulary.</summary>
    public static (string BorderStyle, string LabelStyle) StatusPill(string status) => status switch
    {
        "Completed" or "Paid" or "Available" or "Active" or "LinkedAccount" => ("PillSuccess", "PillLabelSuccess"),
        "Pending" or "InProgress" or "OnHold" or "RunningLate" or "AccountUnknown" => ("PillWarning", "PillLabelWarning"),
        "Cancelled" or "Expired" or "NoShow" or "Unpaid" or "Refunded" or "UnavailableToday" => ("PillDanger", "PillLabelDanger"),
        "ProofSubmitted" or "Confirmed" or "CheckedIn" or "Waived" or "Rescheduled" or "Invited" => ("PillInfo", "PillLabelInfo"),
        _ => ("PillNeutral", "PillLabelNeutral"),
    };

    public static string StatusLabel(string status) => status switch
    {
        "ProofSubmitted" => "Proof submitted",
        "CheckedIn" => "Checked in",
        "InProgress" => "In progress",
        "OnHold" => "On hold",
        "NoShow" => "No-show",
        "RunningLate" => "Running late",
        "UnavailableToday" => "Unavailable today",
        //JJQR - AI (Nov 22, 2026) - patient account states printed as run-together capitals ("NOACCOUNT") //JJQR - END
        "NoAccount" => "No account",
        "AccountUnknown" => "Account unknown",
        "LinkedAccount" => "Portal account",
        _ => status,
    };

    public static View MakeStatusPill(string status)
    {
        var (border, label) = StatusPill(status);
        return new Border
        {
            Style = Style(border),
            Content = Text(StatusLabel(status), label),
        };
    }

    public static View MakeLaneBadge(string? queueNumber)
    {
        var ticket = string.IsNullOrWhiteSpace(queueNumber) ? "—" : queueNumber.Trim();
        var senior = ticket.StartsWith("S-", StringComparison.OrdinalIgnoreCase);
        return new Border
        {
            Style = Style(senior ? "LaneBadgeS" : "LaneBadgeR"),
            Content = Text(ticket, senior ? "LaneLabelS" : "LaneLabelR"),
        };
    }

    public static string RoleSubtitle(UserRole role) => role switch
    {
        UserRole.Doctor => "Doctor workspace",
        UserRole.Staff => "Staff Terminal",
        UserRole.Admin => "Clinic Administration",
        _ => "Clinic Node",
    };
}
// JJQR - END
