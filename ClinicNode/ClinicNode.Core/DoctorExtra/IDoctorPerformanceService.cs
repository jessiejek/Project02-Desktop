//JJQR - AI (Nov 21, 2026) - D-PERF: the doctor's own performance numbers (web /doctor/performance ← GET api/doctor/performance). Same shapes and rules as DoctorPerformanceController, read from the node's local SQLite.
namespace ClinicNode.Core.DoctorExtra;

public enum PerformancePeriodName { Today, Week, Month }

/// <summary>Numbers for ONE period. Money is gross PHP and only counts COMPLETED visits dated inside the period.</summary>
public sealed record DoctorPerformanceStats(
    int PatientsSeen, int UniquePatients, int NewVisits, int FollowUpVisits, int FirstTimePatients,
    decimal Collected, decimal Billed, decimal Waived, decimal Refunded, decimal UnpaidTotal,
    int RxGroups, int MedCerts, int LabOrders, int NoShows, int Cancellations,
    double? AvgConsultMinutes, int AvgConsultSamples);

public sealed record DoctorPerformancePeriod(string Name, DateOnly From, DateOnly To);
public sealed record DoctorPerformanceDay(DateOnly Date, int PatientsSeen, decimal Collected, decimal Billed);
public sealed record DoctorTopDiagnosis(string? Icd10Code, string Label, int Count);
public sealed record DoctorUnpaidVisit(Guid BookingId, Guid PatientId, string PatientName, DateOnly AppointmentDate, decimal AmountDue);
public sealed record DoctorUnpaidSummary(int Count, decimal Total, IReadOnlyList<DoctorUnpaidVisit> Items);
public sealed record DoctorDueFollowUp(Guid FollowUpId, Guid PatientId, string PatientName, DateOnly FollowUpDate, string? Reason);
public sealed record DoctorFollowUpSummary(int Count, IReadOnlyList<DoctorDueFollowUp> Items);

public sealed record DoctorPerformanceReport(
    Guid DoctorId, string Currency, string WeekStartsOn, PerformancePeriodName PeriodName, DateOnly Today,
    DoctorPerformancePeriod Period, DoctorPerformancePeriod PreviousPeriod,
    DoctorPerformanceStats Current, DoctorPerformanceStats Previous, IReadOnlyList<DoctorPerformanceDay> Series,
    IReadOnlyList<DoctorTopDiagnosis> TopDiagnoses, DoctorUnpaidSummary Unpaid, DoctorFollowUpSummary FollowUpsDue);

public interface IDoctorPerformanceService
{
    /// <summary>Everything the Performance page shows for one doctor and period. null when the doctor does not exist on this node. <paramref name="today"/> is for tests (default: clinic today, Manila).</summary>
    Task<DoctorPerformanceReport?> GetAsync(Guid doctorId, PerformancePeriodName period, DateOnly? today = null, CancellationToken ct = default);
}
//JJQR - END
