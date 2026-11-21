//JJQR - AI (Nov 21, 2026) - Api implementation of IDoctorPerformanceService: GET /api/doctor/performance (the same single endpoint Project02-fe/src/lib/data/doctor-performance.ts reads).
// The server takes the doctor from the login (a doctor can only see themself); an admin session passes ?doctorId=, which we always send. The `today` argument is a Local-only test hook (the server uses clinic today).
using ClinicApp.Domain;
using ClinicNode.Api.Http;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.Api.Services;

public sealed class ApiDoctorPerformanceService(IApiClient api) : IDoctorPerformanceService
{
    // ── wire DTOs (snake_case; see DoctorPerformanceController) ──────────
    internal sealed record StatsDto(int PatientsSeen, int UniquePatients, int NewVisits, int FollowUpVisits, int FirstTimePatients,
        decimal Collected, decimal Billed, decimal Waived, decimal Refunded, decimal UnpaidTotal,
        int RxGroups, int MedCerts, int LabOrders, int NoShows, int Cancellations, double? AvgConsultMinutes, int AvgConsultSamples);
    internal sealed record PeriodDto(string Name, DateOnly From, DateOnly To);
    internal sealed record DayDto(DateOnly Date, int PatientsSeen, decimal Collected, decimal Billed);
    internal sealed record TopDiagnosisDto(string? Icd10Code, string Label, int Count);
    internal sealed record UnpaidVisitDto(Guid BookingId, Guid PatientId, string PatientName, DateOnly AppointmentDate, decimal AmountDue);
    internal sealed record UnpaidDto(int Count, decimal Total, List<UnpaidVisitDto>? Items);
    internal sealed record FollowUpDto(Guid FollowUpId, Guid PatientId, string PatientName, DateOnly FollowUpDate, string? Reason);
    internal sealed record FollowUpsDto(int Count, List<FollowUpDto>? Items);
    internal sealed record ResponseDto(Guid DoctorId, string Currency, string WeekStartsOn, PeriodDto Period, PeriodDto PreviousPeriod, StatsDto Current, StatsDto Previous,
        List<DayDto>? Series, List<TopDiagnosisDto>? TopDiagnoses, UnpaidDto Unpaid, FollowUpsDto FollowUpsDue);

    public async Task<DoctorPerformanceReport?> GetAsync(Guid doctorId, PerformancePeriodName period, DateOnly? today = null, CancellationToken ct = default)
    {
        ResponseDto? r;
        try
        {
            r = await api.GetAsync<ResponseDto>("/api/doctor/performance", new Dictionary<string, string?>
            {
                ["period"] = period.ToString().ToLowerInvariant(),
                ["doctorId"] = doctorId.ToString("D"),
            }, ct);
        }
        catch (ApiNotFoundException) { return null; } // admin asked for a doctor that does not exist
        if (r is null) return null;
        static DoctorPerformanceStats S(StatsDto s) => new(s.PatientsSeen, s.UniquePatients, s.NewVisits, s.FollowUpVisits, s.FirstTimePatients, s.Collected, s.Billed, s.Waived,
            s.Refunded, s.UnpaidTotal, s.RxGroups, s.MedCerts, s.LabOrders, s.NoShows, s.Cancellations, s.AvgConsultMinutes, s.AvgConsultSamples);
        static DoctorPerformancePeriod P(PeriodDto p) => new(p.Name, p.From, p.To);
        var series = (r.Series ?? []).Select(d => new DoctorPerformanceDay(d.Date, d.PatientsSeen, d.Collected, d.Billed)).ToList();
        return new DoctorPerformanceReport(
            r.DoctorId, r.Currency, r.WeekStartsOn, period, series.Count > 0 ? series[^1].Date : today ?? ClinicClock.Today,
            P(r.Period), P(r.PreviousPeriod), S(r.Current), S(r.Previous), series,
            (r.TopDiagnoses ?? []).Select(t => new DoctorTopDiagnosis(t.Icd10Code, t.Label, t.Count)).ToList(),
            new DoctorUnpaidSummary(r.Unpaid.Count, r.Unpaid.Total, (r.Unpaid.Items ?? []).Select(u => new DoctorUnpaidVisit(u.BookingId, u.PatientId, u.PatientName, u.AppointmentDate, u.AmountDue)).ToList()),
            new DoctorFollowUpSummary(r.FollowUpsDue.Count, (r.FollowUpsDue.Items ?? []).Select(f => new DoctorDueFollowUp(f.FollowUpId, f.PatientId, f.PatientName, f.FollowUpDate, f.Reason)).ToList()));
    }
}
//JJQR - END
