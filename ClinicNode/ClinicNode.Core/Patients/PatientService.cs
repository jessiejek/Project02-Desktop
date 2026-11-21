// JJQR - AI (Nov 21, 2026) - look up or create patients on the local node (client-supplied ids).
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Events;
using ClinicNode.Data;
//JJQR - AI (Nov 21, 2026)
using ClinicNode.Core.Sync;
//JJQR - END
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Patients;

public sealed record PatientListItem(Guid PatientId, string PatientCode, string FirstName, string LastName, DateOnly DateOfBirth, string? ContactNumber);

public sealed record NewPatientRequest(
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    SexType Sex,
    string? MiddleName = null,
    string? ContactNumber = null,
    string? Address = null,
    string? DiscountCategory = null);

public interface IPatientService
{
    Task<IReadOnlyList<PatientListItem>> SearchByNameAsync(string query, int take = 25, CancellationToken ct = default);
    Task<Patient?> GetAsync(Guid patientId, CancellationToken ct = default);
    Task<Patient> CreateAsync(NewPatientRequest request, CancellationToken ct = default);
    //JJQR - AI (Nov 21, 2026) - queues an "update" outbox op (item 8)
    Task<Patient?> UpdateAsync(Guid patientId, PatientUpdate update, CancellationToken ct = default);
    //JJQR - END
    //JJQR - AI (Nov 21, 2026) - contract v1.1: the allergy list travels with the patient document
    /// <summary>Replaces the patient's allergy list (full list; empty clears it). A real allergen removes the NKDA row.
    /// Queues a patient op only when something changed. Returns the stored list, or null if the patient is unknown.</summary>
    Task<IReadOnlyList<PatientAllergy>?> UpdateAllergiesAsync(Guid patientId, IReadOnlyList<AllergyInput> allergies, CancellationToken ct = default);
    /// <summary>The stored allergy list (empty if none or the patient is unknown).</summary>
    Task<IReadOnlyList<PatientAllergy>> ListAllergiesAsync(Guid patientId, CancellationToken ct = default);
    //JJQR - END
}

//JJQR - AI (Nov 21, 2026)
/// <summary>Demographic edit; null = leave unchanged.</summary>
public sealed record PatientUpdate(
    string? FirstName = null, string? MiddleName = null, string? LastName = null, DateOnly? DateOfBirth = null,
    SexType? Sex = null, string? ContactNumber = null, string? Address = null);
//JJQR - END

//JJQR - AI (Nov 21, 2026)
/// <summary>One allergy line; <c>IsNkda</c> = "no known drug allergies" (allergen text ignored).</summary>
public sealed record AllergyInput(string Allergen, string? Reaction = null, string? Severity = null, bool IsNkda = false, Guid? Id = null);
//JJQR - END

