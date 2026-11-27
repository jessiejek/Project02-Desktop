//JJQR - AI (2026-11-27) - why: owner: "if each as own line give me lines when i type". In "Each on its own line" mode the Diagnosis / Impressions box numbers the next line by itself:
// Enter after "N) text" adds "N+1) " on the new line; Enter on a blank numbered line removes that marker instead of adding a line; typing the number yourself right after
// the automatic one ("2) 2)") keeps only yours; Enter just before a line that already has a marker adds nothing. Exact port of Project02-fe src/lib/clinical/dx-auto-number.ts
// (same test cases in DxAutoNumberTests / dx-auto-number.test.mjs).
using System.Text.RegularExpressions;

namespace ClinicNode.Core.Doctor;

public sealed record DxEdit(string Text, int Caret);

public static class DxAutoNumber
{
    static readonly Regex Marker = new(@"^([ \t]*)([0-9]{1,2})([.)])[ \t]*", RegexOptions.ECMAScript);
    static readonly Regex StartsWithMarker = new(@"^[ \t]*[0-9]{1,2}[.)]", RegexOptions.ECMAScript);
    static readonly Regex Doubled = new(@"^([ \t]*)[0-9]{1,2}[.)] ([0-9]{1,2}[.)])$", RegexOptions.ECMAScript);

    static bool IsNl(char c) => c == '\n' || c == '\r';

    static int LineStart(string s, int i) { var k = i; while (k > 0 && !IsNl(s[k - 1])) k--; return k; }

    static int LineEnd(string s, int i) { var k = i; while (k < s.Length && !IsNl(s[k])) k++; return k; }

    /// <summary>The text (and caret) after automatic numbering, or null when the edit needs nothing. <paramref name="prev"/> = before the edit, <paramref name="next"/> = after it.</summary>
    public static DxEdit? Edit(string? prev, string? next)
    {
        prev ??= ""; next ??= "";
        if (next.Length != prev.Length + 1) return null; // one typed character only (not paste / delete)
        var i = 0;
        while (i < prev.Length && prev[i] == next[i]) i++;
        if (prev[i..] != next[(i + 1)..]) return null;
        var c = next[i];
        if (IsNl(c))
        {
            if (c == '\n' && i > 0 && prev[i - 1] == '\r') return null; // the "\n" of a Windows "\r\n"
            var ls = LineStart(prev, i);
            var before = prev[ls..i];
            var m = Marker.Match(before);
            if (!m.Success) return null;
            var rest = prev[i..LineEnd(prev, i)];
            if (before[m.Length..].Trim().Length == 0 && rest.Trim().Length == 0)
                return new DxEdit(prev[..ls] + prev[i..], ls); // blank numbered line: Enter takes the marker away (no new line)
            if (StartsWithMarker.IsMatch(rest)) return null; // the next part already has its number
            var n = int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) + 1;
            if (n > 99) return null;
            var marker = m.Groups[1].Value + n + m.Groups[3].Value + " ";
            return new DxEdit(prev[..i] + c + marker + rest.TrimStart(' ', '\t') + prev[(i + rest.Length)..], i + 1 + marker.Length);
        }
        if (c == ')' || c == '.')
        {
            var ls = LineStart(next, i);
            var d = Doubled.Match(next[ls..(i + 1)]);
            if (!d.Success) return null;
            var keep = d.Groups[1].Value + d.Groups[2].Value; // "2) 2)": she typed the number herself after the automatic one; keep hers
            return new DxEdit(next[..ls] + keep + next[(i + 1)..], ls + keep.Length);
        }
        return null;
    }
}
//JJQR - END
