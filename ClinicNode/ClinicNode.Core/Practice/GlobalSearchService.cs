// JJQR - AI (Nov 21, 2026) - top bar global search (web lib/data/search.ts): patients, doctors, bookings; 2+ characters, a few hits per kind.
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Practice;

public enum SearchHitKind { Patient, Doctor, Booking }

public sealed record SearchHit(SearchHitKind Kind, Guid Id, string Label)
{
    public string KindLabel => Kind.ToString().ToLowerInvariant();
}

public interface IGlobalSearchService
{
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct = default);
}

public sealed class GlobalSearchService : IGlobalSearchService
{
    private const int PerKind = 5;
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    public GlobalSearchService(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim();
        if (q.Length < 2) return [];
        var like = $"%{q.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        await using var db = await _factory.CreateDbContextAsync(ct);

        var patients = await db.Patients.AsNoTracking()
            .Where(p => EF.Functions.Like(p.FirstName + " " + p.LastName, like, "\\") || EF.Functions.Like(p.PatientCode, like, "\\"))
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName).Take(PerKind).ToListAsync(ct);

        var doctors = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount)
            .Where(d => EF.Functions.Like(d.StaffAccount!.FullName, like, "\\"))
            .OrderBy(d => d.StaffAccount!.FullName).Take(PerKind).ToListAsync(ct);

        var bookings = await db.Bookings.AsNoTracking().Include(b => b.Patient)
            .Where(b => EF.Functions.Like(b.Patient.FirstName + " " + b.Patient.LastName, like, "\\") || EF.Functions.Like(b.QueueNumber ?? "", like, "\\"))
            .OrderByDescending(b => b.AppointmentDate).ThenByDescending(b => b.CreatedAt).Take(PerKind).ToListAsync(ct);

        var hits = new List<SearchHit>();
        hits.AddRange(patients.Select(p => new SearchHit(SearchHitKind.Patient, p.PatientId, $"{p.FirstName} {p.LastName} · {p.PatientCode}")));
        hits.AddRange(doctors.Select(d => new SearchHit(SearchHitKind.Doctor, d.DoctorId, $"{d.StaffAccount!.FullName} · {d.Specialization}")));
        hits.AddRange(bookings.Select(b => new SearchHit(SearchHitKind.Booking, b.BookingId, $"{b.Patient.FirstName} {b.Patient.LastName} · {b.AppointmentDate:yyyy-MM-dd} · {b.Status}")));
        return hits;
    }
}
// JJQR - END
