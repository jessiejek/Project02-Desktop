//JJQR - AI (Nov 21, 2026) - "Doctor status today": every non-inactive doctor with today's status (default Available); set one or several (upsert on doctor + date, as DoctorsController.UpsertDayStatus).
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Events;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Staff;

public sealed record DoctorStatusRow(Guid DoctorId, string Name, DoctorDayStatusEnum Status);

public sealed record DoctorStatusBoard(int Available, int RunningLate, int OutToday, IReadOnlyList<DoctorStatusRow> Doctors);

public interface IDoctorStatusService
{
    Task<DoctorStatusBoard> GetTodayAsync(CancellationToken ct = default);
    Task<StaffResult> SetAsync(Guid doctorId, DoctorDayStatusEnum status, CancellationToken ct = default);
    Task<StaffResult> SetManyAsync(IEnumerable<Guid> doctorIds, DoctorDayStatusEnum status, CancellationToken ct = default);
}

public sealed class DoctorStatusService : IDoctorStatusService
{
    /// <summary>Web STATUS_OPTIONS labels.</summary>
    public static string Label(DoctorDayStatusEnum s) => s switch
    {
        DoctorDayStatusEnum.RunningLate => "Running late",
        DoctorDayStatusEnum.UnavailableToday => "Out today",
        _ => "Available",
    };

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IClinicEventBus _events;
    //JJQR - AI (Nov 21, 2026) - optional sync hook: after a successful save, ask the background loop to push now.
    private readonly ISyncBridge? _sync;

    public DoctorStatusService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IClinicEventBus events, ISyncBridge? sync = null)
    {
        _factory = factory;
        _outbox = outbox;
        _events = events;
        _sync = sync;
    }
    //JJQR - END

    public async Task<DoctorStatusBoard> GetTodayAsync(CancellationToken ct = default)
    {
        var today = ClinicClock.Today;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var doctors = await db.Doctors.AsNoTracking().Include(d => d.StaffAccount).ToListAsync(ct);
        var statuses = await db.DoctorDayStatuses.AsNoTracking().Where(s => s.StatusDate == today).ToListAsync(ct);
        var byDoctor = statuses.GroupBy(s => s.DoctorId).ToDictionary(g => g.Key, g => g.First().Status);
        var rows = doctors.Where(d => d.StaffAccount?.Status != StaffStatus.Inactive)
            .Select(d => new DoctorStatusRow(d.DoctorId, d.StaffAccount?.FullName ?? "", byDoctor.TryGetValue(d.DoctorId, out var s) ? s : DoctorDayStatusEnum.Available))
            .ToList();
        return new DoctorStatusBoard(
            rows.Count(r => r.Status == DoctorDayStatusEnum.Available),
            rows.Count(r => r.Status == DoctorDayStatusEnum.RunningLate),
            rows.Count(r => r.Status == DoctorDayStatusEnum.UnavailableToday),
            rows);
    }

    public Task<StaffResult> SetAsync(Guid doctorId, DoctorDayStatusEnum status, CancellationToken ct = default) =>
        SetManyAsync([doctorId], status, ct);

    public async Task<StaffResult> SetManyAsync(IEnumerable<Guid> doctorIds, DoctorDayStatusEnum status, CancellationToken ct = default)
    {
        var ids = doctorIds.Distinct().ToList();
        if (ids.Count == 0) return StaffResult.Ok();
        if (!Enum.IsDefined(status)) return StaffResult.Fail("That status change didn’t save. Please try again.");
        var today = ClinicClock.Today;
        var now = DateTimeOffset.UtcNow;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var known = await db.Doctors.Where(d => ids.Contains(d.DoctorId)).Select(d => d.DoctorId).ToListAsync(ct);
        if (known.Count != ids.Count) return StaffResult.Fail("Doctor not found.");
        var existing = await db.DoctorDayStatuses.Where(s => ids.Contains(s.DoctorId) && s.StatusDate == today).ToListAsync(ct);
        foreach (var id in ids)
        {
            var row = existing.FirstOrDefault(s => s.DoctorId == id);
            if (row is null)
            {
                row = new DoctorDayStatus { Id = Guid.NewGuid(), DoctorId = id, StatusDate = today, Status = status, CreatedAt = now, UpdatedAt = now };
                db.DoctorDayStatuses.Add(row);
            }
            else
            {
                row.Status = status;
                row.UpdatedAt = now;
            }
            await _outbox.EnqueueAsync(db, new SyncOutboxEntry
            {
                OperationId = Guid.NewGuid(),
                Aggregate = "doctor_day_status",
                EntityId = row.Id,
                OperationType = "upsert",
                PayloadJson = $$"""{"doctorId":"{{id}}","date":"{{today:yyyy-MM-dd}}","status":"{{status}}"}""",
                CreatedAt = now,
            }, ct);
        }
        await db.SaveChangesAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync?.RequestSync();
        //JJQR - END
        _events.Publish(new ClinicEvent(ClinicEventKind.QueueUpdated));
        return StaffResult.Ok();
    }
}
//JJQR - END
