// JJQR - AI (Nov 21, 2026) - ported from ReportsController (clinic-wide admin views).
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Admin;

public sealed record DailySummaryRow(
    DateOnly AppointmentDate,
    int TotalBookings,
    int CompletedCount,
    int PaidCount,
    int UnpaidCount,
    int NoShowCount,
    decimal Revenue);

public sealed record UnpaidCompletedRow(
    Guid BookingId,
    Guid PatientId,
    string PatientCode,
    string PatientName,
    Guid DoctorId,
    string DoctorName,
    DateOnly AppointmentDate,
    decimal AmountDue,
    PaymentStatus? PaymentStatus);

public sealed record PendingFollowUpRow(
    Guid FollowUpId,
    Guid PatientId,
    string PatientName,
    Guid DoctorId,
    string DoctorName,
    DateOnly FollowUpDate,
    string? Reason,
    FollowUpStatus? Status);

public sealed record DoctorEarningsRow(Guid DoctorId, string? Period, int CompletedVisits, decimal GrossBilled, decimal Collected, decimal Waived);

public sealed record DoctorRatingRow(Guid DoctorId, decimal AverageRating, int ReviewCount);

public interface IAdminReportService
{
    Task<IReadOnlyList<DailySummaryRow>> DailyBookingSummaryAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UnpaidCompletedRow>> UnpaidCompletedVisitsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PendingFollowUpRow>> PendingFollowUpsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DoctorEarningsRow>> DoctorEarningsAsync(Guid? doctorId, CancellationToken ct = default);
    Task<IReadOnlyList<DoctorRatingRow>> DoctorRatingsAsync(CancellationToken ct = default);
}

