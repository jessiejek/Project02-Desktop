//JJQR - AI (Nov 21, 2026) - D-PROFILE "Your profile": professional details (Rules.Doctor + staff full name) and Change Password (delegates to the staff profile service: PasswordPolicy + current-password check).
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Staff;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.DoctorExtra;

public sealed record DoctorProfile(Guid DoctorId, string FullName, string Email, string Specialization, string Bio, decimal ConsultationFee,
    string LicenseNumber, string PtrNumber, string S2Number);

public interface IDoctorProfileService
{
    Guid? CurrentDoctorId { get; }
    Task<DoctorProfile?> GetMineAsync(CancellationToken ct = default);
    Task<DoctorResult> SaveAsync(DoctorProfileEdit edit, CancellationToken ct = default);
    Task<DoctorResult> ChangePasswordAsync(string? current, string? newPassword, string? confirm, CancellationToken ct = default);
}

public sealed class DoctorProfileService : IDoctorProfileService
{
    public const string SaveFailed = "Some profile changes could not be saved. Please review your details and try again.";

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IAuthService _auth;
    private readonly IStaffProfileService _password;
    private readonly ISyncOutbox _outbox;
    private readonly ISyncBridge? _sync;

    public DoctorProfileService(IDbContextFactory<ClinicAppDbContext> factory, IAuthService auth, IStaffProfileService password, ISyncOutbox outbox, ISyncBridge? sync = null)
    {
        _factory = factory;
        _auth = auth;
        _password = password;
        _outbox = outbox;
        _sync = sync;
    }

    public Guid? CurrentDoctorId => _auth.Current?.DoctorId ?? (_auth.Current?.IsDoctor == true ? _auth.Current.StaffId : null);

    public async Task<DoctorProfile?> GetMineAsync(CancellationToken ct = default)
    {
        if (CurrentDoctorId is not { } id) return null;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var d = await db.Doctors.AsNoTracking().Include(x => x.StaffAccount).SingleOrDefaultAsync(x => x.DoctorId == id, ct);
        if (d is null) return null;
        return new DoctorProfile(d.DoctorId, d.StaffAccount?.FullName ?? "", d.StaffAccount?.Email ?? "", d.Specialization, d.Bio ?? "",
            d.ConsultationFee, d.LicenseNumber ?? "", d.PtrNumber ?? "", d.S2Number ?? "");
    }

    public async Task<DoctorResult> SaveAsync(DoctorProfileEdit edit, CancellationToken ct = default)
    {
        var check = DoctorExtraRules.ValidateProfile(edit);
        if (!check.Ok) return DoctorResult.Fail(check.Error!);
        if (CurrentDoctorId is not { } id) return DoctorResult.Fail("Your session has ended. Sign in again.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var d = await db.Doctors.Include(x => x.StaffAccount).SingleOrDefaultAsync(x => x.DoctorId == id, ct);
        if (d is null) return DoctorResult.Fail(SaveFailed);
        static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var now = DateTimeOffset.UtcNow;
        d.Specialization = edit.Specialization!.Trim();
        d.Bio = Blank(edit.Bio);
        d.ConsultationFee = check.Fee;
        d.LicenseNumber = Blank(edit.LicenseNumber);
        d.PtrNumber = Blank(edit.PtrNumber);
        d.S2Number = Blank(edit.S2Number);
        d.UpdatedAt = now;
        if (d.StaffAccount is { } staff)
        {
            staff.FullName = edit.FullName!.Trim();
            staff.UpdatedAt = now;
        }
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(), Aggregate = "doctor", EntityId = id, OperationType = "update",
            PayloadJson = $$"""{"doctorId":"{{id}}"}""", CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        _sync?.RequestSync();
        return DoctorResult.Ok(id);
    }

    public async Task<DoctorResult> ChangePasswordAsync(string? current, string? newPassword, string? confirm, CancellationToken ct = default)
    {
        // The staff profile service already requests a sync after it saves.
        var r = await _password.ChangePasswordAsync(current, newPassword, confirm, ct);
        return r.Succeeded ? DoctorResult.Ok(r.Id) : DoctorResult.Fail(r.Error ?? "Could not update the password.");
    }
}
//JJQR - END
