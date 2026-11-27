//JJQR - AI (2026-11-27) - why: owner: a "Print as" choice under Diagnosis / Impressions on the medical certificate ("One line" / "Each on its own line" / "Paragraph").
// There is no database column for it, so it is print-time state only: kept in memory per visit for this run of the app, never saved or synced.
// Not chosen yet = read from the diagnosis text (ClinicFormsHtml.DiagnosisLayoutFromText), the same rule as the web.
using System.Collections.Concurrent;

namespace ClinicNode.Core.Printing;

/// <summary>"Print as" for the certificate's diagnosis (web DiagnosisLayout "lines" | "oneLine" | "paragraph").</summary>
public enum DiagnosisLayout { Lines, OneLine, Paragraph }

public static class MedCertPrintLayout
{
    static readonly ConcurrentDictionary<Guid, DiagnosisLayout> Chosen = new();

    /// <summary>The doctor's pick for this visit, or null when she has not picked one.</summary>
    public static DiagnosisLayout? Get(Guid bookingId) => Chosen.TryGetValue(bookingId, out var v) ? v : null;

    /// <summary>Her pick, else what the text reads as.</summary>
    public static DiagnosisLayout For(Guid bookingId, string? text) => Get(bookingId) ?? ClinicFormsHtml.DiagnosisLayoutFromText(text);

    public static void Set(Guid bookingId, DiagnosisLayout layout) => Chosen[bookingId] = layout;
}
//JJQR - END
