//JJQR - AI (21 Nov 2026) - Api implementation of IAdminDashboardService, assembled exactly like the web /admin/dashboard (there is no single dashboard endpoint):
//   GET /api/reports/daily-booking-summary · /api/reports/unpaid-completed-visits · /api/reports/pending-follow-ups · GET /api/doctors · GET /api/bookings
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminDashboardService(IApiClient api) : IAdminDashboardService
{
    public async Task<AdminDashboard> LoadAsync(CancellationToken ct = default)
    {
        var today = ClinicClock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var in7Days = today.AddDays(7);
        var reports = new ApiAdminReportService(api);

        var dailyTask = reports.DailyBookingSummaryAsync(ct);
        var unpaidTask = reports.UnpaidCompletedVisitsAsync(ct);
        var followTask = reports.PendingFollowUpsAsync(ct);
        var doctorsTask = api.GetAsync<List<Doctor>>("/api/doctors", null, ct);
        var bookingsTask = api.GetAsync<List<Booking>>("/api/bookings", null, ct);
        await Task.WhenAll(dailyTask, unpaidTask, followTask, doctorsTask, bookingsTask);

        var daily = dailyTask.Result;
        var all = bookingsTask.Result ?? [];
        var doctors = doctorsTask.Result ?? [];
        var todaySummary = daily.FirstOrDefault(r => r.AppointmentDate == today);
        var monthSummary = daily.Where(r => r.AppointmentDate >= monthStart).ToList();
        var monthBookings = all.Where(b => b.AppointmentDate >= monthStart).ToList();
        var recent = all.OrderByDescending(b => b.CreatedAt).Take(20).ToList();
        var countByDoctor = recent.GroupBy(b => b.DoctorId).ToDictionary(g => g.Key, g => g.Count());

        return new AdminDashboard(
            today,
            todaySummary?.TotalBookings ?? 0,
            todaySummary?.Revenue ?? 0m,
            todaySummary?.NoShowCount ?? 0,
            all.Count(b => b.Status == BookingStatus.ProofSubmitted),
            unpaidTask.Result.Count,
            monthBookings.Count(b => b.Status == BookingStatus.OnHold),
            followTask.Result.Count(f => f.FollowUpDate >= today && f.FollowUpDate <= in7Days),
            monthSummary.Sum(d => d.Revenue),
            monthBookings.Count,
            doctors.Select(d => new DashboardDoctorLoad(d.DoctorId, d.StaffAccount?.FullName ?? "", countByDoctor.GetValueOrDefault(d.DoctorId))).ToList(),
            recent.Select(b => new DashboardRecentBooking(
                b.BookingId,
                ApiAdmin.PatientName(b.Patient),
                b.Doctor?.StaffAccount?.FullName ?? "",
                b.SlotStartTime,
                b.QueueNumber,
                b.Status)).ToList());
    }
}
//JJQR - END
