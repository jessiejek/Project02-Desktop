//JJQR - AI (21 Nov 2026) - Api implementation of IGlobalSearchService, the same call the web top-bar search makes: GET /api/search?q=
// so search works against the live clinic records even before this computer's local copy has synced.
using ClinicNode.Api.Http;
using ClinicNode.Core.Practice;
using static ClinicNode.Api.Services.ApiStaff;

namespace ClinicNode.Api.Services;

public sealed class ApiGlobalSearchService(IApiClient api) : IGlobalSearchService
{
    internal sealed record PatientDto(Guid Id, string? PatientCode, string? FirstName, string? LastName);
    internal sealed record DoctorDto(Guid DoctorId, string? FullName, string? Specialization);
    internal sealed record BookingDto(Guid BookingId, string? AppointmentDate, string? Status, string? QueueNumber, string? PatientName);
    internal sealed record ResponseDto(List<PatientDto>? Patients, List<DoctorDto>? Doctors, List<BookingDto>? Bookings);

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim();
        if (q.Length < 2) return [];
        var r = await api.GetAsync<ResponseDto>("/api/search", Q(("q", q)), ct);
        var hits = new List<SearchHit>();
        foreach (var p in r?.Patients ?? [])
            hits.Add(new SearchHit(SearchHitKind.Patient, p.Id, $"{p.FirstName} {p.LastName} · {p.PatientCode}"));
        foreach (var d in r?.Doctors ?? [])
            hits.Add(new SearchHit(SearchHitKind.Doctor, d.DoctorId, $"{d.FullName} · {d.Specialization}"));
        foreach (var b in r?.Bookings ?? [])
            hits.Add(new SearchHit(SearchHitKind.Booking, b.BookingId, $"{b.PatientName} · {b.AppointmentDate} · {b.Status}"));
        return hits;
    }
}
//JJQR - END