public sealed class PatientService : IPatientService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IPatientCodeAllocator _codes;
    private readonly IClinicEventBus _events;
    //JJQR - AI (Nov 21, 2026)
    private readonly ISyncBridge _sync;
    //JJQR - END

    public PatientService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IPatientCodeAllocator codes, IClinicEventBus events, ISyncBridge? sync = null)
    {
        //JJQR - AI (Nov 21, 2026)
        _sync = sync ?? new NoOpSyncBridge();
        //JJQR - END
        _factory = factory;
        _outbox = outbox;
        _codes = codes;
        _events = events;
    }

    public async Task<IReadOnlyList<PatientListItem>> SearchByNameAsync(string query, int take = 25, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = (query ?? "").Trim().ToLower();
        var rows = db.Patients.AsQueryable();
        if (q.Length > 0)
        {
            rows = rows.Where(p =>
                p.LastName.ToLower().Contains(q)
                || p.FirstName.ToLower().Contains(q)
                || (p.FirstName + " " + p.LastName).ToLower().Contains(q)
                || p.PatientCode.ToLower().Contains(q));
        }
        return await rows
            .OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
            .Take(take)
            .Select(p => new PatientListItem(p.PatientId, p.PatientCode, p.FirstName, p.LastName, p.DateOfBirth, p.ContactNumber))
            .ToListAsync(ct);
    }

    public async Task<Patient?> GetAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Patients.SingleOrDefaultAsync(p => p.PatientId == patientId, ct);
    }

    public async Task<Patient> CreateAsync(NewPatientRequest request, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var patient = new Patient
        {
            PatientId = Guid.NewGuid(),
            PatientCode = await _codes.NextAsync(db, ct),
            FirstName = request.FirstName.Trim(),
            MiddleName = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName.Trim(),
            LastName = request.LastName.Trim(),
            DateOfBirth = request.DateOfBirth,
            Sex = request.Sex,
            ContactNumber = string.IsNullOrWhiteSpace(request.ContactNumber) ? null : request.ContactNumber.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            Email = "",
            IsGuest = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        if (string.Equals(request.DiscountCategory, "Senior", StringComparison.OrdinalIgnoreCase))
            patient.SeniorIdNumber = "pending";
        if (string.Equals(request.DiscountCategory, "PWD", StringComparison.OrdinalIgnoreCase))
            patient.PwdIdNumber = "pending";

        db.Patients.Add(patient);
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "patient",
            EntityId = patient.PatientId,
            OperationType = "create",
            PayloadJson = $$"""{"patientId":"{{patient.PatientId}}","patientCode":"{{patient.PatientCode}}"}""",
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _events.Publish(new ClinicEvent(ClinicEventKind.PatientsChanged, PatientId: patient.PatientId));
        //JJQR - AI (Nov 21, 2026)
        _sync.RequestSync();
        //JJQR - END
        return patient;
    }

    //JJQR - AI (Nov 21, 2026) - item 8: patient edits queue an update op (full snapshot is built at push time)
    public async Task<Patient?> UpdateAsync(Guid patientId, PatientUpdate u, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = await db.Patients.SingleOrDefaultAsync(x => x.PatientId == patientId, ct);
        if (p is null) return null;
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        var changed = new List<string>();
        if (u.FirstName is not null && T(u.FirstName) is { } fn && fn != p.FirstName) { p.FirstName = fn; changed.Add("first_name"); }
        if (u.LastName is not null && T(u.LastName) is { } ln && ln != p.LastName) { p.LastName = ln; changed.Add("last_name"); }
        if (u.MiddleName is not null && T(u.MiddleName) != p.MiddleName) { p.MiddleName = T(u.MiddleName); changed.Add("middle_name"); }
        if (u.DateOfBirth is { } dob && dob != p.DateOfBirth) { p.DateOfBirth = dob; changed.Add("date_of_birth"); }
        if (u.Sex is { } sex && sex != p.Sex) { p.Sex = sex; changed.Add("sex"); }
        if (u.ContactNumber is not null && T(u.ContactNumber) != p.ContactNumber) { p.ContactNumber = T(u.ContactNumber); changed.Add("contact_number"); }
        if (u.Address is not null && T(u.Address) != p.Address) { p.Address = T(u.Address); changed.Add("address"); }
        if (changed.Count == 0) return p;
        var now = DateTimeOffset.UtcNow;
        p.UpdatedAt = now;
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "patient",
            EntityId = p.PatientId,
            OperationType = "update",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { patientId = p.PatientId, changed_fields = changed }),
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _events.Publish(new ClinicEvent(ClinicEventKind.PatientsChanged, PatientId: p.PatientId));
        _sync.RequestSync();
        return p;
    }
    //JJQR - END

    //JJQR - AI (Nov 21, 2026) - contract v1.1 §5.1: full allergy list on the patient op; a real allergen never coexists with NKDA
    //JJQR - AI (Nov 21, 2026)
    public async Task<IReadOnlyList<PatientAllergy>> ListAllergiesAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.PatientAllergies.AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .OrderBy(a => a.Allergen)
            .ToListAsync(ct);
    }
    //JJQR - END

    public async Task<IReadOnlyList<PatientAllergy>?> UpdateAllergiesAsync(Guid patientId, IReadOnlyList<AllergyInput> allergies, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = await db.Patients.SingleOrDefaultAsync(x => x.PatientId == patientId, ct);
        if (p is null) return null;
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        var wanted = new Dictionary<string, AllergyInput>();
        foreach (var a in allergies ?? [])
        {
            var name = a.IsNkda ? "NKDA" : T(a.Allergen);
            if (name is null) continue;
            if (name.Length > 200) throw new ArgumentException("Allergen is too long.");
            wanted[a.IsNkda ? "\0nkda" : name.ToLowerInvariant()] = a with { Allergen = name };
        }
        if (wanted.Keys.Any(k => k != "\0nkda")) wanted.Remove("\0nkda");

        var existing = await db.PatientAllergies.Where(a => a.PatientId == patientId).ToListAsync(ct);
        static string Key(PatientAllergy a) => a.IsNkda ? "\0nkda" : a.Allergen.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var row in existing.ToList())
        {
            if (!wanted.ContainsKey(Key(row))) { db.PatientAllergies.Remove(row); existing.Remove(row); changed = true; }
        }
        foreach (var (key, w) in wanted)
        {
            var row = existing.FirstOrDefault(r => Key(r) == key);
            if (row is null)
            {
                db.PatientAllergies.Add(new PatientAllergy
                {
                    Id = w.Id is { } wid && wid != Guid.Empty && !await db.PatientAllergies.AnyAsync(x => x.Id == wid, ct) ? wid : Guid.NewGuid(),
                    PatientId = patientId, Allergen = w.Allergen, Reaction = T(w.Reaction), Severity = T(w.Severity),
                    IsNkda = w.IsNkda, CreatedAt = now, UpdatedAt = now,
                });
                changed = true;
            }
            else if (row.Reaction != T(w.Reaction) || row.Severity != T(w.Severity))
            {
                // same allergen (case-insensitive): the stored spelling is kept
                row.Reaction = T(w.Reaction); row.Severity = T(w.Severity); row.UpdatedAt = now;
                changed = true;
            }
        }
        if (!changed) return existing.OrderBy(a => a.Allergen).ToList();

        p.UpdatedAt = now;
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "patient",
            EntityId = p.PatientId,
            OperationType = "update",
            // changed_fields lists only "allergies": demographic fields are never taken from a stale node copy because of an allergy edit
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { patientId = p.PatientId, changed_fields = new[] { "allergies" } }),
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _events.Publish(new ClinicEvent(ClinicEventKind.PatientsChanged, PatientId: p.PatientId));
        _sync.RequestSync();
        return await db.PatientAllergies.AsNoTracking().Where(a => a.PatientId == patientId).OrderBy(a => a.Allergen).ToListAsync(ct);
    }
    //JJQR - END
}
// JJQR - END
