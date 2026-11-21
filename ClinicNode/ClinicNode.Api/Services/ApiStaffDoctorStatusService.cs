//JJQR - AI (21 Nov 2026) - Api implementation of IDoctorStatusService, the same calls the web /staff/doctor-status makes:
//   GET /api/doctors · GET /api/doctors/day-statuses?date=<Manila today>      (every non-inactive doctor; no row = Available)
//   PUT /api/doctors/{id}/day-status { doctor_id, status_date, status, running_late_minutes:null }   (one, or several in parallel for "Select several")
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Staff;
using static ClinicNode.Api.Services.ApiStaff;

namespace ClinicNode.Api.Services;

public sealed class ApiStaffDoctorStatusService(IApiClient api) : IDoctorStatusService
{
    internal sealed record DayStatusDto(Guid DoctorId, DateOnly? StatusDate, DoctorDayStatusEnum Status);

    public async Task<DoctorStatusBoard> GetTodayAsync(CancellationToken ct = default)
    {
        var today = Day(ClinicClock.Today);
        var doctors = await api.GetAsync<List<DoctorEmbed>>("/api/doctors", null, ct) ?? [];
        var statuses = await api.GetAsync<List<DayStatusDto>>("/api/doctors/day-statuses", Q(("date", today)), ct) ?? [];
        var byDoctor = statuses.GroupBy(s => s.DoctorId).ToDictionary(g => g.Key, g => g.First().Status);
        var rows = doctors
            .Where(d => d.DoctorId is not null && !string.Equals(d.StaffAccounts?.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            .Select(d => new DoctorStatusRow(d.DoctorId!.Value, d.StaffAccounts?.FullName ?? "",
                byDoctor.TryGetValue(d.DoctorId.Value, out var s) ? s : DoctorDayStatusEnum.Available))
            .ToList();
        return new DoctorStatusBoard(
            rows.Count(r => r.Status == DoctorDayStatusEnum.Available),
            rows.Count(r => r.Status == DoctorDayStatusEnum.RunningLate),
            rows.Count(r => r.Status == DoctorDayStatusEnum.UnavailableToday),
            rows);
    }

    Task Put(Guid doctorId, DoctorDayStatusEnum status, CancellationToken ct) =>
        api.SendAsync(HttpMethod.Put, $"/api/doctors/{doctorId:D}/day-status", new
        {
            doctor_id = doctorId,
            status_date = Day(ClinicClock.Today),
            status = status.ToString(),
            running_late_minutes = (int?)null,
        }, null, ct);

    public async Task<StaffResult> SetAsync(Guid doctorId, DoctorDayStatusEnum status, CancellationToken ct = default)
    {
        var r = await Write(() => Put(doctorId, status, ct), doctorId, "That status change didn’t save. Please try again.");
        return r.Succeeded ? r : StaffResult.Fail("That status change didn’t save. Please try again.");
    }

    public async Task<StaffResult> SetManyAsync(IEnumerable<Guid> doctorIds, DoctorDayStatusEnum status, CancellationToken ct = default)
    {
        var ids = doctorIds.Distinct().ToList();
        var r = await Write(() => Task.WhenAll(ids.Select(id => Put(id, status, ct))), null, "Some doctors weren’t updated. Refresh to see what saved, then try again.");
        return r.Succeeded ? r : StaffResult.Fail("Some doctors weren’t updated. Refresh to see what saved, then try again.");
    }
}
//JJQR - END
