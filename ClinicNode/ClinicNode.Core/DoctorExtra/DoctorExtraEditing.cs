//JJQR - AI (21 Nov 2026) - doctor overflow screens, EDITABLE like the web (online-first). New interfaces so the tested read services
// (IDoctorTemplatesService, IDoctorProfileService, ...) keep their shape. Each one is routed by AddDoctorExtraModule():
//   API (Api/Services/ApiDoctorExtra*.cs) first; this Local copy when the server cannot be reached.
// Local writes:
//   - favorites / Rx sets / prescriptions are cloud-owned on the node (pulled down read-only, SYNC-API-CONTRACT §6),
//     and a local prescription write belongs to the consultation aggregate, so offline they return NeedsServer instead of half-saving;
//   - the bio save queues through IDoctorSelfEditService (SyncOutbox + ISyncBridge) like the rest of the doctor's self-edits.
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Printing;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.DoctorExtra;

public static class DoctorExtraOnline
{
    public const string NeedsServer = "This change needs the clinic server. Check the connection and that you are signed in online, then try again.";
}

// ── prescription lines, favorites and Rx sets as the prescription form reads them ─
/// <summary>One prescription line (template item or prescription line item). MedicineId = Guid.Empty for a free-typed medicine.</summary>
public sealed record TplRxLine(Guid MedicineId, string GenericName, string Dosage, string Quantity, string? Instruction, bool IsControlled,
    string? Route, string? MealRelation, string? Timing, string? DurationKind, int? DurationValue, string? Indication, bool? AsNeeded);
public sealed record TplRxSet(Guid Id, string Title, bool IsSystem, IReadOnlyList<TplRxLine> Lines);
public sealed record TplFavorite(Guid Id, Guid MedicineId, string GenericName, string Dosage, string Quantity, string? Instruction, int SortOrder);
public sealed record TplMedicine(Guid MedicineId, string GenericName);
// ── prescriptions from the chart (web doctor/patients/[id]/prescriptions/*) ─
public sealed record RxGroupDetail(Guid GroupId, Guid PatientId, Guid DoctorId, Guid BookingId, DateTimeOffset CreatedAt, IReadOnlyList<TplRxLine> Lines);

public interface IDoctorPrescriptionsService
{
    Task<RxGroupDetail?> GetGroupAsync(Guid groupId, CancellationToken ct = default);
    /// <summary>PUT /api/prescription-groups/by-booking/{bookingId}: replaces the booking's prescription with these lines.</summary>
    Task<DoctorResult> SaveByBookingAsync(Guid bookingId, Guid patientId, Guid doctorId, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default);
    Task<DoctorResult> DeleteGroupAsync(Guid groupId, CancellationToken ct = default);
    Task<DoctorResult> AddFavoriteAsync(Guid doctorId, TplRxLine line, CancellationToken ct = default);
    Task<DoctorResult> CreateRxSetAsync(Guid doctorId, string title, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default);
    Task<IReadOnlyList<TplFavorite>> ListFavoritesAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<TplRxSet>> ListRxSetsAsync(Guid doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<TplMedicine>> ListMedicinesAsync(CancellationToken ct = default);
    //JJQR - AI (21 Nov 2026) - Rx form parity: the form's own template Edit / Delete (same endpoints as the Templates workspace) and the patient's
    // allergies (NKDA rows dropped) + pregnancy flag for the allergy / pregnancy checks (web consultation passes patientAllergies / patientPregnant).
    Task<DoctorResult> UpdateRxSetAsync(Guid templateId, Guid doctorId, string title, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default);
    Task<DoctorResult> DeleteRxSetAsync(Guid templateId, CancellationToken ct = default);
    Task<RxPatientSafety> GetPatientSafetyAsync(Guid patientId, CancellationToken ct = default);
    //JJQR - END
}

//JJQR - AI (21 Nov 2026)
public sealed record RxPatientSafety(IReadOnlyList<string> Allergies, bool IsPregnant);
//JJQR - END

// ── profile self-edit: bio only (PUT /api/doctors/me {bio}) ────────────────
public interface IDoctorBioService
{
    //JJQR - AI (Nov 22, 2026) - License / PTR / S2 are the doctor's own (null = unchanged) //JJQR - END
    Task<DoctorResult> SaveBioAsync(Guid doctorId, string? bio, string? licenseNumber = null, string? ptrNumber = null, string? s2Number = null, CancellationToken ct = default);
}

// ── printouts ──────────────────────────────────────────────────────────────
public sealed record BookingPrintAvailability(bool HasRx, bool HasLabs);

public interface IDoctorPrintDataService
{
    Task<PatientCardData?> GetPatientCardAsync(Guid patientId, CancellationToken ct = default);
    Task<AppointmentSummaryData?> GetAppointmentSummaryAsync(Guid bookingId, CancellationToken ct = default);
    /// <summary>web lib/consult-print loadBookingPrintAvailability: any Rx line / any lab order with a test name.</summary>
    Task<BookingPrintAvailability> GetPrintAvailabilityAsync(Guid bookingId, CancellationToken ct = default);
    //JJQR - AI (21 Nov 2026) - header for printing the Rx form's current lines (clinic settings, doctor incl. S2, patient)
    Task<RxPrintHeader?> GetRxPrintHeaderAsync(Guid doctorId, Guid patientId, CancellationToken ct = default);
    //JJQR - END
}

// ═══ Local implementations (SQLite copy) ═══════════════════════════════════
internal static class LocalTpl
{
    internal static TplRxLine Line(Guid medicineId, string generic, string dosage, string quantity, string? instruction, bool controlled,
        string? route, string? meal, string? timing, string? durationKind, int? durationValue, string? indication, bool? asNeeded) =>
        new(medicineId, generic, dosage, quantity, instruction, controlled, route, meal, timing, durationKind, durationValue, indication, asNeeded);

