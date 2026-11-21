// JJQR - AI (Nov 21, 2026) - ported from AnnouncementsController.
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core.Sync;
// JJQR - END

namespace ClinicNode.Core.Admin;

public sealed record AnnouncementEdit(string Title, string Body, bool IsActive = true);

public interface IAdminAnnouncementService
{
    Task<IReadOnlyList<Announcement>> ListAsync(bool activeOnly = false, CancellationToken ct = default);
    Task<AdminResult<Announcement>> CreateAsync(AnnouncementEdit edit, CancellationToken ct = default);
    Task<AdminResult<Announcement>> UpdateAsync(Guid id, AnnouncementEdit edit, CancellationToken ct = default);
    Task<AdminResult> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class AdminAnnouncementService : IAdminAnnouncementService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;

    // JJQR - AI (Nov 21, 2026)
    private readonly ISyncBridge? _sync;
    // JJQR - END

    public AdminAnnouncementService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth, ISyncBridge? sync = null)
    {
        // JJQR - AI (Nov 21, 2026)
        _sync = sync;
        // JJQR - END
        _factory = factory;
        _outbox = outbox;
        _auth = auth;
    }

    public async Task<IReadOnlyList<Announcement>> ListAsync(bool activeOnly = false, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.Announcements.AsNoTracking().AsQueryable();
        if (activeOnly) query = query.Where(a => a.IsActive);
        return await query.OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
    }

    public async Task<AdminResult<Announcement>> CreateAsync(AnnouncementEdit edit, CancellationToken ct = default)
    {
        var check = AdminCheck.Announcement(edit.Title, edit.Body);
        if (check.HasErrors) return AdminResult<Announcement>.Fail(check.Message!);

        await using var db = await _factory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var row = new Announcement
        {
            Id = Guid.NewGuid(),
            Title = edit.Title.Trim(),
            Body = edit.Body.Trim(),
            IsActive = edit.IsActive,
            PostedByUserId = _auth.Current?.UserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Announcements.Add(row);
        await Enqueue(db, row.Id, "create", now, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult<Announcement>.Ok(row);
    }

    public async Task<AdminResult<Announcement>> UpdateAsync(Guid id, AnnouncementEdit edit, CancellationToken ct = default)
    {
        var check = AdminCheck.Announcement(edit.Title, edit.Body);
        if (check.HasErrors) return AdminResult<Announcement>.Fail(check.Message!);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Announcements.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (row is null) return AdminResult<Announcement>.Fail("Announcement not found.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        row.Title = edit.Title.Trim();
        row.Body = edit.Body.Trim();
        row.IsActive = edit.IsActive;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await Enqueue(db, row.Id, "update", row.UpdatedAt, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult<Announcement>.Ok(row);
    }

    public async Task<AdminResult> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.Announcements.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (row is null) return AdminResult.Fail("Announcement not found.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Announcements.Remove(row);
        await Enqueue(db, id, "delete", DateTimeOffset.UtcNow, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult.Ok();
    }

    async Task Enqueue(ClinicAppDbContext db, Guid id, string op, DateTimeOffset now, CancellationToken ct) =>
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "announcement",
            EntityId = id,
            OperationType = op,
            PayloadJson = $$"""{"id":"{{id}}"}""",
            CreatedAt = now,
        }, ct);
}
// JJQR - END
