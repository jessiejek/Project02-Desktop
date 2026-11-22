//JJQR - AI (21 Nov 2026) - Api implementations for the doctor's Schedule and Profile screens (same endpoints as the web):
//   schedule : GET /api/doctors/{id}, GET /api/doctors/{id}/schedules, GET /api/doctors/{id}/blocked-dates,
//              PUT /api/doctors/{id}/schedules (one day per call, upsert by day_of_week - web upsertDoctorSchedule),
//              POST /api/doctors/{id}/blocked-dates, DELETE /api/doctors/blocked-dates/{id}, GET /api/bookings?doctorId=&date= (affected count)
//   profile  : GET /api/doctors/me, PUT /api/doctors/me { bio } (the doctor's only self-edit; fee / slots / capacity / limit / license / PTR / S2 are Admin-only)
// The daily patient limit is NEVER sent from here (Admin-only). Password change stays on the Local service, which delegates to the online-first staff profile service.
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.DoctorExtra;
using static ClinicNode.Api.Services.ApiDoctorExtra;

namespace ClinicNode.Api.Services;

public sealed class ApiDoctorScheduleService(IApiClient api) : IDoctorScheduleService
{
    sealed record ScheduleDto(Guid? Id, short DayOfWeek, bool IsActive, TimeOnly StartTime, TimeOnly EndTime);
    sealed record BlockedDto(Guid Id, DateOnly BlockedDate, string? Reason);

    public async Task<DoctorAvailability?> GetAsync(Guid doctorId, CancellationToken ct = default)
    {
        DoctorEmbed? doctor;
        try { doctor = await api.GetAsync<DoctorEmbed>($"/api/doctors/{Id(doctorId)}", null, ct); }
        catch (ApiNotFoundException) { return null; }
        if (doctor is null) return null;
        var rows = await api.GetAsync<List<ScheduleDto>>($"/api/doctors/{Id(doctorId)}/schedules", null, ct) ?? [];
        // Web defaultSchedule(): all seven days inactive with blank times until rows exist.
        var days = Enumerable.Range(0, 7).Select(i =>
        {
            var r = rows.FirstOrDefault(x => x.DayOfWeek == i);
            return r is null ? new ScheduleDay((short)i, false, default, default) : new ScheduleDay((short)i, r.IsActive, r.StartTime, r.EndTime);
        }).ToList();
        var blocked = await api.GetAsync<List<BlockedDto>>($"/api/doctors/{Id(doctorId)}/blocked-dates", null, ct) ?? [];
        return new DoctorAvailability(days, doctor.DailyPatientLimit,
            blocked.OrderBy(b => b.BlockedDate).Select(b => new BlockedDateRow(b.Id, b.BlockedDate, b.Reason ?? "")).ToList());
    }

    /// <summary>Weekly hours only. <paramref name="dailyPatientLimit"/> is ignored here: the limit is Admin-only and never sent by the doctor.</summary>
    public async Task<DoctorResult> SaveAsync(Guid doctorId, IReadOnlyList<ScheduleDay> days, string? dailyPatientLimit, CancellationToken ct = default)
    {
        if (DoctorExtraRules.ScheduleError(days) is { } bad) return DoctorResult.Fail(bad);
        foreach (var d in days)
        {
            var r = await Write(() => api.SendAsync(HttpMethod.Put, $"/api/doctors/{Id(doctorId)}/schedules", new
            {
                doctor_id = doctorId,
                day_of_week = d.DayOfWeek,
                is_active = d.IsActive,
                start_time = d.StartTime.ToString("HH:mm:ss"),
                end_time = d.EndTime.ToString("HH:mm:ss"),
            }, null, ct), doctorId, "Your availability could not be fully saved. Review the hours and try again.");
            if (!r.Succeeded) return r;
        }
        return DoctorResult.Ok(doctorId);
    }

    public async Task<int> CountAffectedBookingsAsync(Guid doctorId, DateOnly date, CancellationToken ct = default)
    {
        var rows = await api.GetAsync<List<BookingDto>>("/api/bookings", Q(("doctorId", Id(doctorId)), ("date", Day(date))), ct) ?? [];
        return rows.Count(b => b.DoctorId == doctorId && b.AppointmentDate == date && b.Status != BookingStatus.Cancelled);
    }