    internal static async Task<IReadOnlyList<TplMedicine>> MedicinesAsync(IDbContextFactory<ClinicAppDbContext> factory, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.Medicines.AsNoTracking().ToListAsync(ct)).OrderBy(m => m.GenericName).Select(m => new TplMedicine(m.MedicineId, m.GenericName)).ToList();
    }

    internal static async Task<IReadOnlyList<TplFavorite>> FavoritesAsync(IDbContextFactory<ClinicAppDbContext> factory, Guid doctorId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.DoctorFavoriteMedicines.AsNoTracking().Where(f => f.DoctorId == doctorId).ToListAsync(ct))
            .OrderBy(f => f.SortOrder).ThenBy(f => f.GenericName)
            .Select(f => new TplFavorite(f.Id, f.MedicineId, f.GenericName, f.Dosage, f.Quantity, f.Instruction, f.SortOrder)).ToList();
    }

    internal static async Task<IReadOnlyList<TplRxSet>> RxSetsAsync(IDbContextFactory<ClinicAppDbContext> factory, Guid doctorId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.PrescriptionTemplates.AsNoTracking().Include(t => t.Items).Where(t => t.DoctorId == doctorId).ToListAsync(ct);
        return rows.OrderBy(t => t.Title, StringComparer.CurrentCulture).Select(t => new TplRxSet(t.TemplateId, t.Title, t.IsSystemTemplate,
            t.Items.OrderBy(i => i.CreatedAt).Select(i => Line(i.MedicineId, i.GenericName, i.Dosage, i.Quantity, i.Instruction, i.IsControlledSubstance,
                i.Route, i.MealRelation, i.Timing, i.DurationKind, i.DurationValue, i.Indication, i.AsNeeded)).ToList())).ToList();
    }
}

public sealed class DoctorPrescriptionsService(IDbContextFactory<ClinicAppDbContext> factory) : IDoctorPrescriptionsService
{
    private static Task<DoctorResult> Offline() => Task.FromResult(DoctorResult.Fail(DoctorExtraOnline.NeedsServer));

