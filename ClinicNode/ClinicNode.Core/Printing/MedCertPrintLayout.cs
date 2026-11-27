//JJQR - AI (2026-11-27) - why: owner: a "Print as" choice under Diagnosis / Impressions on the medical certificate ("One line" / "Each on its own line", default each on its own line).
// There is no database column for it, so it is print-time state only: kept in memory per visit for this run of the app, never saved or synced.
using System.Collections.Concurrent;

namespace ClinicNode.Core.Printing;

public static class MedCertPrintLayout
{
    static readonly ConcurrentDictionary<Guid, bool> OneLine = new();

    public static bool IsOneLine(Guid bookingId) => OneLine.TryGetValue(bookingId, out var v) && v;

    public static void Set(Guid bookingId, bool oneLine)
    {
        if (oneLine) OneLine[bookingId] = true;
        else OneLine.TryRemove(bookingId, out _);
    }
}
//JJQR - END
