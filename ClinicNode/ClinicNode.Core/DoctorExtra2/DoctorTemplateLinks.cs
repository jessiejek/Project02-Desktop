//JJQR - AI (21 Nov 2026) - desktop equivalent of the web deep links /doctor/templates?tab=&field=&id= (Project02-fe lib/templates/urlState.ts, 1:1: parseTab with its aliases and the
// hidden Diagnoses tab falling back to the first visible tab, parseField accepting "plan", "chief_complaint", "ChiefComplaint"...). The desktop route is "doctor-templates?tab=&field=&id=".
using System.Text;

namespace ClinicNode.Core.DoctorExtra;

public static partial class DoctorTemplateRules
{
    private static readonly Dictionary<string, string> TabAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prescriptions"] = "rx", ["prescription"] = "rx", ["rx-templates"] = "rx",
        ["med-certs"] = "certificates", ["med-cert"] = "certificates", ["certs"] = "certificates", ["certificate"] = "certificates",
        ["phrase"] = "phrases", ["favorite"] = "favorites", ["favourites"] = "favorites",
        ["diagnosis"] = "diagnoses",
    };

    /// <summary>web parseTab: a known id or alias; anything else (or the hidden "diagnoses") = the first visible tab ("soap").</summary>
    public static string ParseTab(string? value)
    {
        var t = (value ?? "").Trim().ToLowerInvariant();
        var found = Tabs.Any(x => x.Id == t) ? t : TabAliases.TryGetValue(t, out var a) ? a : Tabs[0].Id;
        return Tabs.Any(x => x.Id == found) ? found : Tabs[0].Id;
    }

    /// <summary>web parseField: "Plan" / "plan" / "chiefComplaint" / "chief_complaint" -> the DB casing, or null.</summary>
    public static string? ParseField(string? value)
    {
        var t = new string((value ?? "").Where(c => c is not ('_' or ' ' or '-')).ToArray()).ToLowerInvariant();
        foreach (var f in PhraseFields) if (f.Value.ToLowerInvariant() == t) return f.Value;
        return null;
    }

    /// <summary>web buildTemplatesUrl, as a Shell route: "doctor-templates?tab=rx&amp;id=...".</summary>
    public static string BuildLink(string tab, string? field = null, string? id = null)
    {
        var sb = new StringBuilder("doctor-templates?tab=").Append(Uri.EscapeDataString(tab));
        if (!string.IsNullOrEmpty(field)) sb.Append("&field=").Append(Uri.EscapeDataString(field));
        if (!string.IsNullOrEmpty(id)) sb.Append("&id=").Append(Uri.EscapeDataString(id));
        return sb.ToString();
    }
}
//JJQR - END
