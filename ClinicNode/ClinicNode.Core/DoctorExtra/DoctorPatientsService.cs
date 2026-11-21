//JJQR - AI (Nov 21, 2026) - D-PATIENTS / D-CHART: the doctor's patients (latest appointment each) and one chart (Timeline, Appointments, Consultations, Vitals, Prescriptions, Lab Results, Documents, Vaccinations). Read-only.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.DoctorExtra;

public sealed record DoctorPatientRow(Guid PatientId, string FullName, string PatientCode, DateOnly LatestVisitDate, Guid LatestBookingId);

public sealed record ChartPatient(Guid PatientId, string FullName, string PatientCode, SexType Sex, DateOnly DateOfBirth, int Age, string? ContactNumber);
public sealed record ChartBooking(Guid BookingId, DateOnly AppointmentDate, IReadOnlyList<string> ServiceNames, BookingStatus Status, string? QueueNumber);
public sealed record ChartConsultation(
    Guid ConsultationId, Guid BookingId, DateOnly AppointmentDate, ConsultationStatus Status, string? ChiefComplaint,
    string? Subjective, string? Objective, string? Assessment, string? Plan, IReadOnlyList<string> Diagnoses);
public sealed record ChartVitalTemplate(Guid TemplateId, string Description, string Unit, bool IsDefault);
/// <summary>One column of the vitals table: a visit date and its values by template id.</summary>
public sealed record ChartVitalVisit(Guid BookingId, DateOnly Date, IReadOnlyDictionary<Guid, string> Values);
public sealed record ChartRxLine(string GenericName, string Dosage, string Quantity, string? Instruction);
public sealed record ChartRxGroup(Guid GroupId, Guid BookingId, DateTimeOffset CreatedAt, IReadOnlyList<ChartRxLine> Lines);
public sealed record ChartFile(Guid Id, string Title, DateTimeOffset UploadedAt);
public sealed record ChartVaccination(Guid Id, string VaccineName, short? DoseNumber, DateOnly? Date, VaccinationStatus Status);

public sealed record PatientChart(
    ChartPatient Patient,
    IReadOnlyList<ChartBooking> Bookings,
    IReadOnlyList<ChartConsultation> Consultations,
    IReadOnlyList<ChartVitalTemplate> VitalTemplates,
    IReadOnlyList<ChartVitalVisit> VitalVisits,
    IReadOnlyList<ChartRxGroup> Prescriptions,
    IReadOnlyList<ChartFile> LabResults,
    IReadOnlyList<ChartFile> Documents,
    IReadOnlyList<ChartVaccination> Vaccinations);

public static class ChartTabs
{
    public static readonly (string Id, string Label)[] All =
    [
        ("timeline", "Timeline"), ("appointments", "Appointments"), ("consultations", "Consultations"), ("vitals", "Vitals"),
        ("prescriptions", "Prescriptions"), ("labs", "Lab Results"), ("documents", "Documents"), ("vaccinations", "Vaccinations"),
    ];
}

public interface IDoctorPatientsService
{
    Task<IReadOnlyList<DoctorPatientRow>> ListAsync(Guid doctorId, CancellationToken ct = default);
    /// <summary>Web: name + patient code contains the search (case-insensitive).</summary>
    IReadOnlyList<DoctorPatientRow> Filter(IEnumerable<DoctorPatientRow> rows, string? search);
    Task<PatientChart?> GetChartAsync(Guid doctorId, Guid patientId, CancellationToken ct = default);
}

