//JJQR - AI (Nov 21, 2026) - "Your profile": full name + contact (Rules.Staff), read-only email, change password (PasswordPolicy + current-password check), as in StaffAccountsController.Update / AuthController.ChangePassword.
using ClinicApp.Auth;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Staff;

public sealed record StaffProfile(Guid StaffId, string FullName, string ContactNumber, string Email);

public interface IStaffProfileService
{
    Task<StaffProfile?> GetMineAsync(CancellationToken ct = default);
    Task<StaffResult> SaveAsync(string? fullName, string? contactNumber, CancellationToken ct = default);
    /// <summary>Web <c>canUpdate</c>: current not blank, new ≥ 12 chars, new == confirm.</summary>
    bool CanUpdatePassword(string? current, string? newPassword, string? confirm);
    Task<StaffResult> ChangePasswordAsync(string? current, string? newPassword, string? confirm, CancellationToken ct = default);
}

public sealed class StaffProfileService : IStaffProfileService
{
    public static readonly string PasswordHint = $"At least {PasswordPolicy.MinLength} characters. Avoid common passwords and your own email.";

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IAuthService _auth;
    private readonly PasswordHasherService _hasher;
    private readonly ISyncOutbox _outbox;
    //JJQR - AI (Nov 21, 2026) - optional sync hook: after a successful save, ask the background loop to push now.
    private readonly ISyncBridge? _sync;

    public StaffProfileService(IDbContextFactory<ClinicAppDbContext> factory, IAuthService auth, PasswordHasherService hasher, ISyncOutbox outbox, ISyncBridge? sync = null)
    {
        _factory = factory;
        _auth = auth;
        _hasher = hasher;
        _outbox = outbox;
        _sync = sync;
    }
    //JJQR - END

    public async Task<StaffProfile?> GetMineAsync(CancellationToken ct = default)
    {
        var userId = _auth.Current?.UserId;
        if (userId is null) return null;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.StaffAccounts.AsNoTracking().SingleOrDefaultAsync(s => s.UserId == userId, ct);
        return row is null ? null : new StaffProfile(row.StaffId, row.FullName, row.ContactNumber ?? "", row.Email);
    }

    public async Task<StaffResult> SaveAsync(string? fullName, string? contactNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return StaffResult.Fail("Enter your full name.");
        if ((StaffRules.Length(fullName, 200, "Full name") ?? StaffRules.Phone(contactNumber)) is { } problem) return StaffResult.Fail(problem);
        var userId = _auth.Current?.UserId;
        if (userId is null) return StaffResult.Fail("Your session has ended. Sign in again.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.StaffAccounts.SingleOrDefaultAsync(s => s.UserId == userId, ct);
        if (row is null) return StaffResult.Fail("Your profile wasn’t saved. Please try again.");
        var now = DateTimeOffset.UtcNow;
        row.FullName = fullName.Trim();
        row.ContactNumber = string.IsNullOrWhiteSpace(contactNumber) ? null : contactNumber.Trim();
        row.UpdatedAt = now;
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "staff_account",
            EntityId = row.StaffId,
            OperationType = "update",
            PayloadJson = $$"""{"staffId":"{{row.StaffId}}"}""",
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        //JJQR - END
        return StaffResult.Ok(row.StaffId);
    }

    public bool CanUpdatePassword(string? current, string? newPassword, string? confirm) =>
        !string.IsNullOrWhiteSpace(current) && (newPassword ?? "").Length >= PasswordPolicy.MinLength && newPassword == confirm;

    public async Task<StaffResult> ChangePasswordAsync(string? current, string? newPassword, string? confirm, CancellationToken ct = default)
    {
        if (newPassword != confirm) return StaffResult.Fail("Passwords don’t match yet.");
        if (PasswordPolicy.Check(newPassword) is { } weak) return StaffResult.Fail(weak);
        var userId = _auth.Current?.UserId;
        if (userId is null) return StaffResult.Fail("Your session has ended. Sign in again.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return StaffResult.Fail("Your session has ended. Sign in again.");
        if (string.IsNullOrEmpty(current) || !_hasher.Verify(user, user.PasswordHash, current))
            return StaffResult.Fail("Current password is incorrect.");
        if (PasswordPolicy.Check(newPassword, user.Email) is { } weakEmail) return StaffResult.Fail(weakEmail);
        user.PasswordHash = _hasher.Hash(user, newPassword!);
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "user",
            EntityId = user.Id,
            OperationType = "password-changed",
            PayloadJson = $$"""{"userId":"{{user.Id}}"}""",
            CreatedAt = DateTimeOffset.UtcNow,
        }, ct);
        await db.SaveChangesAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        //JJQR - END
        return StaffResult.Ok(user.Id);
    }
}
//JJQR - END