    public async Task<RxGroupDetail?> GetGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var g = await db.PrescriptionGroups.AsNoTracking().Include(x => x.LineItems).SingleOrDefaultAsync(x => x.GroupId == groupId && x.DeletedAt == null, ct);
        if (g is null) return null;
        return new RxGroupDetail(g.GroupId, g.PatientId, g.DoctorId, g.BookingId, g.CreatedAt,
            g.LineItems.Where(i => i.DeletedAt == null).OrderBy(i => i.CreatedAt).Select(i => LocalTpl.Line(i.MedicineId, i.GenericName, i.Dosage, i.Quantity, i.Instruction,
                i.IsControlledSubstance, i.Route, i.MealRelation, i.Timing, i.DurationKind, i.DurationValue, i.Indication, i.AsNeeded)).ToList());
    }

    public Task<DoctorResult> SaveByBookingAsync(Guid bookingId, Guid patientId, Guid doctorId, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> DeleteGroupAsync(Guid groupId, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> AddFavoriteAsync(Guid doctorId, TplRxLine line, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> CreateRxSetAsync(Guid doctorId, string title, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default) => Offline();
    public Task<IReadOnlyList<TplFavorite>> ListFavoritesAsync(Guid doctorId, CancellationToken ct = default) => LocalTpl.FavoritesAsync(factory, doctorId, ct);
    public Task<IReadOnlyList<TplRxSet>> ListRxSetsAsync(Guid doctorId, CancellationToken ct = default) => LocalTpl.RxSetsAsync(factory, doctorId, ct);
    public Task<IReadOnlyList<TplMedicine>> ListMedicinesAsync(CancellationToken ct = default) => LocalTpl.MedicinesAsync(factory, ct);
    //JJQR - AI (21 Nov 2026)
    public Task<DoctorResult> UpdateRxSetAsync(Guid templateId, Guid doctorId, string title, IReadOnlyList<TplRxLine> lines, CancellationToken ct = default) => Offline();
    public Task<DoctorResult> DeleteRxSetAsync(Guid templateId, CancellationToken ct = default) => Offline();

    public async Task<RxPatientSafety> GetPatientSafetyAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var allergies = (await db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == patientId && !a.IsNkda).ToListAsync(ct))
            .OrderBy(a => a.Allergen, StringComparer.CurrentCulture).Select(a => a.Allergen).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
        var pregnant = await db.Patients.AsNoTracking().Where(p => p.PatientId == patientId).Select(p => p.IsPregnant).FirstOrDefaultAsync(ct);
        return new RxPatientSafety(allergies, pregnant == true);
    }
    //JJQR - END
}

