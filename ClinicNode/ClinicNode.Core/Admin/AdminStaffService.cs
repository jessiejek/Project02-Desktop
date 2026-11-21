// JJQR - AI (Nov 21, 2026) - ported from StaffAccountsController + AuthController Invite / DeleteUser.
using ClinicApp.Auth;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
// JJQR - AI (Nov 21, 2026)
using ClinicNode.Core.Sync;
// JJQR - END

namespace ClinicNode.Core.Admin;

public sealed record StaffListItem(
    Guid StaffId,
    Guid UserId,
    string FullName,
    string Email,
    string? ContactNumber,
    StaffRole Role,
    StaffStatus Status,
    DateTimeOffset InvitedAt);

public sealed record StaffInviteRequest(
    string Email,
    string FullName,
    string Role,
    string? ContactNumber = null,
    string? InitialPassword = null,
    DoctorInviteProfile? Doctor = null);

public sealed record DoctorInviteProfile(
    string Specialization,
    decimal ConsultationFee,
    int SlotDurationMinutes = 30,
    int SlotCapacity = 1,
    int? DailyPatientLimit = null,
    string? Bio = null,
    string? LicenseNumber = null,
    string? PtrNumber = null,
    string? S2Number = null,
    IReadOnlyList<ScheduleDayEdit>? Schedule = null);

public sealed record ScheduleDayEdit(short DayOfWeek, bool IsActive, TimeOnly StartTime, TimeOnly EndTime);

public sealed record StaffInviteResult(Guid UserId, Guid StaffId, bool PasswordSet);

public interface IAdminStaffService
{
    Task<AdminPage<StaffListItem>> SearchAsync(string? q, string? role, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default);
    Task<StaffAccount?> GetAsync(Guid staffId, CancellationToken ct = default);
    Task<AdminResult<StaffAccount>> UpdateAsync(Guid staffId, string fullName, string? contactNumber, StaffStatus? status, CancellationToken ct = default);
    Task<AdminResult<StaffInviteResult>> InviteAsync(StaffInviteRequest request, CancellationToken ct = default);
    Task<AdminResult> SetInvitedPasswordAsync(Guid userId, string password, CancellationToken ct = default);
    Task<AdminResult> RevokeInviteAsync(Guid userId, CancellationToken ct = default);
}

