//JJQR - AI (21 Nov 2026) - Api implementation of IAdminDoctorService (web /admin/doctors, /admin/doctors/[id]/edit, components/admin/DoctorForm.tsx):
//   GET /api/doctors · GET /api/doctors/search?q&page&pageSize&sort · GET /api/doctors/{id} · PUT /api/doctors/{id} (full row: GET + patch)
//   GET/PUT /api/doctors/{id}/schedules (one PUT per day) · GET/POST /api/doctors/{id}/blocked-dates · DELETE /api/doctors/blocked-dates/{id}
//   GET/PUT /api/doctors/{id}/services · name / contact / status: GET + PUT /api/staff-accounts/{staffId}
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminDoctorService(IApiClient api) : IAdminDoctorService
{
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private async Task<List<DoctorSchedule>> SchedulesAsync(Guid doctorId, CancellationToken ct) =>
        await api.GetAsync<List<DoctorSchedule>>($"/api/doctors/{doctorId:D}/schedules", null, ct) ?? [];

    private async Task<List<AdminDoctorListItem>> WithSchedulesAsync(IEnumerable<Doctor> doctors, CancellationToken ct)
    {
        var list = doctors.ToList();
        var schedules = await Task.WhenAll(list.Select(d => SchedulesAsync(d.DoctorId, ct))); // the web loads each doctor's schedule the same way
        return list.Select((d, i) => Map(d, schedules[i])).ToList();
    }

    public async Task<AdminPage<AdminDoctorListItem>> SearchAsync(string? q, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default)
    {
        var res = await api.GetAsync<ApiPaged<Doctor>>("/api/doctors/search", ApiAdmin.Q(("q", q), ("page", page), ("pageSize", pageSize), ("sort", sort)), ct);
        var items = await WithSchedulesAsync(res?.Items ?? [], ct);
        return new AdminPage<AdminDoctorListItem>(items, res?.TotalCount ?? items.Count, res?.Page ?? page, res?.PageSize ?? pageSize);
    }

    public async Task<IReadOnlyList<AdminDoctorListItem>> ListAllAsync(bool includeInactive = true, CancellationToken ct = default)
    {
        var all = await api.GetAsync<List<Doctor>>("/api/doctors", null, ct) ?? [];
        var rows = includeInactive ? all : all.Where(d => d.StaffAccount?.Status != StaffStatus.Inactive).ToList();
        return (await WithSchedulesAsync(rows, ct)).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<AdminDoctorListItem?> GetAsync(Guid doctorId, CancellationToken ct = default)
    {
        Doctor? d;
        try { d = await api.GetAsync<Doctor>($"/api/doctors/{doctorId:D}", null, ct); }
        catch (ApiNotFoundException) { return null; }
        return d is null ? null : Map(d, await SchedulesAsync(doctorId, ct));
    }

    public async Task<AdminResult<AdminDoctorListItem>> UpdateAsync(Guid doctorId, DoctorEdit edit, CancellationToken ct = default)
    {
        try
        {
            var current = await api.GetAsync<Doctor>($"/api/doctors/{doctorId:D}", null, ct);
            if (current is null) return AdminResult<AdminDoctorListItem>.Fail("Doctor not found.");
            current.Specialization = edit.Specialization.Trim();
            current.ConsultationFee = edit.ConsultationFee;
            current.Bio = ApiAdmin.Blank(edit.Bio);
            current.LicenseNumber = ApiAdmin.Blank(edit.LicenseNumber);
            current.PtrNumber = ApiAdmin.Blank(edit.PtrNumber);
            current.S2Number = ApiAdmin.Blank(edit.S2Number);
            current.SlotDurationMinutes = edit.SlotDurationMinutes;
            current.SlotCapacity = edit.SlotCapacity;
            current.DailyPatientLimit = edit.DailyPatientLimit;
            await api.SendAsync(HttpMethod.Put, $"/api/doctors/{doctorId:D}", current, null, ct);

            if (edit.FullName is not null || edit.ContactNumber is not null || edit.Status is not null)
            {
                var staffId = current.StaffAccount?.StaffId is { } sid && sid != Guid.Empty ? sid : doctorId; // the web passes the doctor id (doctor_id == staff_id)
                var staff = await api.GetAsync<StaffAccount>($"/api/staff-accounts/{staffId:D}", null, ct);
                if (staff is not null)
                {
                    staff.FullName = (edit.FullName ?? staff.FullName).Trim();
                    staff.ContactNumber = edit.ContactNumber is null ? staff.ContactNumber : ApiAdmin.Blank(edit.ContactNumber);
                    if (edit.Status is { } st) staff.Status = st;
                    await api.SendAsync(HttpMethod.Put, $"/api/staff-accounts/{staffId:D}", staff, null, ct);
                }
            }

            var item = await GetAsync(doctorId, ct);
            return item is null
                ? AdminResult<AdminDoctorListItem>.Fail("Doctor not found after save.")
                : AdminResult<AdminDoctorListItem>.Ok(item);
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<AdminDoctorListItem>.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<ScheduleDayEdit>> GetSchedulesAsync(Guid doctorId, CancellationToken ct = default) =>
        (await SchedulesAsync(doctorId, ct)).OrderBy(s => s.DayOfWeek)
            .Select(s => new ScheduleDayEdit(s.DayOfWeek, s.IsActive, s.StartTime, s.EndTime)).ToList();

    public async Task<AdminResult> UpsertSchedulesAsync(Guid doctorId, IReadOnlyList<ScheduleDayEdit> days, CancellationToken ct = default)
    {
        try
        {
            foreach (var d in days)
                await api.SendAsync(HttpMethod.Put, $"/api/doctors/{doctorId:D}/schedules", new
                {
                    id = Guid.NewGuid(), // only used when the day row does not exist yet (client-supplied id, offline-sync safe)
                    doctor_id = doctorId,
                    day_of_week = d.DayOfWeek,
                    is_active = d.IsActive,
                    start_time = d.StartTime,
                    end_time = d.EndTime,
                }, null, ct);
            return AdminResult.Ok();
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<BlockedDateItem>> GetBlockedDatesAsync(Guid doctorId, CancellationToken ct = default) =>
        (await api.GetAsync<List<DoctorBlockedDate>>($"/api/doctors/{doctorId:D}/blocked-dates", null, ct) ?? [])
            .OrderBy(b => b.BlockedDate).Select(b => new BlockedDateItem(b.Id, b.BlockedDate, b.Reason)).ToList();

    public async Task<AdminResult<BlockedDateItem>> AddBlockedDateAsync(Guid doctorId, DateOnly date, string? reason, CancellationToken ct = default)
    {
        try
        {
            var row = await api.SendAsync<DoctorBlockedDate>(HttpMethod.Post, $"/api/doctors/{doctorId:D}/blocked-dates",
                new { id = Guid.NewGuid(), doctor_id = doctorId, blocked_date = date, reason = ApiAdmin.Blank(reason) }, null, ct);
            return row is null
                ? AdminResult<BlockedDateItem>.Fail("The blocked date could not be read back.")
                : AdminResult<BlockedDateItem>.Ok(new BlockedDateItem(row.Id, row.BlockedDate, row.Reason));
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<BlockedDateItem>.Fail(ex.Message); }
    }

    public async Task<AdminResult> RemoveBlockedDateAsync(Guid blockedDateId, CancellationToken ct = default)
    {
        try
        {
            await api.SendAsync(HttpMethod.Delete, $"/api/doctors/blocked-dates/{blockedDateId:D}", null, null, ct);
            return AdminResult.Ok();
        }
        catch (ApiNotFoundException) { return AdminResult.Fail("Blocked date not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult.Fail(ex.Message); }
    }

    public async Task<IReadOnlyList<Guid>> GetServiceIdsAsync(Guid doctorId, CancellationToken ct = default) =>
        (await api.GetAsync<List<DoctorService>>($"/api/doctors/{doctorId:D}/services", null, ct) ?? []).Select(s => s.ServiceId).ToList();

    public async Task<AdminResult> SetServicesAsync(Guid doctorId, IReadOnlyList<Guid> serviceIds, CancellationToken ct = default)
    {
        try
        {
            await api.SendAsync(HttpMethod.Put, $"/api/doctors/{doctorId:D}/services", serviceIds.Distinct().ToList(), null, ct);
            return AdminResult.Ok();
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult.Fail(ex.Message); }
    }

    private static AdminDoctorListItem Map(Doctor d, IReadOnlyList<DoctorSchedule> schedules)
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
//JJQR - END
