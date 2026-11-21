//JJQR - AI (21 Nov 2026) - per-computer official receipt numbers for the admin's online (API) payment confirmations.
// Same format and sequence as the offline path (StaffPaymentsService.AllocateOrNumberAsync): OR-<first 4 hex of this node id>-<yyyymmdd Manila>-<nnnn>.
// The cloud does not issue OR numbers (POST /api/payments/{id}/confirm just stores or_number), so the node allocates one before it calls the API.
using ClinicApp.Domain;
//JJQR - AI (21 Nov 2026)
using ClinicApp.Domain.Enums;
//JJQR - END
using ClinicApp.Infrastructure;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Admin;

public interface IAdminOrNumberAllocator
{
    /// <summary>The next per-computer OR number for <paramref name="bookingId"/>'s payment. A number already issued today for that payment is reused.
    /// When this computer has the payment row it is stored there; every number issued this session is also remembered, so two confirmations made
    /// online before the next sync pull can never share a number.</summary>
    Task<string> NextAsync(Guid bookingId, CancellationToken ct = default);

    //JJQR - AI (21 Nov 2026) - OR uniqueness: the server refused <paramref name="orNumber"/> (409 or_number_duplicate), so nothing about it may stay saved
    // locally. Undoes the local payment copy's OR number (back to what it was) and stops reusing it for this booking. Allocation itself is unchanged.
    Task ReleaseAsync(Guid bookingId, string orNumber, CancellationToken ct = default) => Task.CompletedTask;
    //JJQR - END
}

public sealed class AdminOrNumberAllocator : IAdminOrNumberAllocator
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _issued = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _byBooking = new();
    //JJQR - AI (21 Nov 2026) - the local payment copy's OR number before NextAsync overwrote it, so a server clash can undo the local write
    private readonly Dictionary<Guid, string?> _previous = new();
    //JJQR - END

    public AdminOrNumberAllocator(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    public async Task<string> NextAsync(Guid bookingId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var today = ClinicClock.Today;
            await using var db = await _factory.CreateDbContextAsync(ct);
            var identity = await SyncOutbox.GetOrCreateIdentityAsync(db, ct);
            var dayPrefix = StaffRules.OrDayPrefix(StaffRules.NodePrefixFrom(identity.NodeId), today);

            if (_byBooking.TryGetValue(bookingId, out var mine) && StaffRules.SeqOf(mine, dayPrefix) is not null) return mine;
            var payment = await db.Payments.SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
            if (payment is not null && StaffRules.SeqOf(payment.OrNumber, dayPrefix) is not null) return payment.OrNumber!;

            var stored = await db.Payments.AsNoTracking()
                .Where(p => p.OrNumber != null && p.OrNumber.StartsWith(dayPrefix))
                .Select(p => p.OrNumber!)
                .ToListAsync(ct);
            var next = 1;
            foreach (var n in stored.Concat(_issued))
                if (StaffRules.SeqOf(n, dayPrefix) is { } seq && seq >= next)
                    next = seq + 1;
            var or = StaffRules.FormatOrNumber(StaffRules.NodePrefixFrom(identity.NodeId), today, next);

            if (payment is not null)
            {
                // Kept on the local copy only (no outbox entry): the API call carries it to the server and the next pull brings the row back unchanged.
                //JJQR - AI (21 Nov 2026)
                _previous[bookingId] = payment.OrNumber;
                //JJQR - END
                payment.OrNumber = or;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            _issued.Add(or);
            _byBooking[bookingId] = or;
            return or;
        }
        finally
        {
            _gate.Release();
        }
    }

    //JJQR - AI (21 Nov 2026) - see IAdminOrNumberAllocator.ReleaseAsync. The refused number stays in _issued so this computer never issues it again.
    public async Task ReleaseAsync(Guid bookingId, string orNumber, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_byBooking.TryGetValue(bookingId, out var mine) && string.Equals(mine, orNumber, StringComparison.Ordinal))
                _byBooking.Remove(bookingId);
            await using var db = await _factory.CreateDbContextAsync(ct);
            var payment = await db.Payments.SingleOrDefaultAsync(p => p.BookingId == bookingId, ct);
            //JJQR - AI (21 Nov 2026) - only an Unpaid local row may lose the refused number (a Paid row still waiting to sync keeps its OR), matching the staff fix
            if (payment is not null && payment.Status == PaymentStatus.Unpaid && string.Equals(payment.OrNumber, orNumber, StringComparison.Ordinal))
            //JJQR - END
            {
                payment.OrNumber = _previous.TryGetValue(bookingId, out var before) ? before : null;
                payment.UpdatedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            _previous.Remove(bookingId);
        }
        finally
        {
            _gate.Release();
        }
    }
    //JJQR - END
}
//JJQR - END
