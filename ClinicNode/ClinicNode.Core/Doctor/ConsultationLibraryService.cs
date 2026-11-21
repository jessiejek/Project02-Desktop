//JJQR - AI (21 Nov 2026) - local (offline) side of IConsultationLibraryService. Reads the doctor's own rows and, like the backend
// BuiltInTemplateCopier, adds the built-ins from BuiltInTemplateCatalog (20 SOAP templates, phrases, 6 certificate templates,
// 10 diagnoses) that the doctor has not copied/deleted yet. READ-ONLY merge: built-ins are never written here (the server copies them).
// Medicines come from the local catalogue seeded by MedicineCatalogSeeder. New templates/phrases saved offline stay on this computer.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Practice;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Doctor;

public sealed class ConsultationLibraryService : IConsultationLibraryService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IDoctorCatalogService _catalog;
    private readonly IPatientService _patients;

    public ConsultationLibraryService(IDbContextFactory<ClinicAppDbContext> factory, IDoctorCatalogService catalog, IPatientService patients)
    {
        _factory = factory;
        _catalog = catalog;
        _patients = patients;
    }

    /// <summary>Built-in keys this doctor already has (a copy row exists, or a tracking row says it was copied and later deleted).</summary>
    private static async Task<HashSet<string>> TakenKeysAsync(ClinicAppDbContext db, Guid doctorId, string kind, IEnumerable<string?> sourceKeys, CancellationToken ct)
    {
        var tracked = await db.DoctorBuiltinTemplateCopies.AsNoTracking()
            .Where(c => c.DoctorId == doctorId && c.Kind == kind).Select(c => c.BuiltinKey).ToListAsync(ct);
        var set = new HashSet<string>(tracked, StringComparer.OrdinalIgnoreCase);
        foreach (var k in sourceKeys) if (!string.IsNullOrWhiteSpace(k)) set.Add(k!);
        return set;
    }

    public async Task<IReadOnlyList<SoapTemplateItem>> ListSoapTemplatesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var own = await db.SoapTemplates.AsNoTracking().Where(t => t.DoctorId == doctorId && !t.IsSystemTemplate).ToListAsync(ct);
        var taken = await TakenKeysAsync(db, doctorId, BuiltInTemplateKind.Soap, own.Select(o => o.SourceBuiltinKey), ct);
        var titles = own.Select(o => o.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = own.Select(t => new SoapTemplateItem(t.Id, t.Title, false, t.ChiefComplaint, t.Subjective, t.Objective, t.Assessment, t.Plan, t.Category)).ToList();
        list.AddRange(BuiltInTemplateCatalog.SoapTemplates.Where(b => !taken.Contains(b.Key) && !titles.Contains(b.Title))
            .Select(b => new SoapTemplateItem(Guid.Parse(b.Key), b.Title, true, b.ChiefComplaint, b.Subjective, b.Objective, b.Assessment, b.Plan)));
        return list.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<SoapPhraseItem>> ListSoapPhrasesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var own = await db.SoapPhrases.AsNoTracking().Where(p => p.DoctorId == doctorId).ToListAsync(ct);
        var taken = await TakenKeysAsync(db, doctorId, BuiltInTemplateKind.Phrase, own.Select(o => o.SourceBuiltinKey), ct);
        var list = own.OrderBy(p => p.Field).ThenBy(p => p.SortOrder).ThenBy(p => p.Label)
            .Select(p => new SoapPhraseItem(p.Id, p.Field, p.Label, p.Body)).ToList();
        list.AddRange(BuiltInTemplateCatalog.Phrases.Where(b => !taken.Contains(b.Key)
                && !own.Any(o => o.Field == b.Field && string.Equals(o.Label, b.Label, StringComparison.OrdinalIgnoreCase)))
            .Select(b => new SoapPhraseItem(Guid.Parse(b.Key), b.Field, b.Label, b.Body)));
        return list;
    }

    public async Task<IReadOnlyList<CertTemplateItem>> ListCertTemplatesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var own = await db.MedicalCertificateTemplates.AsNoTracking().Where(t => t.DoctorId == doctorId && !t.IsSystemTemplate).ToListAsync(ct);
        var taken = await TakenKeysAsync(db, doctorId, BuiltInTemplateKind.Cert, own.Select(o => o.SourceBuiltinKey), ct);
        var titles = own.Select(o => o.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = own.Select(t => new CertTemplateItem(t.Id, t.Title, false, t.DiagnosisText, t.Recommendations, t.PurposeException)).ToList();
        list.AddRange(BuiltInTemplateCatalog.CertTemplates.Where(b => !taken.Contains(b.Key) && !titles.Contains(b.Title))
            .Select(b => new CertTemplateItem(Guid.Parse(b.Key), b.Title, true, b.DiagnosisText, b.Recommendations, b.PurposeException)));
        return list.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<DiagnosisTemplateItem>> ListDiagnosisTemplatesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var own = await db.DoctorDiagnosisTemplates.AsNoTracking().Where(t => t.DoctorId == doctorId).ToListAsync(ct);
        var taken = await TakenKeysAsync(db, doctorId, BuiltInTemplateKind.Diagnosis, own.Select(o => o.SourceBuiltinKey), ct);
        var labels = own.Select(o => o.Label).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var list = own.Select(t => new DiagnosisTemplateItem(t.Id, t.Label, t.Body, t.Icd10Code)).ToList();
        list.AddRange(BuiltInTemplateCatalog.Diagnoses.Where(b => !taken.Contains(b.Key) && !labels.Contains(b.Label))
            .Select(b => new DiagnosisTemplateItem(Guid.Parse(b.Key), b.Label, b.Body, null)));
        return list.OrderBy(t => t.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public Task<IReadOnlyList<RxTemplateItem>> ListRxTemplatesAsync(Guid doctorId, CancellationToken ct = default) =>
        _catalog.ListRxTemplatesAsync(doctorId, ct);

    public async Task<IReadOnlyList<MedicineHit>> ListMedicinesAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Medicines.AsNoTracking().OrderBy(m => m.GenericName)
            .Select(m => new MedicineHit(m.MedicineId, m.GenericName)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LabTestItem>> ListLabTestsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.LabTestCatalog.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new LabTestItem(t.LabTestId, t.Name, t.IsDefault, t.SortOrder)).ToListAsync(ct);
    }

    public async Task<ClinicSetting> GetClinicSettingsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.ClinicSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, ct) ?? new ClinicSetting();
    }

    public async Task<DoctorCredentials?> GetDoctorCredentialsAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var d = await db.Doctors.AsNoTracking().Include(x => x.StaffAccount).SingleOrDefaultAsync(x => x.DoctorId == doctorId, ct);
        return d is null ? null : new DoctorCredentials(d.StaffAccount?.FullName ?? "", d.LicenseNumber, d.PtrNumber);
    }

    public async Task<PatientAddress?> GetPatientAddressAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var p = await db.Patients.AsNoTracking().Where(x => x.PatientId == patientId)
            .Select(x => new PatientAddress(x.Address, x.City)).SingleOrDefaultAsync(ct);
        return p;
    }

    public Task<IReadOnlyList<PatientAllergy>> ListAllergiesAsync(Guid patientId, CancellationToken ct = default) =>
        _patients.ListAllergiesAsync(patientId, ct);

    public async Task<SoapTemplateItem> SaveSoapTemplateAsync(Guid doctorId, string title, SoapValues v, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var row = new SoapTemplate
        {
            Id = Guid.NewGuid(), DoctorId = doctorId, Title = title.Trim(), IsSystemTemplate = false,
            ChiefComplaint = Nb(v.ChiefComplaint), Subjective = Nb(v.Subjective), Objective = Nb(v.Objective), Assessment = Nb(v.Assessment), Plan = Nb(v.Plan),
            CreatedAt = now, UpdatedAt = now,
        };
        db.SoapTemplates.Add(row);
        await db.SaveChangesAsync(ct);
        return new SoapTemplateItem(row.Id, row.Title, false, row.ChiefComplaint, row.Subjective, row.Objective, row.Assessment, row.Plan);
    }

    public async Task<SoapPhraseItem> SaveSoapPhraseAsync(Guid doctorId, SoapField field, string label, string body, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var row = new SoapPhrase { Id = Guid.NewGuid(), DoctorId = doctorId, Field = field, Label = label.Trim(), Body = body.Trim(), CreatedAt = now, UpdatedAt = now };
        db.SoapPhrases.Add(row);
        await db.SaveChangesAsync(ct);
        return new SoapPhraseItem(row.Id, row.Field, row.Label, row.Body);
    }

    public async Task<CertTemplateItem> SaveCertTemplateAsync(Guid doctorId, string title, string? diagnosisText, string? recommendations, string? purposeException, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var row = new MedicalCertificateTemplate
        {
            Id = Guid.NewGuid(), DoctorId = doctorId, Title = title.Trim(), IsSystemTemplate = false,
            DiagnosisText = Nb(diagnosisText), Recommendations = Nb(recommendations), PurposeException = Nb(purposeException),
            CreatedAt = now, UpdatedAt = now,
        };
        db.MedicalCertificateTemplates.Add(row);
        await db.SaveChangesAsync(ct);
        return new CertTemplateItem(row.Id, row.Title, false, row.DiagnosisText, row.Recommendations, row.PurposeException);
    }

    public Task<IReadOnlyList<PatientAllergy>?> ReplaceAllergiesAsync(Guid patientId, IReadOnlyList<AllergyInput> allergies, CancellationToken ct = default) =>
        _patients.UpdateAllergiesAsync(patientId, allergies, ct);

    /// <summary>Offline: the patient profile is owned by the patients lane, so the address is only stored on the certificate snapshot.</summary>
    public Task<bool> UpdatePatientAddressAsync(Guid patientId, string address, CancellationToken ct = default) => Task.FromResult(false);

    //JJQR - AI (21 Nov 2026) - offline side of the gap-close reads/writes
    public async Task<IReadOnlyList<FavoriteMedicineItem>> ListFavoriteMedicinesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.DoctorFavoriteMedicines.AsNoTracking().Where(f => f.DoctorId == doctorId).ToListAsync(ct);
        return rows.OrderBy(f => f.SortOrder == 0 ? int.MaxValue : f.SortOrder).ThenBy(f => f.GenericName, StringComparer.OrdinalIgnoreCase)
            .Select(f => new FavoriteMedicineItem(f.Id, f.MedicineId == Guid.Empty ? null : f.MedicineId, f.GenericName, f.Dosage, f.Quantity, f.Instruction)).ToList();
    }

    /// <summary>Offline: favorites are a cloud-owned (pull-only) table, so a favorite added offline only lives on this computer.
    /// A free-typed medicine (no catalogue id) is not stored locally (medicine_id would break the local foreign key); it is returned for this visit only.</summary>
    public async Task<FavoriteMedicineItem> AddFavoriteMedicineAsync(Guid doctorId, FavoriteMedicineItem item, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = new DoctorFavoriteMedicine
        {
            Id = Guid.NewGuid(), DoctorId = doctorId, MedicineId = item.MedicineId ?? Guid.Empty, GenericName = item.GenericName.Trim(),
            Dosage = item.Dosage ?? "", Quantity = item.Quantity ?? "", Instruction = Nb(item.Instruction), CreatedAt = DateTimeOffset.UtcNow,
        };
        if (item.MedicineId is { } mid && await db.Medicines.AsNoTracking().AnyAsync(m => m.MedicineId == mid, ct))
        {
            row.SortOrder = (await db.DoctorFavoriteMedicines.Where(f => f.DoctorId == doctorId).MaxAsync(f => (int?)f.SortOrder, ct) ?? 0) + 1;
            db.DoctorFavoriteMedicines.Add(row);
            await db.SaveChangesAsync(ct);
        }
        return new FavoriteMedicineItem(row.Id, item.MedicineId, row.GenericName, row.Dosage, row.Quantity, row.Instruction);
    }

    public async Task<IReadOnlyList<PatientVaccination>> ListPatientVaccinationsAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PatientVaccinations.AsNoTracking().Where(v => v.PatientId == patientId).ToListAsync(ct);
    }

    //JJQR - AI (21 Nov 2026) - ALL-1: banner payment line from the local mirror (offline)
    public async Task<string?> GetBookingPaymentDisplayAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await db.Bookings.AsNoTracking().Where(x => x.BookingId == bookingId)
            .Select(x => new { x.Status, PaymentStatus = x.Payment == null ? (ClinicApp.Domain.Enums.PaymentStatus?)null : x.Payment.Status }).SingleOrDefaultAsync(ct);
        return b is null ? null : ConsultPaymentDisplay.For(b.Status, b.PaymentStatus);
    }
    //JJQR - END

    public async Task<PatientClinicalFlags?> GetPatientClinicalFlagsAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        //JJQR - AI (21 Nov 2026) - + renal fields for the renal cue
        var flag = await db.Patients.AsNoTracking().Where(p => p.PatientId == patientId)
            .Select(p => new { p.IsPregnant, p.LastCreatinineMgDl, p.LastEgfr, p.LastRenalLabsDate, p.LmpDate, p.Gravida, p.Para, p.RhFactor, p.GuardianName, p.GuardianRelationship }).SingleOrDefaultAsync(ct);
        //JJQR - AI (21 Nov 2026) - ALL-1 banner chips from the local mirror (offline)
        return flag is null ? null : new PatientClinicalFlags(flag.IsPregnant == true, flag.LastCreatinineMgDl, flag.LastEgfr, flag.LastRenalLabsDate,
            flag.LmpDate, flag.Gravida, flag.Para, flag.RhFactor, flag.GuardianName, flag.GuardianRelationship);
        //JJQR - END
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - Refill last visit Rx / SOAP specialty default (local SQLite, same rules as the API version)
    public async Task<ConsultPriorVisit?> GetPriorVisitAsync(Guid patientId, Guid currentBookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var prior = await db.Consultations.AsNoTracking()
            .Where(c => c.PatientId == patientId && c.BookingId != currentBookingId)
            .Join(db.Bookings.AsNoTracking(), c => c.BookingId, b => b.BookingId, (c, b) => new { c.ConsultationId, c.BookingId, b.AppointmentDate })
            .OrderByDescending(x => x.AppointmentDate).FirstOrDefaultAsync(ct);
        if (prior is null) return null;
        var group = await db.PrescriptionGroups.AsNoTracking().Where(g => g.BookingId == prior.BookingId)
            .OrderBy(g => g.CreatedAt).Select(g => g.GroupId).FirstOrDefaultAsync(ct);
        List<PrescriptionLineItem> lines = group == Guid.Empty ? [] : await db.PrescriptionLineItems.AsNoTracking()
            .Where(i => i.GroupId == group && i.DeletedAt == null).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var items = lines.Select(i => new ClinicNode.Core.Clinical.RxLine
        {
            MedicineId = i.MedicineId == Guid.Empty ? null : i.MedicineId, GenericName = i.GenericName, Dosage = i.Dosage, Quantity = i.Quantity,
            Instruction = i.Instruction, Timing = i.Timing, MealRelation = i.MealRelation, DurationKind = i.DurationKind, DurationValue = i.DurationValue,
            Indication = i.Indication, Route = i.Route, AsNeeded = i.AsNeeded, IsControlledSubstance = i.IsControlledSubstance,
        }).ToList();
        return new ConsultPriorVisit(prior.ConsultationId, prior.BookingId, prior.AppointmentDate, items);
    }

    public async Task<string?> GetDoctorSpecializationAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Doctors.AsNoTracking().Where(d => d.DoctorId == doctorId).Select(d => d.Specialization).FirstOrDefaultAsync(ct);
    }
    //JJQR - END

    public async Task<IReadOnlyList<AmendmentEntry>> ListAmendmentHistoryAsync(Guid consultationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == AuditEntityType.Consultation && a.EntityId == consultationId).ToListAsync(ct);
        return rows.OrderByDescending(a => a.PerformedAt).Take(100).Select(a => new AmendmentEntry(a.PerformedAt, a.Details)).ToList();
    }
    //JJQR - END

    private static string? Nb(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
//JJQR - END
