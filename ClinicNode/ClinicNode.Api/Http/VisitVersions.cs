//JJQR - AI (Nov 22, 2026) - stale-form protection for a visit (the C# twin of Project02-fe lib/api/client.ts). While a visit is open on this computer, the server's "visit version"
// (X-Visit-Version) is remembered and sent with every save of that visit (consultation, vitals, prescriptions, labs, follow-up, diagnoses). If the visit was changed on another
// computer or window the server answers 409 "visit_changed" and nothing is overwritten. Saves of one visit run one at a time so two of our own saves cannot trip the check.
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ClinicNode.Api.Http;

public static partial class VisitVersions
{
    public const string VersionHeader = "X-Visit-Version";
    public const string BookingHeader = "X-Visit-Booking";
    public const string ConsultationHeader = "X-Visit-Consultation";

    internal sealed class Session
    {
        public string? Version;
        public int Generation;
        public readonly SemaphoreSlim Gate = new(1, 1);
    }

    private static readonly ConcurrentDictionary<Guid, Session> Sessions = new();
    private static readonly ConcurrentDictionary<Guid, Guid> ConsultationToBooking = new();
    private static int _generation;

    /// <summary>Call when a visit opens (a fresh read of it follows). Returns a token for <see cref="End"/>.</summary>
    public static int Begin(Guid bookingId)
    {
        var s = new Session { Generation = Interlocked.Increment(ref _generation) };
        Sessions[bookingId] = s;
        return s.Generation;
    }

    public static int GenerationOf(Guid bookingId) => Sessions.TryGetValue(bookingId, out var s) ? s.Generation : 0;

    /// <summary>Call when the visit page closes. Does nothing if the visit was opened again meanwhile.</summary>
    public static void End(Guid bookingId, int generation)
    {
        if (generation != 0 && Sessions.TryGetValue(bookingId, out var s) && s.Generation == generation) Sessions.TryRemove(bookingId, out _);
    }

    [GeneratedRegex(@"^/api/(?:consultations|vitals|prescription-groups|lab-orders)/by-booking/(?<id>[0-9a-fA-F-]{36})/?$")]
    private static partial Regex ByBooking();
    [GeneratedRegex(@"^/api/consultations/(?<id>[0-9a-fA-F-]{36})/diagnoses/?$")]
    private static partial Regex Diagnoses();
    [GeneratedRegex(@"^/api/(?:lab-orders|follow-ups)/by-consultation/(?<id>[0-9a-fA-F-]{36})/?$")]
    private static partial Regex ByConsultation();

    internal static (Guid BookingId, Session Session)? For(string path, IReadOnlyDictionary<string, string?>? query)
    {
        if (Sessions.IsEmpty) return null;
        var bare = path.Split('?')[0];
        if (bare.StartsWith("http", StringComparison.OrdinalIgnoreCase)) bare = new Uri(bare).AbsolutePath;
        Guid? booking = null;
        if (ByBooking().Match(bare) is { Success: true } m1 && Guid.TryParse(m1.Groups["id"].Value, out var b1)) booking = b1;
        else if ((Diagnoses().Match(bare) is { Success: true } m2 ? m2 : ByConsultation().Match(bare)) is { Success: true } m3
                 && Guid.TryParse(m3.Groups["id"].Value, out var c3) && ConsultationToBooking.TryGetValue(c3, out var bc)) booking = bc;
        else if (bare is "/api/vitals" or "/api/prescription-groups" or "/api/consultations"
                 && query is not null && query.TryGetValue("bookingId", out var qb) && Guid.TryParse(qb, out var b4)) booking = b4;
        return booking is { } id && Sessions.TryGetValue(id, out var s) ? (id, s) : null;
    }

    internal static void Remember(Guid? sessionBooking, Session? session, HttpMethod method, bool ok, int status, string? version, string? bookingHeader, string? consultationHeader)
    {
        if (Guid.TryParse(consultationHeader, out var cons) && Guid.TryParse(bookingHeader, out var bk)) ConsultationToBooking[cons] = bk;
        if (string.IsNullOrEmpty(version)) return;
        var isWrite = method == HttpMethod.Put || method == HttpMethod.Delete;
        if (session is not null)
        {
            if (isWrite && ok) session.Version = version;                                           // our own save: the visit is now exactly what we saved
            else if (!isWrite && session.Version is null && (ok || status == 404)) session.Version = version; // first read of this visit
            // a refused (409) save keeps the old version, so nothing can be saved until the visit is reloaded
        }
        else if (isWrite && ok && Guid.TryParse(bookingHeader, out var other) && Sessions.TryGetValue(other, out var s2))
            s2.Version = version; // a save of this visit through a route we could not map: keep our version in step with it
    }
}
//JJQR - END