public sealed class AdminReportService : IAdminReportService
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;

    public AdminReportService(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<DailySummaryRow>> DailyBookingSummaryAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        try
        {
            var views = await db.VDailyBookingSummaries.AsNoTracking().ToListAsync(ct);
            if (views.Count > 0)
            {
                return views
                    .Where(v => v.AppointmentDate is not null)
                    .Select(v => new DailySummaryRow(
                        v.AppointmentDate!.Value,
                        v.TotalBookings ?? 0,
                        v.CompletedCount ?? 0,
                        v.PaidCount ?? 0,
                        v.UnpaidCount ?? 0,
                        v.NoShowCount ?? 0,
                        v.Revenue ?? 0m))
                    .OrderBy(r => r.AppointmentDate)
                    .ToList();
            }
        }
        catch (Exception)
        {
            // Fall through to the same computed path the API uses for a doctor-scoped report.
        }

        var rows = await db.Bookings.AsNoTracking()
            .Select(b => new
            {
                b.AppointmentDate,
                b.Status,
                PayStatus = b.Payment == null ? (PaymentStatus?)null : b.Payment.Status,
                Amount = b.Payment == null ? (decimal?)null : b.Payment.Amount,
                Received = b.Payment == null ? (decimal?)null : b.Payment.AmountReceived,
            }).ToListAsync(ct);

        return rows.GroupBy(r => r.AppointmentDate).OrderBy(g => g.Key).Select(g => new DailySummaryRow(
            g.Key,
            g.Count(),
            g.Count(r => r.Status == BookingStatus.Completed),
            g.Count(r => r.PayStatus == PaymentStatus.Paid),
            g.Count(r => r.PayStatus == PaymentStatus.Unpaid && r.Status != BookingStatus.Cancelled && r.Status != BookingStatus.NoShow && r.Status != BookingStatus.Expired && r.Status != BookingStatus.Rescheduled),
            g.Count(r => r.Status == BookingStatus.NoShow),
            g.Where(r => r.PayStatus == PaymentStatus.Paid).Sum(r => r.Received ?? r.Amount ?? 0m)
        )).ToList();
    }

    public async Task<IReadOnlyList<UnpaidCompletedRow>> UnpaidCompletedVisitsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        try
        {
            var views = await db.VUnpaidCompletedVisits.AsNoTracking().ToListAsync(ct);
            return views
                .Where(v => v.BookingId is not null)
                .Select(v => new UnpaidCompletedRow(
                    v.BookingId!.Value,
                    v.PatientId ?? Guid.Empty,
                    v.PatientCode ?? "",
                    v.PatientName ?? "",
                    v.DoctorId ?? Guid.Empty,
                    v.DoctorName ?? "",
                    v.AppointmentDate ?? default,
                    v.AmountDue ?? 0m,
                    v.PaymentStatus))
                .ToList();
        }
        catch (Exception)
        {
            // computed fallback
        }

        var rows = await db.Bookings.AsNoTracking()
            .Include(b => b.Patient)
            .Include(b => b.Doctor)!.ThenInclude(d => d.StaffAccount)
            .Include(b => b.Payment)
            .Where(b => b.Status == BookingStatus.Completed && b.Payment != null && b.Payment.Status == PaymentStatus.Unpaid)
            .OrderBy(b => b.AppointmentDate)
            .ToListAsync(ct);
        return rows.Select(b => new UnpaidCompletedRow(
            b.BookingId, b.PatientId, b.Patient.PatientCode, $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
            b.DoctorId, b.Doctor?.StaffAccount?.FullName ?? "", b.AppointmentDate, b.AmountDue, b.Payment?.Status)).ToList();
    }

    public async Task<IReadOnlyList<PendingFollowUpRow>> PendingFollowUpsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        try
        {
            var views = await db.VPendingFollowUps.AsNoTracking().ToListAsync(ct);
            return views
                .Where(v => v.FollowUpId is not null)
                .Select(v => new PendingFollowUpRow(
                    v.FollowUpId!.Value,
                    v.PatientId ?? Guid.Empty,
                    v.PatientName ?? "",
                    v.DoctorId ?? Guid.Empty,
                    v.DoctorName ?? "",
                    v.FollowUpDate ?? default,
                    v.Reason,
                    v.Status))
                .ToList();
        }
        catch (Exception)
        {
            // JJQR - AI (Nov 21, 2026) - table fallback when v_pending_follow_ups is unavailable; Pending only, like the web reports page.
            return await (
                from f in db.FollowUps.AsNoTracking()
                join pat in db.Patients.AsNoTracking() on f.PatientId equals pat.PatientId
                join doc in db.Doctors.AsNoTracking() on f.DoctorId equals doc.DoctorId
                join staff in db.StaffAccounts.AsNoTracking() on doc.DoctorId equals staff.StaffId
                where f.Status == FollowUpStatus.Pending
                orderby f.FollowUpDate
                select new PendingFollowUpRow(
                    f.Id,
                    f.PatientId,
                    (pat.FirstName + " " + pat.LastName).Trim(),
                    f.DoctorId,
                    staff.FullName,
                    f.FollowUpDate,
                    f.Reason,
                    f.Status)
            ).ToListAsync(ct);
            // JJQR - END
        }
    }

    public async Task<IReadOnlyList<DoctorEarningsRow>> DoctorEarningsAsync(Guid? doctorId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var q = db.VDoctorEarnings.AsNoTracking().AsQueryable();
        if (doctorId is not null) q = q.Where(e => e.DoctorId == doctorId);
        try
        {
            var rows = await q.OrderByDescending(e => e.Period).ToListAsync(ct);
            return rows
                .Where(e => e.DoctorId is not null)
                .Select(e => new DoctorEarningsRow(e.DoctorId!.Value, e.Period, e.CompletedVisits ?? 0, e.GrossBilled ?? 0m, e.Collected ?? 0m, e.Waived ?? 0m))
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<DoctorRatingRow>> DoctorRatingsAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        try
        {
            var rows = await db.VDoctorRatings.AsNoTracking().ToListAsync(ct);
            return rows
                .Where(r => r.DoctorId is not null)
                .Select(r => new DoctorRatingRow(r.DoctorId!.Value, r.AverageRating ?? 0m, r.ReviewCount ?? 0))
                .ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    // JJQR - AI (Nov 21, 2026) - clinic-reports CSV matches Project02-fe /admin/reports exportCsv (BOM + CRLF).
    public static string? InvalidDateRange(DateOnly from, DateOnly to) =>
        from > to ? "The start date must be on or before the end date." : null;

    public static string CsvFileName(DateOnly from, DateOnly to) =>
        $"clinic-reports_{from:yyyy-MM-dd}_to_{to:yyyy-MM-dd}.csv";

    public static string BuildCsv(
        DateOnly from,
        DateOnly to,
        IEnumerable<UnpaidCompletedRow> unpaid,
        IEnumerable<DailySummaryRow> daily,
        IEnumerable<PendingFollowUpRow> followUps)
    {
        var chunks = new List<string>
        {
            "Unpaid Completed Appointments",
            Line("Appointment Date", "Patient", "Doctor", "Amount Due", "Booking ID"),
        };
        foreach (var b in unpaid)
            chunks.Add(Line(b.AppointmentDate.ToString("yyyy-MM-dd"), b.PatientName, b.DoctorName, b.AmountDue, b.BookingId));
        chunks.Add("");
        chunks.Add("Daily Booking Summary");
        chunks.Add(Line("Date", "Total", "Completed", "Paid", "Unpaid", "No-Shows", "Revenue"));
        foreach (var r in daily)
            chunks.Add(Line(r.AppointmentDate.ToString("yyyy-MM-dd"), r.TotalBookings, r.CompletedCount, r.PaidCount, r.UnpaidCount, r.NoShowCount, r.Revenue));
        chunks.Add("");
        chunks.Add("Pending Follow-Ups");
        chunks.Add(Line("Follow-up Date", "Patient", "Doctor", "Reason", "Id"));
        foreach (var f in followUps)
            chunks.Add(Line(f.FollowUpDate.ToString("yyyy-MM-dd"), f.PatientName, f.DoctorName, f.Reason, f.FollowUpId));
        return "\uFEFF" + string.Join("\r\n", chunks);
    }

    static string Line(params object?[] cells) => string.Join(",", cells.Select(EscapeCsv));

    static string EscapeCsv(object? value)
    {
        var raw = value is null ? "" : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        return raw.IndexOfAny(new[] { '"', ',', '\n', '\r' }) >= 0 ? "\"" + raw.Replace("\"", "\"\"") + "\"" : raw;
    }
    // JJQR - END
}
// JJQR - END
