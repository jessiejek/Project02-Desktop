//JJQR - AI (21 Nov 2026) - 1:1 text rules of the web doctor patient header card (Project02-fe app/doctor/patients/[id]/page.tsx lines ~320-344): initials() and "{sex} · {age} yrs · Born {dateOfBirth}".
// The web prints Born as the raw ISO date (yyyy-MM-dd), so does this.
using System.Globalization;

namespace ClinicNode.Core.DoctorExtra;

public static class DoctorPatientCardRules
{
    /// <summary>web initials(): first letter of the first and the last word, upper-cased ("Ana Maria Cruz" -> "AC", one word -> that letter twice only if one letter... same as the web: first + last).</summary>
    public static string Initials(string? fullName)
    {
        var parts = (fullName ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "";
        var first = parts[0][0].ToString();
        var last = parts[^1][0].ToString();
        return (first + last).ToUpperInvariant();
    }

    /// <summary>"Female · 34 yrs · Born 1991-05-02".</summary>
    public static string Subtitle(string sex, int age, DateOnly dateOfBirth) =>
        $"{sex} · {age} yrs · Born {dateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
}
//JJQR - END