public sealed class AdminStaffService : IAdminStaffService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;
    private readonly PasswordHasherService _hasher;

    // JJQR - AI (Nov 21, 2026)
    private readonly ISyncBridge? _sync;
    // JJQR - END

    public AdminStaffService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth, PasswordHasherService hasher, ISyncBridge? sync = null)
    {
        // JJQR - AI (Nov 21, 2026)
        _sync = sync;
        // JJQR - END
        _factory = factory;
        _outbox = outbox;
        _auth = auth;
        _hasher = hasher;
    }

    public async Task<AdminPage<StaffListItem>> SearchAsync(string? q, string? role, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.StaffAccounts.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<StaffRole>(role, out var r))
            query = query.Where(s => s.Role == r);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(x => x.FullName.Contains(s) || x.Email.Contains(s));
        }
        var desc = sort is not null && sort.StartsWith('-');
        var key = (sort ?? "name").TrimStart('-');
        query = (key, desc) switch
        {
            ("role", false) => query.OrderBy(x => x.Role),
            ("role", true) => query.OrderByDescending(x => x.Role),
            ("status", false) => query.OrderBy(x => x.Status),
            ("status", true) => query.OrderByDescending(x => x.Status),
            (_, true) => query.OrderByDescending(x => x.FullName),
            _ => query.OrderBy(x => x.FullName),
        };
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new StaffListItem(s.StaffId, s.UserId, s.FullName, s.Email, s.ContactNumber, s.Role, s.Status, s.InvitedAt))
            .ToListAsync(ct);
        return new AdminPage<StaffListItem>(items, total, page, pageSize);
    }

    public async Task<StaffAccount?> GetAsync(Guid staffId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.StaffAccounts.AsNoTracking().SingleOrDefaultAsync(s => s.StaffId == staffId, ct);
    }

    public async Task<AdminResult<StaffAccount>> UpdateAsync(Guid staffId, string fullName, string? contactNumber, StaffStatus? status, CancellationToken ct = default)
    {
        var check = AdminCheck.Staff(fullName, contactNumber);
        if (check.HasErrors) return AdminResult<StaffAccount>.Fail(check.Message!);

        var actor = _auth.Current;
        if (actor is null) return AdminResult<StaffAccount>.Fail("Not signed in.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.StaffAccounts.SingleOrDefaultAsync(s => s.StaffId == staffId, ct);
        if (row is null) return AdminResult<StaffAccount>.Fail("Staff account not found.");

        var self = actor.UserId == row.UserId;
        if (!self && !actor.IsAdmin) return AdminResult<StaffAccount>.Fail("Only an administrator can edit another account.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        row.FullName = fullName.Trim();
        row.ContactNumber = string.IsNullOrWhiteSpace(contactNumber) ? null : contactNumber.Trim();
        // Only an Admin changing someone else's row may change status; a self-edit keeps it.
        if (!self && actor.IsAdmin && status is { } next)
        {
            row.Status = next;
            if (next == StaffStatus.Inactive)
            {
                var live = await db.RefreshTokens.Where(t => t.UserId == row.UserId && t.RevokedAt == null).ToListAsync(ct);
                foreach (var t in live) t.RevokedAt = DateTimeOffset.UtcNow;
                row.RevokedAt = DateTimeOffset.UtcNow;
            }
        }
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "staff_account",
            EntityId = row.StaffId,
            OperationType = "update",
            PayloadJson = $$"""{"staffId":"{{row.StaffId}}","status":"{{row.Status}}"}""",
            CreatedAt = row.UpdatedAt,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult<StaffAccount>.Ok(row);
    }

    public async Task<AdminResult<StaffInviteResult>> InviteAsync(StaffInviteRequest request, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult<StaffInviteResult>.Fail("Only an administrator can invite accounts.");

        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(request.FullName))
            return AdminResult<StaffInviteResult>.Fail("Email and full name are required.");
        var nameCheck = AdminCheck.Staff(request.FullName, request.ContactNumber);
        if (nameCheck.HasErrors) return AdminResult<StaffInviteResult>.Fail(nameCheck.Message!);
        if (!Enum.TryParse<StaffRole>(request.Role, out var staffRole))
            return AdminResult<StaffInviteResult>.Fail("Role must be Staff, Doctor, or Admin.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            return AdminResult<StaffInviteResult>.Fail("An account with this email already exists.");

        if (staffRole == StaffRole.Doctor && request.Doctor is { } dp0)
        {
            var proto = new ClinicApp.Domain.Entities.Doctor
            {
                Specialization = dp0.Specialization,
                ConsultationFee = dp0.ConsultationFee,
                SlotDurationMinutes = dp0.SlotDurationMinutes <= 0 ? 30 : dp0.SlotDurationMinutes,
                SlotCapacity = dp0.SlotCapacity <= 0 ? 1 : dp0.SlotCapacity,
                DailyPatientLimit = dp0.DailyPatientLimit,
                Bio = dp0.Bio,
                LicenseNumber = dp0.LicenseNumber,
                PtrNumber = dp0.PtrNumber,
                S2Number = dp0.S2Number,
            };
            var dcheck = AdminCheck.Doctor(proto);
            if (dcheck.HasErrors) return AdminResult<StaffInviteResult>.Fail(dcheck.Message!);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            EmailConfirmed = false,
            MustSetPassword = string.IsNullOrWhiteSpace(request.InitialPassword),
            CreatedAt = now,
        };
        var password = string.IsNullOrWhiteSpace(request.InitialPassword)
            ? Guid.NewGuid().ToString("N") + Guid.NewGuid()
            : request.InitialPassword;
        user.PasswordHash = _hasher.Hash(user, password);
        db.Users.Add(user);
        db.Profiles.Add(new Profile
        {
            Id = user.Id,
            Role = staffRole switch
            {
                StaffRole.Doctor => UserRole.Doctor,
                StaffRole.Admin => UserRole.Admin,
                _ => UserRole.Staff,
            },
            CreatedAt = now,
        });

        var staffId = Guid.NewGuid();
        db.StaffAccounts.Add(new StaffAccount
        {
            StaffId = staffId,
            UserId = user.Id,
            FullName = request.FullName.Trim(),
            Email = email,
            ContactNumber = string.IsNullOrWhiteSpace(request.ContactNumber) ? null : request.ContactNumber.Trim(),
            Role = staffRole,
            Status = StaffStatus.Invited,
            InvitedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        });

        if (staffRole == StaffRole.Doctor && request.Doctor is { } dp)
        {
            db.Doctors.Add(new ClinicApp.Domain.Entities.Doctor
            {
                DoctorId = staffId,
                Specialization = dp.Specialization.Trim(),
                ConsultationFee = dp.ConsultationFee,
                Bio = string.IsNullOrWhiteSpace(dp.Bio) ? null : dp.Bio.Trim(),
                LicenseNumber = string.IsNullOrWhiteSpace(dp.LicenseNumber) ? null : dp.LicenseNumber.Trim(),
                PtrNumber = string.IsNullOrWhiteSpace(dp.PtrNumber) ? null : dp.PtrNumber.Trim(),
                S2Number = string.IsNullOrWhiteSpace(dp.S2Number) ? null : dp.S2Number.Trim(),
                SlotDurationMinutes = dp.SlotDurationMinutes <= 0 ? 30 : dp.SlotDurationMinutes,
                SlotCapacity = dp.SlotCapacity <= 0 ? 1 : dp.SlotCapacity,
                DailyPatientLimit = dp.DailyPatientLimit,
                CreatedAt = now,
                UpdatedAt = now,
            });
            foreach (var d in dp.Schedule ?? [])
            {
                db.DoctorSchedules.Add(new DoctorSchedule
                {
                    Id = Guid.NewGuid(),
                    DoctorId = staffId,
                    DayOfWeek = d.DayOfWeek,
                    IsActive = d.IsActive,
                    StartTime = d.StartTime,
                    EndTime = d.EndTime,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }

        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "staff_account",
            EntityId = staffId,
            OperationType = "create",
            PayloadJson = $$"""{"userId":"{{user.Id}}","staffId":"{{staffId}}","role":"{{staffRole}}"}""",
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult<StaffInviteResult>.Ok(new StaffInviteResult(user.Id, staffId, !string.IsNullOrWhiteSpace(request.InitialPassword)));
    }

    public async Task<AdminResult> SetInvitedPasswordAsync(Guid userId, string password, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult.Fail("Only an administrator can set an invited password.");
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return AdminResult.Fail("Password must be at least 8 characters.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return AdminResult.Fail("User not found.");
        user.PasswordHash = _hasher.Hash(user, password);
        user.MustSetPassword = false;
        await db.SaveChangesAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult.Ok();
    }

    public async Task<AdminResult> RevokeInviteAsync(Guid userId, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult.Fail("Only an administrator can revoke an invite.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return AdminResult.Fail("User not found.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var staff = await db.StaffAccounts.Where(s => s.UserId == userId).ToListAsync(ct);
        if (staff.Count > 0) db.StaffAccounts.RemoveRange(staff);
        var profile = await db.Profiles.FindAsync([userId], ct);
        if (profile is not null) db.Profiles.Remove(profile);
        db.Users.Remove(user);
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "staff_account",
            EntityId = userId,
            OperationType = "delete",
            PayloadJson = $$"""{"userId":"{{userId}}"}""",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult.Ok();
    }
}
// JJQR - END
