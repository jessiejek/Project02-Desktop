// JJQR - AI (Nov 21, 2026) - doctor consultation helpers on the local DB: ICD-10 search, medicine catalogue, SOAP templates/phrases,
// diagnosis templates, lab catalogue, and the patient's past visits (history + last-visit SOAP). Read-only.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Practice;

public sealed record Icd10Hit(string Code, string Description);
public sealed record MedicineHit(Guid MedicineId, string GenericName);
public sealed record SoapTemplateItem(Guid Id, string Title, bool IsSystem, string? ChiefComplaint, string? Subjective, string? Objective, string? Assessment, string? Plan);
public sealed record SoapPhraseItem(Guid Id, SoapField Field, string Label, string Body);
public sealed record DiagnosisTemplateItem(Guid Id, string Label, string Body, string? Icd10Code);
public sealed record RxTemplateItem(Guid Id, string Title, bool IsSystem, IReadOnlyList<ClinicNode.Core.Clinical.RxLine> Lines);
public sealed record PastVisit(
    Guid BookingId, DateOnly Date, string Status, string? ChiefComplaint, string? Subjective, string? Objective, string? Assessment, string? Plan,
    string? DoctorNotes, string Diagnoses, string Vitals, string Prescriptions, string Labs);

public interface IDoctorCatalogService
{
    Task<IReadOnlyList<Icd10Hit>> SearchIcd10Async(string? query, int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<MedicineHit>> SearchMedicinesAsync(string? query, int take = 20, CancellationToken ct = default);
    Task<IReadOnlyList<SoapTemplateItem>> ListSoapTemplatesAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<SoapPhraseItem>> ListSoapPhrasesAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<DiagnosisTemplateItem>> ListDiagnosisTemplatesAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<RxTemplateItem>> ListRxTemplatesAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListLabTestNamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PastVisit>> GetHistoryAsync(Guid patientId, Guid? excludeBookingId = null, int take = 30, CancellationToken ct = default);
}

public sealed class DoctorCatalogService : IDoctorCatalogService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    public DoctorCatalogService(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<Icd10Hit>> SearchIcd10Async(string? query, int take = 20, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim().ToLower();
        if (q.Length < 2) return [];
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Icd10Codes.AsNoTracking()
            .Where(c => c.IsLeaf && (c.Code.ToLower().StartsWith(q) || c.Description.ToLower().Contains(q)))
            .OrderBy(c => c.Code).Take(take)
            .Select(c => new Icd10Hit(c.Code, c.Description)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<MedicineHit>> SearchMedicinesAsync(string? query, int take = 20, CancellationToken ct = default)
    {
        var q = (query ?? "").Trim().ToLower();
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = db.Medicines.AsNoTracking().AsQueryable();
        if (q.Length > 0) rows = rows.Where(m => m.GenericName.ToLower().Contains(q));
        return await rows.OrderBy(m => m.GenericName).Take(take).Select(m => new MedicineHit(m.MedicineId, m.GenericName)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SoapTemplateItem>> ListSoapTemplatesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SoapTemplates.AsNoTracking().Where(t => t.DoctorId == doctorId || t.IsSystemTemplate)
            .OrderBy(t => t.Title)
            .Select(t => new SoapTemplateItem(t.Id, t.Title, t.IsSystemTemplate, t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SoapPhraseItem>> ListSoapPhrasesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.SoapPhrases.AsNoTracking().Where(p => p.DoctorId == doctorId)
            .OrderBy(p => p.Field).ThenBy(p => p.SortOrder).ThenBy(p => p.Label)
            .Select(p => new SoapPhraseItem(p.Id, p.Field, p.Label, p.Body)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DiagnosisTemplateItem>> ListDiagnosisTemplatesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DoctorDiagnosisTemplates.AsNoTracking().Where(t => t.DoctorId == doctorId)
            .OrderBy(t => t.Label).Select(t => new DiagnosisTemplateItem(t.Id, t.Label, t.Body, t.Icd10Code)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RxTemplateItem>> ListRxTemplatesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var templates = await db.PrescriptionTemplates.AsNoTracking().Include(t => t.Items)
            .Where(t => t.DoctorId == doctorId || t.IsSystemTemplate).OrderBy(t => t.Title).ToListAsync(ct);
        return templates.Select(t => new RxTemplateItem(t.TemplateId, t.Title, t.IsSystemTemplate,
            t.Items.OrderBy(i => i.GenericName).Select(i => new ClinicNode.Core.Clinical.RxLine
            {
                MedicineId = i.MedicineId, GenericName = i.GenericName, Dosage = i.Dosage, Quantity = i.Quantity, Instruction = i.Instruction,
                Timing = i.Timing, MealRelation = i.MealRelation, DurationKind = i.DurationKind, DurationValue = i.DurationValue,
                Indication = i.Indication, Route = i.Route, AsNeeded = i.AsNeeded, IsControlledSubstance = i.IsControlledSubstance,
            }).ToList())).ToList();
    }

    public async Task<IReadOnlyList<string>> ListLabTestNamesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.LabTestCatalog.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name).Select(t => t.Name).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PastVisit>> GetHistoryAsync(Guid patientId, Guid? excludeBookingId = null, int take = 30, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var consults = await db.Consultations.AsNoTracking().Include(c => c.ConsultationDiagnoses)
            .Where(c => c.PatientId == patientId && (excludeBookingId == null || c.BookingId != excludeBookingId))
            .ToListAsync(ct);
        if (consults.Count == 0) return [];
        var bookingIds = consults.Select(c => c.BookingId).ToList();
        var bookings = await db.Bookings.AsNoTracking().Where(b => bookingIds.Contains(b.BookingId)).ToDictionaryAsync(b => b.BookingId, ct);
        var templates = await db.VitalFieldTemplates.AsNoTracking().ToDictionaryAsync(t => t.TemplateId, ct);
        var readings = await db.PatientVitalReadings.AsNoTracking().Where(v => bookingIds.Contains(v.BookingId)).ToListAsync(ct);
        var groups = await db.PrescriptionGroups.AsNoTracking().Include(g => g.LineItems).Where(g => bookingIds.Contains(g.BookingId)).ToListAsync(ct);
        var consultIds = consults.Select(c => c.ConsultationId).ToList();
        var labs = await db.LabOrders.AsNoTracking().Where(l => consultIds.Contains(l.ConsultationId)).ToListAsync(ct);

        return consults
            .Select(c =>
            {
                bookings.TryGetValue(c.BookingId, out var b);
                var vit = readings.Where(v => v.BookingId == c.BookingId && !string.IsNullOrWhiteSpace(v.Value))
                    .Select(v => templates.TryGetValue(v.TemplateId, out var t) ? $"{t.Description} {v.Value}{(string.IsNullOrEmpty(t.Unit) ? "" : " " + t.Unit)}" : v.Value);
                var rx = groups.Where(g => g.BookingId == c.BookingId).SelectMany(g => g.LineItems)
                    .Select(i => $"{i.GenericName} {i.Dosage} #{i.Quantity}");
                return new PastVisit(
                    c.BookingId, b?.AppointmentDate ?? DateOnly.FromDateTime(c.CreatedAt.UtcDateTime), c.Status.ToString(),
                    c.ChiefComplaint, c.Subjective, c.Objective, c.Assessment, c.Plan, c.DoctorNotes,
                    string.Join("; ", c.ConsultationDiagnoses.Select(d => string.IsNullOrWhiteSpace(d.Icd10Code) ? d.CustomDescription : $"{d.Icd10Code} {d.CustomDescription}".Trim())),
                    string.Join(", ", vit), string.Join("; ", rx),
                    string.Join(", ", labs.Where(l => l.ConsultationId == c.ConsultationId).Select(l => l.TestName)));
            })
            .OrderByDescending(v => v.Date).Take(take).ToList();
    }
}
// JJQR - END
