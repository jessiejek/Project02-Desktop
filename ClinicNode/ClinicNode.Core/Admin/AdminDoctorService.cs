// JJQR - AI (Nov 21, 2026) - ported from DoctorsController (admin list, update, schedules, blocked dates, services, deactivate via staff).
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

public sealed record AdminDoctorListItem(
    Guid DoctorId,
    Guid StaffId,
    Guid UserId,
    string Name,
    string Email,
    string? ContactNumber,
    string Specialization,
    decimal ConsultationFee,
    StaffStatus Status,
    string WorkingDays,
    int SlotDurationMinutes,
    int SlotCapacity,
    int? DailyPatientLimit,
    string? Bio,
    string? LicenseNumber,
    string? PtrNumber,
    string? S2Number);

public sealed record DoctorEdit(
    string Specialization,
    decimal ConsultationFee,
    int SlotDurationMinutes,
    int SlotCapacity,
    int? DailyPatientLimit,
    string? Bio,
    string? LicenseNumber,
    string? PtrNumber,
    string? S2Number,
    string? FullName = null,
    string? ContactNumber = null,
    StaffStatus? Status = null);

public sealed record BlockedDateItem(Guid Id, DateOnly BlockedDate, string? Reason);

public interface IAdminDoctorService
{
    Task<AdminPage<AdminDoctorListItem>> SearchAsync(string? q, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default);
    Task<IReadOnlyList<AdminDoctorListItem>> ListAllAsync(bool includeInactive = true, CancellationToken ct = default);
    Task<AdminDoctorListItem?> GetAsync(Guid doctorId, CancellationToken ct = default);
    Task<AdminResult<AdminDoctorListItem>> UpdateAsync(Guid doctorId, DoctorEdit edit, CancellationToken ct = default);
    Task<IReadOnlyList<ScheduleDayEdit>> GetSchedulesAsync(Guid doctorId, CancellationToken ct = default);
    Task<AdminResult> UpsertSchedulesAsync(Guid doctorId, IReadOnlyList<ScheduleDayEdit> days, CancellationToken ct = default);
    Task<IReadOnlyList<BlockedDateItem>> GetBlockedDatesAsync(Guid doctorId, CancellationToken ct = default);
    Task<AdminResult<BlockedDateItem>> AddBlockedDateAsync(Guid doctorId, DateOnly date, string? reason, CancellationToken ct = default);
    Task<AdminResult> RemoveBlockedDateAsync(Guid blockedDateId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetServiceIdsAsync(Guid doctorId, CancellationToken ct = default);
    Task<AdminResult> SetServicesAsync(Guid doctorId, IReadOnlyList<Guid> serviceIds, CancellationToken ct = default);
}

public sealed class AdminDoctorService : IAdminDoctorService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;
    private readonly IAdminStaffService _staff;

    // JJQR - AI (Nov 21, 2026)
    private readonly ISyncBridge? _sync;
    // JJQR - END

