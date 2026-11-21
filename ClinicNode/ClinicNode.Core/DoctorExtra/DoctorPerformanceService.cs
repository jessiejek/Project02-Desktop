//JJQR - AI (Nov 21, 2026) - D-PERF query service: a 1:1 port of DoctorPerformanceController's queries against the local SQLite node DB
// (patients, money, trends, 14-day series, top diagnoses, unpaid + follow-up lists). Collected = Paid payments (amount_received, else amount); a Refunded
// payment is never collected; a paid-but-not-yet-completed booking is excluded. Collected / Billed per period agree with the v_doctor_earnings view.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.DoctorExtra;

public sealed class DoctorPerformanceService : IDoctorPerformanceService
{
    public const int SeriesDays = 14;
    public const int ListLimit = 20;
    public const int TopDiagnosisLimit = 5;
    public const int MinConsultSamples = 5;
    /// <summary>A "consult" longer than this is a draft that was left open, not a real visit length.</summary>
    public const int MaxConsultMinutes = 180;

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;

    public DoctorPerformanceService(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    /// <summary>Manila days; week = Monday..today; month = 1st..today. The previous period spans the same number of elapsed days.</summary>
    public static (DoctorPerformancePeriod Current, DoctorPerformancePeriod Previous) Range(PerformancePeriodName name, DateOnly today)
    {
        switch (name)
        {
            case PerformancePeriodName.Week:
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                return (new("week", monday, today), new("previous_week", monday.AddDays(-7), today.AddDays(-7)));
            case PerformancePeriodName.Month:
                var first = new DateOnly(today.Year, today.Month, 1);
                var prevFirst = first.AddMonths(-1);
                var prevLast = first.AddDays(-1);
                var prevTo = new DateOnly(prevFirst.Year, prevFirst.Month, Math.Min(today.Day, prevLast.Day));
                return (new("month", first, today), new("previous_month", prevFirst, prevTo));
            default:
                return (new("today", today, today), new("yesterday", today.AddDays(-1), today.AddDays(-1)));
        }
    }

    public async Task<DoctorPerformanceReport?> GetAsync(Guid doctorId, PerformancePeriodName period, DateOnly? today = null, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (!await db.Doctors.AsNoTracking().AnyAsync(d => d.DoctorId == doctorId, ct)) return null;
        var day = today ?? ClinicClock.Today;
        var (cur, prev) = Range(period, day);
        var current = await StatsAsync(db, doctorId, cur.From, cur.To, ct);
        var previous = await StatsAsync(db, doctorId, prev.From, prev.To, ct);
        var series = await SeriesAsync(db, doctorId, day, ct);
        var top = await TopDiagnosesAsync(db, doctorId, cur.From, cur.To, ct);
        var unpaid = await UnpaidAsync(db, doctorId, ct);
        var followUps = await FollowUpsAsync(db, doctorId, day, ct);
        return new DoctorPerformanceReport(doctorId, "PHP", "Monday", period, day, cur, prev, current, previous, series, top, unpaid, followUps);
    }

    private sealed record VisitRow(Guid BookingId, Guid PatientId, DateOnly Date, VisitType VisitType, decimal TotalFee, decimal AmountDue,
        PaymentStatus? PayStatus, decimal? PayAmount, decimal? PayReceived, decimal? PayRefund);

    private static async Task<List<VisitRow>> CompletedVisitsAsync(ClinicAppDbContext db, Guid doctor, DateOnly from, DateOnly to, CancellationToken ct) =>
        await db.Bookings.AsNoTracking()
            .Where(b => b.DoctorId == doctor && b.Status == BookingStatus.Completed && b.AppointmentDate >= from && b.AppointmentDate <= to)
            .Select(b => new VisitRow(b.BookingId, b.PatientId, b.AppointmentDate, b.VisitType, b.TotalFee, b.AmountDue,
                b.Payment == null ? null : b.Payment.Status, b.Payment == null ? null : b.Payment.Amount,
                b.Payment == null ? null : b.Payment.AmountReceived, b.Payment == null ? null : b.Payment.RefundAmount))
            .ToListAsync(ct);

    private static decimal CollectedOf(VisitRow v) => v.PayStatus == PaymentStatus.Paid ? v.PayReceived ?? v.PayAmount ?? 0m : 0m;

    private static async Task<DoctorPerformanceStats> StatsAsync(ClinicAppDbContext db, Guid doctor, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var visits = await CompletedVisitsAsync(db, doctor, from, to, ct);
        var patients = visits.Select(v => v.PatientId).Distinct().ToList();

        // first-time = no completed visit with this doctor before the period starts
        var returning = patients.Count == 0 ? new HashSet<Guid>() : (await db.Bookings.AsNoTracking()
            .Where(b => b.DoctorId == doctor && b.Status == BookingStatus.Completed && b.AppointmentDate < from && patients.Contains(b.PatientId))
            .Select(b => b.PatientId).Distinct().ToListAsync(ct)).ToHashSet();

        var statuses = await db.Bookings.AsNoTracking()
            .Where(b => b.DoctorId == doctor && b.AppointmentDate >= from && b.AppointmentDate <= to
                && (b.Status == BookingStatus.NoShow || b.Status == BookingStatus.Cancelled || b.Status == BookingStatus.Expired))
            .Select(b => b.Status).ToListAsync(ct);

        var rx = await db.PrescriptionGroups.AsNoTracking()
            .CountAsync(g => g.DoctorId == doctor && g.Booking != null && g.Booking.AppointmentDate >= from && g.Booking.AppointmentDate <= to, ct);
        var certs = await db.MedicalCertificates.AsNoTracking()
            .CountAsync(m => m.DoctorId == doctor && m.IssueDate >= from && m.IssueDate <= to, ct);
        var labs = await db.LabOrders.AsNoTracking()
            .CountAsync(l => l.DoctorId == doctor && db.Consultations.Any(c => c.ConsultationId == l.ConsultationId
                && c.Booking != null && c.Booking.AppointmentDate >= from && c.Booking.AppointmentDate <= to), ct);

        var spans = await db.Consultations.AsNoTracking()
            .Where(c => c.DoctorId == doctor && c.CompletedAt != null && (c.Status == ConsultationStatus.Completed || c.Status == ConsultationStatus.Amended)
                && c.Booking != null && c.Booking.AppointmentDate >= from && c.Booking.AppointmentDate <= to)
            .Select(c => new { c.CreatedAt, c.CompletedAt }).ToListAsync(ct);
        var minutes = spans.Select(s => (s.CompletedAt!.Value - s.CreatedAt).TotalMinutes).Where(m => m > 0 && m <= MaxConsultMinutes).ToList();

        return new DoctorPerformanceStats(
            PatientsSeen: visits.Count,
            UniquePatients: patients.Count,
            NewVisits: visits.Count(v => v.VisitType == VisitType.New),
            FollowUpVisits: visits.Count(v => v.VisitType == VisitType.FollowUp),
            FirstTimePatients: patients.Count(p => !returning.Contains(p)),
            Collected: visits.Sum(CollectedOf),
            Billed: visits.Sum(v => v.TotalFee),
            Waived: visits.Where(v => v.PayStatus == PaymentStatus.Waived).Sum(v => v.TotalFee),
            Refunded: visits.Where(v => v.PayStatus == PaymentStatus.Refunded).Sum(v => v.PayRefund ?? v.PayReceived ?? v.PayAmount ?? 0m),
            UnpaidTotal: visits.Where(v => v.AmountDue > 0).Sum(v => v.AmountDue),
            RxGroups: rx, MedCerts: certs, LabOrders: labs,
            NoShows: statuses.Count(s => s == BookingStatus.NoShow),
            Cancellations: statuses.Count(s => s is BookingStatus.Cancelled or BookingStatus.Expired),
            AvgConsultMinutes: minutes.Count >= MinConsultSamples ? Math.Round(minutes.Average(), 1) : null,
            AvgConsultSamples: minutes.Count);
    }

    /// <summary>Last 14 clinic days ending today, every day present (zero-filled).</summary>
    private static async Task<List<DoctorPerformanceDay>> SeriesAsync(ClinicAppDbContext db, Guid doctor, DateOnly today, CancellationToken ct)
    {
        var start = today.AddDays(-(SeriesDays - 1));
        var visits = await CompletedVisitsAsync(db, doctor, start, today, ct);
        return Enumerable.Range(0, SeriesDays).Select(i =>
        {
            var d = start.AddDays(i);
            var rows = visits.Where(v => v.Date == d).ToList();
            return new DoctorPerformanceDay(d, rows.Count, rows.Sum(CollectedOf), rows.Sum(v => v.TotalFee));
        }).ToList();
    }

    private static async Task<List<DoctorTopDiagnosis>> TopDiagnosesAsync(ClinicAppDbContext db, Guid doctor, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var rows = await db.ConsultationDiagnoses.AsNoTracking()
            .Where(d => d.Type != DiagnosisType.Differential
                && db.Consultations.Any(c => c.ConsultationId == d.ConsultationId && c.DoctorId == doctor
                    && (c.Status == ConsultationStatus.Completed || c.Status == ConsultationStatus.Amended)
                    && c.Booking != null && c.Booking.AppointmentDate >= from && c.Booking.AppointmentDate <= to))
            .Select(d => new { d.ConsultationId, d.Icd10Code, d.CustomDescription }).ToListAsync(ct);

        var codes = rows.Where(r => !string.IsNullOrWhiteSpace(r.Icd10Code)).Select(r => r.Icd10Code!).Distinct().ToList();
        var names = codes.Count == 0 ? new Dictionary<string, string>() :
            await db.Icd10Codes.AsNoTracking().Where(c => codes.Contains(c.Code)).ToDictionaryAsync(c => c.Code, c => c.Description, ct);

        return rows
            .Select(r => new
            {
                r.ConsultationId,
                Code = string.IsNullOrWhiteSpace(r.Icd10Code) ? null : r.Icd10Code!.Trim(),
                Text = (r.CustomDescription ?? "").Trim()
            })
            .Where(r => r.Code is not null || r.Text.Length > 0)
            .Select(r => new { r.ConsultationId, r.Code, Key = r.Code ?? r.Text.ToLowerInvariant(), Label = r.Code is not null ? names.GetValueOrDefault(r.Code, r.Text.Length > 0 ? r.Text : r.Code) : r.Text })
            .DistinctBy(r => (r.ConsultationId, r.Key)) // one visit counts a diagnosis once
            .GroupBy(r => r.Key)
            .Select(g => new DoctorTopDiagnosis(g.First().Code, g.First().Label, g.Count()))
            .OrderByDescending(t => t.Count).ThenBy(t => t.Label, StringComparer.Ordinal)
            .Take(TopDiagnosisLimit).ToList();
    }

    /// <summary>Everything this doctor still has outstanding (completed visits with money due), newest first.</summary>
    private static async Task<DoctorUnpaidSummary> UnpaidAsync(ClinicAppDbContext db, Guid doctor, CancellationToken ct)
    {
        var due = await db.Bookings.AsNoTracking()
            .Where(b => b.DoctorId == doctor && b.Status == BookingStatus.Completed && b.AmountDue > 0)
            .Select(b => new { b.BookingId, b.PatientId, b.AppointmentDate, b.AmountDue, First = b.Patient.FirstName, Last = b.Patient.LastName })
            .ToListAsync(ct);
        var items = due.OrderByDescending(b => b.AppointmentDate).ThenBy(b => b.Last, StringComparer.Ordinal).Take(ListLimit)
            .Select(b => new DoctorUnpaidVisit(b.BookingId, b.PatientId, $"{b.First} {b.Last}".Trim(), b.AppointmentDate, b.AmountDue)).ToList();
        return new DoctorUnpaidSummary(due.Count, due.Sum(b => b.AmountDue), items);
    }

    private static async Task<DoctorFollowUpSummary> FollowUpsAsync(ClinicAppDbContext db, Guid doctor, DateOnly today, CancellationToken ct)
    {
        var until = today.AddDays(7);
        var rows = await db.FollowUps.AsNoTracking()
            .Where(f => f.DoctorId == doctor && f.Status == FollowUpStatus.Pending && f.FollowUpDate >= today && f.FollowUpDate <= until)
            .Select(f => new { f.Id, f.PatientId, f.FollowUpDate, f.Reason, First = db.Patients.Where(p => p.PatientId == f.PatientId).Select(p => p.FirstName).FirstOrDefault(), Last = db.Patients.Where(p => p.PatientId == f.PatientId).Select(p => p.LastName).FirstOrDefault() })
            .ToListAsync(ct);
        var items = rows.OrderBy(f => f.FollowUpDate).ThenBy(f => f.Last, StringComparer.Ordinal).Take(ListLimit)
            .Select(f => new DoctorDueFollowUp(f.Id, f.PatientId, $"{f.First} {f.Last}".Trim(), f.FollowUpDate, f.Reason)).ToList();
        return new DoctorFollowUpSummary(rows.Count, items);
    }
}
//JJQR - END
