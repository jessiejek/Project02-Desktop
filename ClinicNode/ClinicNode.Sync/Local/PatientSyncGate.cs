//JJQR - AI (2026-11-26) - GAV-12: "Merge to existing patient" syncs first (like GAV-11's VisitSyncGate). Unsent work of the CURRENT patient on this computer
// (the patient record and allergies, its visits, their payments, consultations and vitals) must reach the clinic server BEFORE the merge, or it would be
// pushed later under the retired record. The server now redirects such late pushes to the kept patient, but the doctor's typing must not be lost either.
using ClinicApp.Infrastructure;
using ClinicNode.Core.Admin;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Sync.Local;

public sealed class PatientSyncGate(IDbContextFactory<ClinicAppDbContext> factory, ISyncService? sync = null) : IPatientSyncGate
{
    public async Task<string?> SyncFirstAsync(Guid patientId, CancellationToken ct = default)
    {
        if (sync is not null)
        {
            try { await sync.RunManualAsync("manual", ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("PatientMerge.SyncFirst", ex); }
        }
        return await PendingProblemAsync(patientId, ct);
    }

    public async Task<string?> PendingProblemAsync(Guid patientId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var keys = new List<(string Aggregate, Guid Id)> { (Aggregates.Patient, patientId) };
        var bookings = await db.Bookings.AsNoTracking().Where(b => b.PatientId == patientId).Select(b => b.BookingId).ToListAsync(ct);
        keys.AddRange(bookings.Select(id => (Aggregates.Booking, id)));
        keys.AddRange((await db.Payments.AsNoTracking().Where(p => bookings.Contains(p.BookingId)).Select(p => p.PaymentId).ToListAsync(ct)).Select(id => (Aggregates.Payment, id)));
        keys.AddRange((await db.Consultations.AsNoTracking().Where(c => c.PatientId == patientId).Select(c => c.ConsultationId).ToListAsync(ct)).Select(id => (Aggregates.Consultation, id)));
        keys.AddRange((await db.PatientVitalReadings.AsNoTracking().Where(v => v.PatientId == patientId).Select(v => v.Id).ToListAsync(ct)).Select(id => (Aggregates.VitalReading, id)));
        foreach (var (aggregate, id) in keys)
            if (await SyncLocalStore.HasPendingAsync(db, aggregate, id.ToString(), ct)) return PatientMergeRules.PendingMessage;
        return null;
    }
}
//JJQR - END