    public Task<DoctorResult> AddBlockedDateAsync(Guid doctorId, DateOnly date, string? reason, CancellationToken ct = default)
    {
        if (date == default) return Task.FromResult(DoctorResult.Fail("This date could not be blocked. Please try again."));
        return Write(async () =>
        {
            var row = await api.SendAsync<BlockedDto>(HttpMethod.Post, $"/api/doctors/{Id(doctorId)}/blocked-dates", new
            {
                doctor_id = doctorId,
                blocked_date = Day(date),
                reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            }, null, ct);
            return row?.Id;
        }, "This date could not be blocked. Please try again.");
    }

    public Task<DoctorResult> RemoveBlockedDateAsync(Guid doctorId, Guid blockedDateId, CancellationToken ct = default) =>
        Write(() => api.SendAsync(HttpMethod.Delete, $"/api/doctors/blocked-dates/{Id(blockedDateId)}", null, null, ct),
            blockedDateId, "The blocked date could not be removed. Please try again.");
}

public sealed class ApiDoctorProfileService(IApiClient api) : IDoctorProfileService
{
    /// <summary>Synchronous: always served by the Local service (session).</summary>
    public Guid? CurrentDoctorId => throw new ApiNotSupportedException();

    public async Task<DoctorProfile?> GetMineAsync(CancellationToken ct = default)
    {
        DoctorEmbed? d;
        try { d = await api.GetAsync<DoctorEmbed>("/api/doctors/me", null, ct); }
        catch (ApiNotFoundException) { return null; }
        if (d?.DoctorId is not { } id) return null;
        return new DoctorProfile(id, d.StaffAccounts?.FullName ?? "", d.StaffAccounts?.Email ?? "", d.Specialization ?? "", d.Bio ?? "",
            d.ConsultationFee ?? 0m, d.LicenseNumber ?? "", d.PtrNumber ?? "", d.S2Number ?? "");
    }

    /// <summary>The doctor may change the bio only: PUT /api/doctors/me { bio }. Every other field of <paramref name="edit"/> is ignored.</summary>
    public Task<DoctorResult> SaveAsync(DoctorProfileEdit edit, CancellationToken ct = default) => ApiDoctorBioService.PutBioAsync(api, edit.Bio, null, ct, edit.LicenseNumber, edit.PtrNumber, edit.S2Number, decimal.TryParse((edit.ConsultationFee ?? "").Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var fee) ? fee : null); //JJQR - AI (Nov 22, 2026) - the doctor's own fee //JJQR - END

    public Task<DoctorResult> ChangePasswordAsync(string? current, string? newPassword, string? confirm, CancellationToken ct = default) =>
        throw new ApiNotSupportedException("Password change runs through the staff profile service.");
}

public sealed class ApiDoctorBioService(IApiClient api) : IDoctorBioService
{
    internal const string SaveFailed = "Some profile changes could not be saved. Please review your details and try again.";

    internal static Task<DoctorResult> PutBioAsync(IApiClient api, string? bio, Guid? doctorId, CancellationToken ct, string? license = null, string? ptr = null, string? s2 = null, decimal? consultationFee = null)
    {
        var clean = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim();
        if (clean?.Length > 2000) return Task.FromResult(DoctorResult.Fail("Bio must be 2,000 characters or fewer."));
        return Write(() => api.SendAsync(HttpMethod.Put, "/api/doctors/me", new { bio = clean, licenseNumber = license, ptrNumber = ptr, s2Number = s2, consultationFee }, null, ct), doctorId, SaveFailed);
    }

    public Task<DoctorResult> SaveBioAsync(Guid doctorId, string? bio, string? licenseNumber = null, string? ptrNumber = null, string? s2Number = null, CancellationToken ct = default) => PutBioAsync(api, bio, doctorId, ct, licenseNumber, ptrNumber, s2Number);
}
//JJQR - END
