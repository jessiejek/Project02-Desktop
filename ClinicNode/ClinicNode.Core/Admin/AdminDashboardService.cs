// JJQR - AI (Nov 21, 2026) - clinic-wide dashboard figures matching /admin/dashboard.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Admin;

public sealed record DashboardDoctorLoad(Guid DoctorId, string Name, int BookingCount);

public sealed record DashboardRecentBooking(
    Guid BookingId,
    string PatientName,
    string DoctorName,
    TimeOnly SlotStart,
    string? QueueNumber,
    BookingStatus Status);

public sealed record AdminDashboard(
    DateOnly Today,
    int AppointmentsToday,
    decimal RevenueToday,
    int NoShowsToday,
    int PaymentProofsToVerify,
    int UnpaidCompleted,
    int OnHoldThisMonth,
    int FollowUpsDueIn7Days,
    decimal RevenueThisMonth,
    int AppointmentsThisMonth,
    IReadOnlyList<DashboardDoctorLoad> DoctorLoads,
    IReadOnlyList<DashboardRecentBooking> LatestBookings);

public interface IAdminDashboardService
{
    Task<AdminDashboard> LoadAsync(CancellationToken ct = default);
}

public sealed class AdminDashboardService : IAdminDashboardService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly IAdminReportService _reports;
    private readonly IAdminDoctorService _doctors;

    public AdminDashboardService(IDbContextFactory<ClinicAppDbContext> factory, IAdminReportService reports, IAdminDoctorService doctors)
    {
        _factory = factory;
        _reports = reports;
        _doctors = doctors;
    }

    public async Task<AdminDashboard> LoadAsync(CancellationToken ct = default)
    {
        var today = ClinicClock.Today;
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var daily = await _reports.DailyBookingSummaryAsync(ct);
        var todaySummary = daily.FirstOrDefault(r => r.AppointmentDate == today);
        var monthSummary = daily.Where(r => r.AppointmentDate >= monthStart).ToList();

        await using var db = await _factory.CreateDbContextAsync(ct);
        var all = await db.Bookings.AsNoTracking()
            .Include(b => b.Patient)
            .Include(b => b.Doctor)!.ThenInclude(d => d.StaffAccount)
            .ToListAsync(ct);

        var monthBookings = all.Where(b => b.AppointmentDate >= monthStart).ToList();
        var recent = all.OrderByDescending(b => b.CreatedAt).Take(20).ToList();
        var unpaid = await _reports.UnpaidCompletedVisitsAsync(ct);
        var followUps = await _reports.PendingFollowUpsAsync(ct);
        var dueUntil = today.AddDays(7);
        var followUpsDue = followUps.Count(f => f.FollowUpDate >= today && f.FollowUpDate <= dueUntil);

        var doctors = await _doctors.ListAllAsync(includeInactive: true, ct);
        var countByDoctor = recent.GroupBy(b => b.DoctorId).ToDictionary(g => g.Key, g => g.Count());
        var loads = doctors.Select(d => new DashboardDoctorLoad(d.DoctorId, d.Name, countByDoctor.GetValueOrDefault(d.DoctorId))).ToList();

        var latest = recent.Select(b => new DashboardRecentBooking(
            b.BookingId,
            $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
            b.Doctor?.StaffAccount?.FullName ?? "",
            b.SlotStartTime,
            b.QueueNumber,
            b.Status)).ToList();

        return new AdminDashboard(
            today,
            todaySummary?.TotalBookings ?? all.Count(b => b.AppointmentDate == today),
            todaySummary?.Revenue ?? 0m,
            todaySummary?.NoShowCount ?? all.Count(b => b.AppointmentDate == today && b.Status == BookingStatus.NoShow),
            all.Count(b => b.Status == BookingStatus.ProofSubmitted),
            unpaid.Count,
            monthBookings.Count(b => b.Status == BookingStatus.OnHold),
            followUpsDue,
            monthSummary.Sum(d => d.Revenue),
            monthBookings.Count,
            loads,
            latest);
    }
}
// JJQR - END
