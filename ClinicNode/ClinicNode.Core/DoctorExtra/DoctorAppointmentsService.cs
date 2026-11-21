//JJQR - AI (Nov 21, 2026) - D-APPT-LIST / D-APPT-DETAIL: the doctor's bookings (Today / History) and one booking with its consultation summary and previous consultations. Read-only.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.DoctorExtra;

public sealed record PreviousConsultation(
    Guid ConsultationId, Guid BookingId, DateOnly Date, string DoctorName, string Summary,
    IReadOnlyList<string> Diagnoses, string RxSummary, DateOnly? FollowUpDate, string? FollowUpNote);

public sealed record AppointmentDetail(
    Guid BookingId, Guid PatientId, string PatientName, string PatientContact, string PatientEmail,
    DateOnly AppointmentDate, string SlotStart, BookingStatus Status, string PaymentLabel, string? QueueNumber,
    decimal TotalFee, decimal AmountDue, string? ChiefComplaint, string? Assessment, string? Plan, bool HasConsultation,
    IReadOnlyList<PreviousConsultation> PreviousConsultations)
{
    public AppointmentState State => DoctorExtraRules.StateOf(Status);
}

public interface IDoctorAppointmentsService
{
    Task<IReadOnlyList<AppointmentRow>> ListAsync(Guid doctorId, AppointmentScope scope, CancellationToken ct = default);
    /// <summary>null when the booking does not exist or belongs to another doctor (web notFound()).</summary>
    Task<AppointmentDetail?> GetAsync(Guid doctorId, Guid bookingId, CancellationToken ct = default);
}

public sealed class DoctorAppointmentsService : IDoctorAppointmentsService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    public DoctorAppointmentsService(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<AppointmentRow>> ListAsync(Guid doctorId, AppointmentScope scope, CancellationToken ct = default)
    {
        var today = ClinicClock.Today;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.Bookings.AsNoTracking().Include(b => b.Patient).Include(b => b.Payment).Where(b => b.DoctorId == doctorId);
        if (scope == AppointmentScope.Today) q = q.Where(b => b.AppointmentDate == today);
        var rows = await q.ToListAsync(ct);
        return DoctorExtraRules.Order(rows.Select(b => new AppointmentRow(
            b.BookingId, DoctorExtraRules.PatientName(b.Patient?.FirstName, b.Patient?.LastName), b.Patient?.PatientCode,
            b.AppointmentDate, b.QueueNumber, b.Status, DoctorExtraRules.ListPaymentLabel(b.Status, b.Payment?.Status),
            DoctorExtraRules.VisitTypeLabel(b.VisitType))), scope);
    }

    public async Task<AppointmentDetail?> GetAsync(Guid doctorId, Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await db.Bookings.AsNoTracking().Include(x => x.Patient).Include(x => x.Payment).SingleOrDefaultAsync(x => x.BookingId == bookingId, ct);
        if (b is null || b.DoctorId != doctorId) return null;
        var consult = await db.Consultations.AsNoTracking().FirstOrDefaultAsync(c => c.BookingId == bookingId, ct);
        var prior = await PreviousAsync(db, b.PatientId, bookingId, ct);
        var slot = b.SlotStartTime.ToString("HH:mm");
        return new AppointmentDetail(
            b.BookingId, b.PatientId,
            b.Patient is { } p ? $"{p.FirstName} {p.LastName}" : "Unknown patient",
            b.Patient?.ContactNumber ?? "", b.Patient?.Email ?? "",
            b.AppointmentDate, slot == "00:00" ? "" : slot, b.Status,
            DoctorExtraRules.DetailPaymentLabel(b.Status, b.Payment?.Status), b.QueueNumber, b.TotalFee, b.AmountDue,
            consult?.ChiefComplaint, consult?.Assessment, consult?.Plan, consult is not null, prior);
    }

    internal static async Task<IReadOnlyList<PreviousConsultation>> PreviousAsync(ClinicAppDbContext db, Guid patientId, Guid? excludeBookingId, CancellationToken ct)
    {
        var consults = await db.Consultations.AsNoTracking().Include(c => c.ConsultationDiagnoses)
            .Where(c => c.PatientId == patientId && (excludeBookingId == null || c.BookingId != excludeBookingId)).ToListAsync(ct);
        if (consults.Count == 0) return [];
        var bookingIds = consults.Select(c => c.BookingId).ToList();
        var consultIds = consults.Select(c => c.ConsultationId).ToList();
        var doctorIds = consults.Select(c => c.DoctorId).Distinct().ToList();
        var dates = await db.Bookings.AsNoTracking().Where(x => bookingIds.Contains(x.BookingId)).ToDictionaryAsync(x => x.BookingId, x => x.AppointmentDate, ct);
        var doctors = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount).Where(d => doctorIds.Contains(d.DoctorId))
            .ToDictionaryAsync(d => d.DoctorId, d => d.StaffAccount?.FullName ?? "—", ct);
        var rx = await db.PrescriptionGroups.AsNoTracking().Include(g => g.LineItems).Where(g => bookingIds.Contains(g.BookingId)).ToListAsync(ct);
        var followUps = await db.FollowUps.AsNoTracking().Where(f => consultIds.Contains(f.ConsultationId)).ToListAsync(ct);
        return consults.Select(c =>
        {
            var date = dates.TryGetValue(c.BookingId, out var d) ? d : ClinicClock.DateOf(c.CreatedAt);
            var summary = string.Join(" · ", new[] { c.ChiefComplaint, c.Assessment }.Where(x => !string.IsNullOrWhiteSpace(x)));
            var names = rx.Where(g => g.BookingId == c.BookingId).SelectMany(g => g.LineItems).Select(i => i.GenericName).ToList();
            var fu = followUps.FirstOrDefault(f => f.ConsultationId == c.ConsultationId);
            return new PreviousConsultation(c.ConsultationId, c.BookingId, date,
                doctors.TryGetValue(c.DoctorId, out var n) ? n : "—",
                summary.Length > 0 ? summary : "—",
                c.ConsultationDiagnoses.Select(x => DoctorExtraRules.DiagnosisLabel(x.Icd10Code, x.CustomDescription)).Where(x => x.Length > 0).ToList(),
                DoctorExtraRules.ShortRxSummary(names), fu?.FollowUpDate, fu?.Instructions);
        }).OrderByDescending(x => x.Date).ToList();
    }
}
//JJQR - END