    public AdminDoctorService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth, IAdminStaffService staff, ISyncBridge? sync = null)
    {
        // JJQR - AI (Nov 21, 2026)
        _sync = sync;
        // JJQR - END
        _factory = factory;
        _outbox = outbox;
        _auth = auth;
        _staff = staff;
    }

    public async Task<AdminPage<AdminDoctorListItem>> SearchAsync(string? q, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        await using var db = await _factory.CreateDbContextAsync(ct);
        var query = db.Doctors.AsNoTracking().Include(d => d.StaffAccount).AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(d =>
                (d.StaffAccount != null && d.StaffAccount.FullName.Contains(s)) ||
                d.Specialization.Contains(s) ||
                (d.LicenseNumber != null && d.LicenseNumber.Contains(s)));
        }
        var desc = sort is not null && sort.StartsWith('-');
        var key = (sort ?? "name").TrimStart('-');
        query = (key, desc) switch
        {
            ("specialization", false) => query.OrderBy(d => d.Specialization),
            ("specialization", true) => query.OrderByDescending(d => d.Specialization),
            (_, true) => query.OrderByDescending(d => d.StaffAccount!.FullName),
            _ => query.OrderBy(d => d.StaffAccount!.FullName),
        };
        var total = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = rows.Select(r => r.DoctorId).ToList();
        var schedules = await db.DoctorSchedules.AsNoTracking().Where(s => ids.Contains(s.DoctorId)).ToListAsync(ct);
        var items = rows.Select(d => Map(d, schedules.Where(s => s.DoctorId == d.DoctorId).ToList())).ToList();
        return new AdminPage<AdminDoctorListItem>(items, total, page, pageSize);
    }

    public async Task<IReadOnlyList<AdminDoctorListItem>> ListAllAsync(bool includeInactive = true, CancellationToken ct = default)
    {
        var page = await SearchAsync(null, 1, 200, "name", ct);
        return includeInactive ? page.Items : page.Items.Where(d => d.Status != StaffStatus.Inactive).ToList();
    }

    public async Task<AdminDoctorListItem?> GetAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var d = await db.Doctors.AsNoTracking().Include(x => x.StaffAccount).SingleOrDefaultAsync(x => x.DoctorId == doctorId, ct);
        if (d is null) return null;
        var schedules = await db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == doctorId).ToListAsync(ct);
        return Map(d, schedules);
    }

    public async Task<AdminResult<AdminDoctorListItem>> UpdateAsync(Guid doctorId, DoctorEdit edit, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult<AdminDoctorListItem>.Fail("Only an administrator can edit a doctor.");

        var proto = new ClinicApp.Domain.Entities.Doctor
        {
            Specialization = edit.Specialization,
            ConsultationFee = edit.ConsultationFee,
            SlotDurationMinutes = edit.SlotDurationMinutes,
            SlotCapacity = edit.SlotCapacity,
            DailyPatientLimit = edit.DailyPatientLimit,
            Bio = edit.Bio,
            LicenseNumber = edit.LicenseNumber,
            PtrNumber = edit.PtrNumber,
            S2Number = edit.S2Number,
        };
        var check = AdminCheck.Doctor(proto);
        if (check.HasErrors) return AdminResult<AdminDoctorListItem>.Fail(check.Message!);

        await using var db = await _factory.CreateDbContextAsync(ct);
        var doctor = await db.Doctors.Include(d => d.StaffAccount).SingleOrDefaultAsync(d => d.DoctorId == doctorId, ct);
        if (doctor is null) return AdminResult<AdminDoctorListItem>.Fail("Doctor not found.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        doctor.Specialization = edit.Specialization.Trim();
        doctor.ConsultationFee = edit.ConsultationFee;
        doctor.Bio = string.IsNullOrWhiteSpace(edit.Bio) ? null : edit.Bio.Trim();
        doctor.LicenseNumber = string.IsNullOrWhiteSpace(edit.LicenseNumber) ? null : edit.LicenseNumber.Trim();
        doctor.PtrNumber = string.IsNullOrWhiteSpace(edit.PtrNumber) ? null : edit.PtrNumber.Trim();
        doctor.S2Number = string.IsNullOrWhiteSpace(edit.S2Number) ? null : edit.S2Number.Trim();
        doctor.SlotDurationMinutes = edit.SlotDurationMinutes;
        doctor.SlotCapacity = edit.SlotCapacity;
        doctor.DailyPatientLimit = edit.DailyPatientLimit;
        doctor.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END

        if (doctor.StaffAccount is { } staff && (edit.FullName is not null || edit.ContactNumber is not null || edit.Status is not null))
        {
            var name = edit.FullName ?? staff.FullName;
            var contact = edit.ContactNumber ?? staff.ContactNumber;
            var updated = await _staff.UpdateAsync(staff.StaffId, name, contact, edit.Status, ct);
            if (!updated.Succeeded) return AdminResult<AdminDoctorListItem>.Fail(updated.Error!);
        }

        var item = await GetAsync(doctorId, ct);
        return item is null
            ? AdminResult<AdminDoctorListItem>.Fail("Doctor not found after save.")
            : AdminResult<AdminDoctorListItem>.Ok(item);
    }

    public async Task<IReadOnlyList<ScheduleDayEdit>> GetSchedulesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var rows = await db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == doctorId).OrderBy(s => s.DayOfWeek).ToListAsync(ct);
        return rows.Select(s => new ScheduleDayEdit(s.DayOfWeek, s.IsActive, s.StartTime, s.EndTime)).ToList();
    }

    public async Task<AdminResult> UpsertSchedulesAsync(Guid doctorId, IReadOnlyList<ScheduleDayEdit> days, CancellationToken ct = default)
    {
        if (_auth.Current is null) return AdminResult.Fail("Not signed in.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        if (!await db.Doctors.AnyAsync(d => d.DoctorId == doctorId, ct))
            return AdminResult.Fail("Doctor not found.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.DoctorSchedules.Where(s => s.DoctorId == doctorId).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var d in days)
        {
            var row = existing.SingleOrDefault(x => x.DayOfWeek == d.DayOfWeek);
            if (row is null)
            {
                db.DoctorSchedules.Add(new DoctorSchedule
                {
                    Id = Guid.NewGuid(),
                    DoctorId = doctorId,
                    DayOfWeek = d.DayOfWeek,
                    IsActive = d.IsActive,
                    StartTime = d.StartTime,
                    EndTime = d.EndTime,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
            else
            {
                row.IsActive = d.IsActive;
                row.StartTime = d.StartTime;
                row.EndTime = d.EndTime;
                row.UpdatedAt = now;
            }
        }
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = "doctor_schedule",
            EntityId = doctorId,
            OperationType = "update",
            PayloadJson = $$"""{"doctorId":"{{doctorId}}"}""",
            CreatedAt = now,
        }, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult.Ok();
    }

    public async Task<IReadOnlyList<BlockedDateItem>> GetBlockedDatesAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DoctorBlockedDates.AsNoTracking()
            .Where(b => b.DoctorId == doctorId)
            .OrderBy(b => b.BlockedDate)
            .Select(b => new BlockedDateItem(b.Id, b.BlockedDate, b.Reason))
            .ToListAsync(ct);
    }

    public async Task<AdminResult<BlockedDateItem>> AddBlockedDateAsync(Guid doctorId, DateOnly date, string? reason, CancellationToken ct = default)
    {
        if (_auth.Current is null) return AdminResult<BlockedDateItem>.Fail("Not signed in.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var row = new DoctorBlockedDate
        {
            Id = Guid.NewGuid(),
            DoctorId = doctorId,
            BlockedDate = date,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedAt = now,
        };
        db.DoctorBlockedDates.Add(row);
        await db.SaveChangesAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult<BlockedDateItem>.Ok(new BlockedDateItem(row.Id, row.BlockedDate, row.Reason));
    }

    public async Task<AdminResult> RemoveBlockedDateAsync(Guid blockedDateId, CancellationToken ct = default)
    {
        if (_auth.Current is null) return AdminResult.Fail("Not signed in.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.DoctorBlockedDates.FindAsync([blockedDateId], ct);
        if (row is null) return AdminResult.Fail("Blocked date not found.");
        db.DoctorBlockedDates.Remove(row);
        await db.SaveChangesAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult.Ok();
    }

    public async Task<IReadOnlyList<Guid>> GetServiceIdsAsync(Guid doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.DoctorServices.AsNoTracking().Where(ds => ds.DoctorId == doctorId).Select(ds => ds.ServiceId).ToListAsync(ct);
    }

    public async Task<AdminResult> SetServicesAsync(Guid doctorId, IReadOnlyList<Guid> serviceIds, CancellationToken ct = default)
    {
        if (_auth.Current is not { IsAdmin: true })
            return AdminResult.Fail("Only an administrator can set doctor services.");
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = db.DoctorServices.Where(ds => ds.DoctorId == doctorId);
        db.DoctorServices.RemoveRange(existing);
        var now = DateTimeOffset.UtcNow;
        foreach (var serviceId in serviceIds.Distinct())
            db.DoctorServices.Add(new DoctorService { DoctorId = doctorId, ServiceId = serviceId, DurationMinutes = 30, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        // JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        // JJQR - END
        return AdminResult.Ok();
    }

    static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    static AdminDoctorListItem Map(ClinicApp.Domain.Entities.Doctor d, IReadOnlyList<DoctorSchedule> schedules)
    {
        var staff = d.StaffAccount;
        var days = string.Join(", ", schedules.Where(s => s.IsActive).OrderBy(s => s.DayOfWeek).Select(s => DayNames[s.DayOfWeek % 7]));
        return new AdminDoctorListItem(
            d.DoctorId,
            staff?.StaffId ?? d.DoctorId,
            staff?.UserId ?? Guid.Empty,
            staff?.FullName ?? "",
            staff?.Email ?? "",
            staff?.ContactNumber,
            d.Specialization,
            d.ConsultationFee,
            staff?.Status ?? StaffStatus.Active,
            string.IsNullOrEmpty(days) ? "No working days" : days,
            d.SlotDurationMinutes,
            d.SlotCapacity,
            d.DailyPatientLimit,
            d.Bio,
            d.LicenseNumber,
            d.PtrNumber,
            d.S2Number);
    }
}
// JJQR - END
