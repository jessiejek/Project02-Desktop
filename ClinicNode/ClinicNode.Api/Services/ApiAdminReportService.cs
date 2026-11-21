//JJQR - AI (21 Nov 2026) - Api implementation of IAdminReportService: the five report views the web /admin/reports and /admin/dashboard read (lib/data/admin.ts queryReport / queryDoctorEarnings).
//   GET /api/reports/daily-booking-summary · /unpaid-completed-visits · /pending-follow-ups · /doctor-earnings?doctorId · /doctor-ratings
// CSV is still built on this computer (AdminReportService.BuildCsv) from these rows; the web has no CSV endpoint either.
using ClinicApp.Domain.Entities;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminReportService(IApiClient api) : IAdminReportService
{
    public async Task<IReadOnlyList<DailySummaryRow>> DailyBookingSummaryAsync(CancellationToken ct = default) =>
        (await api.GetAsync<List<VDailyBookingSummary>>("/api/reports/daily-booking-summary", null, ct) ?? [])
            .Where(r => r.AppointmentDate is not null)
            .Select(r => new DailySummaryRow(r.AppointmentDate!.Value, r.TotalBookings ?? 0, r.CompletedCount ?? 0, r.PaidCount ?? 0,
                r.UnpaidCount ?? 0, r.NoShowCount ?? 0, r.Revenue ?? 0m))
            .ToList();

    public async Task<IReadOnlyList<UnpaidCompletedRow>> UnpaidCompletedVisitsAsync(CancellationToken ct = default) =>
        (await api.GetAsync<List<VUnpaidCompletedVisit>>("/api/reports/unpaid-completed-visits", null, ct) ?? [])
            .Select(r => new UnpaidCompletedRow(r.BookingId ?? Guid.Empty, r.PatientId ?? Guid.Empty, r.PatientCode ?? "", r.PatientName ?? "",
                r.DoctorId ?? Guid.Empty, r.DoctorName ?? "", r.AppointmentDate ?? default, r.AmountDue ?? 0m, r.PaymentStatus))
            .ToList();

    public async Task<IReadOnlyList<PendingFollowUpRow>> PendingFollowUpsAsync(CancellationToken ct = default) =>
        (await api.GetAsync<List<VPendingFollowUp>>("/api/reports/pending-follow-ups", null, ct) ?? [])
            .Select(r => new PendingFollowUpRow(r.FollowUpId ?? Guid.Empty, r.PatientId ?? Guid.Empty, r.PatientName ?? "",
                r.DoctorId ?? Guid.Empty, r.DoctorName ?? "", r.FollowUpDate ?? default, r.Reason, r.Status))
            .ToList();

    public async Task<IReadOnlyList<DoctorEarningsRow>> DoctorEarningsAsync(Guid? doctorId, CancellationToken ct = default) =>
        (await api.GetAsync<List<VDoctorEarnings>>("/api/reports/doctor-earnings", ApiAdmin.Q(("doctorId", doctorId)), ct) ?? [])
            .Select(r => new DoctorEarningsRow(r.DoctorId ?? Guid.Empty, r.Period, r.CompletedVisits ?? 0, r.GrossBilled ?? 0m, r.Collected ?? 0m, r.Waived ?? 0m))
            .ToList();

    public async Task<IReadOnlyList<DoctorRatingRow>> DoctorRatingsAsync(CancellationToken ct = default) =>
        (await api.GetAsync<List<VDoctorRating>>("/api/reports/doctor-ratings", null, ct) ?? [])
            .Select(r => new DoctorRatingRow(r.DoctorId ?? Guid.Empty, r.AverageRating ?? 0m, r.ReviewCount ?? 0))
            .ToList();
}
//JJQR - END