public sealed class DoctorPatientsService : IDoctorPatientsService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    public DoctorPatientsService(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<DoctorPatientRow>> ListAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var bookings = await db.Bookings.AsNoTracking().Include(b => b.Patient).Where(b => b.DoctorId == doctorId).ToListAsync(ct);
        // Newest appointment first (then newest created), one row per patient — doctor/patients/page.tsx.
        var byPatient = new Dictionary<Guid, DoctorPatientRow>();
        foreach (var b in bookings.OrderByDescending(b => b.AppointmentDate).ThenByDescending(b => b.CreatedAt))
        {
            if (b.Patient is null || byPatient.ContainsKey(b.PatientId)) continue;
            byPatient[b.PatientId] = new DoctorPatientRow(b.PatientId, DoctorExtraRules.PatientName(b.Patient.FirstName, b.Patient.LastName),
                b.Patient.PatientCode ?? "", b.AppointmentDate, b.BookingId);
        }
        return byPatient.Values.ToList();
    }

    public IReadOnlyList<DoctorPatientRow> Filter(IEnumerable<DoctorPatientRow> rows, string? search)
    {
        var q = (search ?? "").Trim().ToLowerInvariant();
        return rows.Where(r => $"{r.FullName} {r.PatientCode}".ToLowerInvariant().Contains(q)).ToList();
    }

    public async Task<PatientChart?> GetChartAsync(Guid doctorId, Guid patientId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var p = await db.Patients.AsNoTracking().SingleOrDefaultAsync(x => x.PatientId == patientId, ct);
        if (p is null) return null;
        var today = ClinicClock.Today;

        // Bookings, consultations and prescriptions are this doctor's (web passes doctorId); vitals, labs, documents, vaccinations are the patient's.
        var bookings = await db.Bookings.AsNoTracking().Where(b => b.PatientId == patientId && b.DoctorId == doctorId).ToListAsync(ct);
        var bookingIds = bookings.Select(b => b.BookingId).ToList();
        var services = await db.BookingServices.AsNoTracking().Include(s => s.Service).Where(s => bookingIds.Contains(s.BookingId)).ToListAsync(ct);
        var chartBookings = bookings.OrderByDescending(b => b.AppointmentDate).ThenByDescending(b => b.CreatedAt)
            .Select(b => new ChartBooking(b.BookingId, b.AppointmentDate,
                services.Where(s => s.BookingId == b.BookingId).Select(s => s.Service?.Name ?? "").Where(n => n.Length > 0).ToList(),
                b.Status, b.QueueNumber)).ToList();

        var allDates = await db.Bookings.AsNoTracking().Where(b => b.PatientId == patientId).ToDictionaryAsync(b => b.BookingId, b => b.AppointmentDate, ct);
        var consults = await db.Consultations.AsNoTracking().Include(c => c.ConsultationDiagnoses)
            .Where(c => c.PatientId == patientId && c.DoctorId == doctorId).ToListAsync(ct);
        var chartConsults = consults.Select(c => new ChartConsultation(c.ConsultationId, c.BookingId,
                allDates.TryGetValue(c.BookingId, out var d) ? d : ClinicClock.DateOf(c.CreatedAt), c.Status,
                c.ChiefComplaint, c.Subjective, c.Objective, c.Assessment, c.Plan,
                c.ConsultationDiagnoses.Select(x => x.CustomDescription ?? "").Where(x => x.Length > 0).ToList()))
            .OrderByDescending(c => c.AppointmentDate).ToList();

        var templates = (await db.VitalFieldTemplates.AsNoTracking().ToListAsync(ct)).OrderBy(t => t.CreatedAt).ToList(); // SQLite: no ORDER BY DateTimeOffset
        var readings = await db.PatientVitalReadings.AsNoTracking().Where(v => v.PatientId == patientId).ToListAsync(ct);
        var visits = readings.GroupBy(r => r.BookingId)
            .Where(g => allDates.ContainsKey(g.Key))
            .Select(g => new ChartVitalVisit(g.Key, allDates[g.Key],
                g.GroupBy(r => r.TemplateId).ToDictionary(x => x.Key, x => x.OrderByDescending(r => r.UpdatedAt).First().Value)))
            .OrderByDescending(v => v.Date).ToList();

        var groups = await db.PrescriptionGroups.AsNoTracking().Include(g => g.LineItems)
            .Where(g => g.PatientId == patientId && g.DoctorId == doctorId).ToListAsync(ct);
        var rx = groups.OrderByDescending(g => g.CreatedAt).Select(g => new ChartRxGroup(g.GroupId, g.BookingId, g.CreatedAt,
            g.LineItems.Select(i => new ChartRxLine(i.GenericName, i.Dosage, i.Quantity, i.Instruction)).ToList())).ToList();

        var labs = await db.PatientLabResults.AsNoTracking().Where(l => l.PatientId == patientId).ToListAsync(ct);
        var docs = await db.PatientDocuments.AsNoTracking().Where(d => d.PatientId == patientId).ToListAsync(ct);
        var vax = await db.PatientVaccinations.AsNoTracking().Where(v => v.PatientId == patientId && v.DeletedAt == null).ToListAsync(ct);

        return new PatientChart(
            new ChartPatient(p.PatientId, $"{p.FirstName} {p.LastName}".Trim(), p.PatientCode, p.Sex, p.DateOfBirth, DoctorExtraRules.AgeOn(p.DateOfBirth, today), p.ContactNumber),
            chartBookings, chartConsults,
            templates.Select(t => new ChartVitalTemplate(t.TemplateId, t.Description, t.Unit, t.IsDefault)).ToList(),
            visits, rx,
            labs.OrderByDescending(l => l.UploadedAt).Select(l => new ChartFile(l.Id, string.IsNullOrWhiteSpace(l.ResultTitle) ? "Lab result" : l.ResultTitle!, l.UploadedAt)).ToList(),
            docs.OrderByDescending(d => d.UploadedAt).Select(d => new ChartFile(d.Id, string.IsNullOrWhiteSpace(d.Title) ? d.FileName : d.Title!, d.UploadedAt)).ToList(),
            vax.OrderByDescending(v => v.AdministeredDate ?? v.NextDoseDate).Select(v => new ChartVaccination(v.Id, v.VaccineName, v.DoseNumber, v.AdministeredDate ?? v.NextDoseDate, v.Status)).ToList());
    }
}
//JJQR - END