/// <summary>Offline bio save: queue only the bio through the doctor self-edit outbox. Name, specialization and contact are passed back exactly as
/// stored, so the self-edit service sees no change in them and queues nothing else.</summary>
public sealed class DoctorBioService(IDbContextFactory<ClinicAppDbContext> factory, IDoctorSelfEditService selfEdit) : IDoctorBioService
{
    public async Task<DoctorResult> SaveBioAsync(Guid doctorId, string? bio, string? licenseNumber = null, string? ptrNumber = null, string? s2Number = null, CancellationToken ct = default)
    {
        if ((bio ?? "").Trim().Length > 2000) return DoctorResult.Fail("Bio must be 2,000 characters or fewer.");
        await using var db = await factory.CreateDbContextAsync(ct);
        var d = await db.Doctors.AsNoTracking().Include(x => x.StaffAccount).SingleOrDefaultAsync(x => x.DoctorId == doctorId, ct);
        if (d is null) return DoctorResult.Fail("Doctor profile not found.");
        var r = await selfEdit.SaveProfileAsync(doctorId, new DoctorSelfProfileEdit(d.StaffAccount?.FullName, d.Specialization, bio, d.StaffAccount?.ContactNumber), ct);
        if (!r.Ok) return DoctorResult.Fail(r.Error ?? "Profile was not saved.");
        if (licenseNumber is not null || ptrNumber is not null || s2Number is not null)
        {
            await using var w = await factory.CreateDbContextAsync(ct);
            var row = await w.Doctors.SingleOrDefaultAsync(x => x.DoctorId == doctorId, ct);
            if (row is not null)
            {
                static string? B(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
                if (licenseNumber is not null) row.LicenseNumber = B(licenseNumber);
                if (ptrNumber is not null) row.PtrNumber = B(ptrNumber);
                if (s2Number is not null) row.S2Number = B(s2Number);
                await w.SaveChangesAsync(ct);
            }
        }
        return DoctorResult.Ok(doctorId);
    }
}

public sealed class DoctorPrintDataService(IDbContextFactory<ClinicAppDbContext> factory) : IDoctorPrintDataService
{
    //JJQR - AI (21 Nov 2026) - same header the booking print uses (ClinicFormsPrintDocumentFactory ClinicAsync / Doctor / Patient)
    public async Task<RxPrintHeader?> GetRxPrintHeaderAsync(Guid doctorId, Guid patientId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var doctor = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount).SingleOrDefaultAsync(d => d.DoctorId == doctorId, ct);
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == patientId, ct);
        if (doctor is null || patient is null) return null;
        var s = await db.ClinicSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        var clinic = s is null ? new PrintClinic("Grace Medical Clinic", "", null) : new PrintClinic(s.ClinicName, s.Address, s.ContactNumber);
        return new RxPrintHeader(clinic,
            new PrintDoctor(doctor.StaffAccount?.FullName ?? "", doctor.LicenseNumber, doctor.PtrNumber, doctor.S2Number),
            new PrintPatient($"{patient.FirstName} {patient.LastName}".Trim(), patient.PatientCode, ClinicPrintForms.Iso(patient.DateOfBirth), patient.Sex.ToString(), patient.Address));
    }
    //JJQR - END

    /// <summary>Same projection as PatientsController.Card (server side).</summary>
    public async Task<PatientCardData?> GetPatientCardAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var p = await db.Patients.AsNoTracking().Include(x => x.Allergies).SingleOrDefaultAsync(x => x.PatientId == patientId && x.DeletedAt == null, ct);
        if (p is null) return null;
        var clinic = await db.ClinicSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var allergies = p.Allergies.OrderBy(a => a.Allergen).Select(a => a.IsNkda ? "NKDA" : a.Allergen).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
        var hasEmergency = !string.IsNullOrWhiteSpace(p.EmergencyContactName) || !string.IsNullOrWhiteSpace(p.EmergencyContactNumber);
        return new PatientCardData(p.PatientId, p.PatientCode,
            string.Join(" ", new[] { p.FirstName, p.MiddleName, p.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))),
            p.DateOfBirth.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), p.Sex.ToString(), p.BloodType, allergies,
            hasEmergency ? p.EmergencyContactName : null, hasEmergency ? p.EmergencyContactNumber : null, hasEmergency ? p.EmergencyContactRelationship : null,
            clinic?.ClinicName ?? "", clinic?.ContactNumber, DateTimeOffset.UtcNow);
    }

    public async Task<AppointmentSummaryData?> GetAppointmentSummaryAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var b = await db.Bookings.AsNoTracking().Include(x => x.Patient).Include(x => x.Doctor).ThenInclude(d => d.StaffAccount).Include(x => x.Payment)
            .SingleOrDefaultAsync(x => x.BookingId == bookingId && x.DeletedAt == null, ct);
        if (b is null) return null;
        return new AppointmentSummaryData(b.BookingId, b.AppointmentDate, b.Status.ToString(),
            b.Patient is null ? "" : $"{b.Patient.FirstName} {b.Patient.LastName}", b.Patient?.PatientCode ?? "",
            b.Doctor?.StaffAccount?.FullName ?? "", b.Doctor?.Specialization ?? "", b.AmountDue,
            b.Payment?.Status.ToString() ?? "Unpaid", b.Payment?.OrNumber);
    }

    public async Task<BookingPrintAvailability> GetPrintAvailabilityAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var hasRx = await db.PrescriptionLineItems.AsNoTracking()
            .AnyAsync(i => i.DeletedAt == null && db.PrescriptionGroups.Any(g => g.GroupId == i.GroupId && g.BookingId == bookingId && g.DeletedAt == null), ct);
        var consultIds = await db.Consultations.AsNoTracking().Where(c => c.BookingId == bookingId && c.DeletedAt == null).Select(c => c.ConsultationId).ToListAsync(ct);
        List<string> tests = consultIds.Count == 0 ? [] : await db.LabOrders.AsNoTracking().Where(l => consultIds.Contains(l.ConsultationId) && l.DeletedAt == null).Select(l => l.TestName).ToListAsync(ct);
        return new BookingPrintAvailability(hasRx, tests.Any(t => !string.IsNullOrWhiteSpace(t)));
    }
}
//JJQR - END
