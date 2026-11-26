//JJQR - AI (2026-11-26) - GAV-11: "Wrong patient?" syncs first. Unsent work for this visit on this computer (the booking, its payment, its
// consultation, its vitals) must reach the clinic server BEFORE the visit moves, or it could be pushed later under the old patient. The server
// now also refuses that (SyncPushEngine keeps the moved patient), but the doctor's typing must not be lost either, so the move waits for it.
// A sync that fails (offline, computer not registered) is fine when nothing of this visit is waiting; otherwise the move is refused.
using ClinicApp.Infrastructure;
using ClinicNode.Core.Admin;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Sync.Local;

public sealed class VisitSyncGate(IDbContextFactory<ClinicAppDbContext> factory, ISyncService? sync = null) : IVisitSyncGate
{
    public async Task<string?> SyncFirstAsync(Guid bookingId, CancellationToken ct = default)
    {
        if (sync is not null) // always (owner: the desktop syncs first); a failed run only matters when this visit still has work waiting
        {
            try { await sync.RunManualAsync("manual", ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("WrongPatient.SyncFirst", ex); }
        }
        return await PendingProblemAsync(bookingId, ct);
    }

    public async Task<string?> PendingProblemAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var keys = new List<(string Aggregate, Guid Id)> { (Aggregates.Booking, bookingId) };
        keys.AddRange((await db.Payments.AsNoTracking().Where(p => p.BookingId == bookingId).Select(p => p.PaymentId).ToListAsync(ct)).Select(id => (Aggregates.Payment, id)));
        keys.AddRange((await db.Consultations.AsNoTracking().Where(c => c.BookingId == bookingId).Select(c => c.ConsultationId).ToListAsync(ct)).Select(id => (Aggregates.Consultation, id)));
        keys.AddRange((await db.PatientVitalReadings.AsNoTracking().Where(v => v.BookingId == bookingId).Select(v => v.Id).ToListAsync(ct)).Select(id => (Aggregates.VitalReading, id)));
        foreach (var (aggregate, id) in keys)
            if (await SyncLocalStore.HasPendingAsync(db, aggregate, id.ToString(), ct)) return WrongPatientRules.PendingMessage;
        return null;
    }
}
//JJQR - END
