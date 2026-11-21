// JJQR - AI (Nov 21, 2026) - A-REP. wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs).
//JJQR - AI (21 Nov 2026) - allen0 I-1: wiring note resolved; wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs). //JJQR - END
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Admin;
using ClinicNode.App.Services.Files;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminReportsViewModel : AdminViewModelBase
{
    readonly IAdminReportService _reports;
    readonly IAdminDoctorService _doctors;
    readonly IFileSaver? _files;
    IReadOnlyList<UnpaidCompletedRow> _unpaid = [];
    IReadOnlyList<DailySummaryRow> _daily = [];
    IReadOnlyList<PendingFollowUpRow> _followUps = [];

    //JJQR - AI (21 Nov 2026) - allen0 I-1: wiring note resolved; wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs). //JJQR - END
    public AdminReportsViewModel(IAdminReportService reports, IAdminDoctorService doctors, IFileSaver? files = null)
    {
        _reports = reports;
        _doctors = doctors;
        _files = files;
        DateFrom = ClinicClock.Today.AddDays(-30).ToDateTime(TimeOnly.MinValue);
        DateTo = ClinicClock.Today.ToDateTime(TimeOnly.MinValue);
        RefreshCommand = new Command(async () => await LoadAsync());
        ExportCommand = new Command(async () => await ExportAsync(), () => !HasRangeError);
        OpenBookingCommand = new Command<Guid>(async id => await GoAsync($"admin-booking-detail?id={id}"));
        OpenPatientCommand = new Command<Guid>(async id => await GoAsync($"admin-patient-detail?id={id}"));
    }

    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string RangeLabel { get; private set; } = "";
    public bool HasRangeError { get; private set; }
    public ObservableCollection<string> DailyRows { get; } = [];
    public ObservableCollection<UnpaidRow> UnpaidRows { get; } = [];
    public ObservableCollection<string> FollowUpRows { get; } = [];
    public ObservableCollection<string> EarningsRows { get; } = [];
    public ObservableCollection<string> RatingRows { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand OpenBookingCommand { get; }
    public ICommand OpenPatientCommand { get; }

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var from = DateOnly.FromDateTime(DateFrom);
            var to = DateOnly.FromDateTime(DateTo);
            var invalid = AdminReportService.InvalidDateRange(from, to);
            HasRangeError = invalid is not null;
            Raise(nameof(HasRangeError));
            (ExportCommand as Command)?.ChangeCanExecute();
            if (invalid is not null)
            {
                Error = invalid;
                RangeLabel = "";
                Raise(nameof(RangeLabel));
                return;
            }

            //JJQR - AI (21 Nov 2026) - web /admin/reports loads only the three report views; the desktop-only earnings / ratings lists (and the doctor
            // name lookup they need) are no longer fetched, so a failure there cannot blank the web sections.
            var names = IncludeDesktopExtras
                ? (await _doctors.ListAllAsync(includeInactive: true)).ToDictionary(d => d.DoctorId, d => Dash(d.Name))
                : new Dictionary<Guid, string>();
            //JJQR - END
            string DoctorName(Guid id) => names.GetValueOrDefault(id, "—");

            _daily = (await _reports.DailyBookingSummaryAsync())
                .Where(r => r.AppointmentDate >= from && r.AppointmentDate <= to)
                .ToList();
            DailyRows.Clear();
            foreach (var r in _daily)
            {
                DailyRows.Add($"{ClinicDateShort(r.AppointmentDate)}  ·  {r.TotalBookings} bookings  ·  {r.CompletedCount} completed  ·  {r.NoShowCount} no-shows  ·  {Peso(r.Revenue)}  ·  {r.UnpaidCount} unpaid");
            }

            _unpaid = (await _reports.UnpaidCompletedVisitsAsync())
                .Where(r => r.AppointmentDate >= from && r.AppointmentDate <= to)
                .ToList();
            UnpaidRows.Clear();
            foreach (var r in _unpaid)
            {
                UnpaidRows.Add(new UnpaidRow(
                    r.BookingId, r.PatientId,
                    $"{r.PatientName}  ·  {r.PatientCode}",
                    $"{ClinicDateShort(r.AppointmentDate)}  ·  {Dash(r.DoctorName)}  ·  {Peso(r.AmountDue)}  ·  {StatusText(r.PaymentStatus?.ToString() ?? "Unpaid")}"));
            }

            _followUps = (await _reports.PendingFollowUpsAsync())
                .Where(r => r.FollowUpDate >= from && r.FollowUpDate <= to
                    && (r.Status is null or FollowUpStatus.Pending))
                .ToList();
            FollowUpRows.Clear();
            foreach (var r in _followUps)
            {
                FollowUpRows.Add($"{ClinicDateShort(r.FollowUpDate)}  ·  {Dash(r.PatientName)}  ·  {Dash(r.DoctorName)}  ·  {Dash(r.Reason)}  ·  {StatusText(r.Status?.ToString() ?? "")}");
            }

            EarningsRows.Clear();
            //JJQR - AI (21 Nov 2026)
            if (IncludeDesktopExtras)
            //JJQR - END
            foreach (var r in await _reports.DoctorEarningsAsync(null))
            {
                EarningsRows.Add($"{DoctorName(r.DoctorId)}  ·  {Dash(r.Period)}  ·  {r.CompletedVisits} visits  ·  billed {Peso(r.GrossBilled)}  ·  collected {Peso(r.Collected)}  ·  waived {Peso(r.Waived)}");
            }

            RatingRows.Clear();
            //JJQR - AI (21 Nov 2026)
            if (IncludeDesktopExtras)
            //JJQR - END
            foreach (var r in await _reports.DoctorRatingsAsync())
            {
                RatingRows.Add($"{DoctorName(r.DoctorId)}  ·  {r.AverageRating:0.00} average  ·  {r.ReviewCount} review{(r.ReviewCount == 1 ? "" : "s")}");
            }

            var visits = _daily.Sum(r => r.TotalBookings);
            var completed = _daily.Sum(r => r.CompletedCount);
            var revenue = _daily.Sum(r => r.Revenue);
            RangeLabel = $"{ClinicDateShort(from)} – {ClinicDateShort(to)}";
            Raise(nameof(RangeLabel));
            StatusMessage = $"Revenue, unpaid visits and follow-ups for {RangeLabel}.  {visits} visits  ·  {completed} completed  ·  {Peso(revenue)} revenue.";
        });
    }

    //JJQR - AI (21 Nov 2026) - web /admin/reports: From/To, 4 stat tiles, "Completed visits not yet paid", "Pending follow-ups", "Day by day" with totals, Export CSV.
    /// <summary>Desktop-only earnings / ratings lists (removed from the screen in the 1:1 pass). Off: not loaded.</summary>
    public bool IncludeDesktopExtras { get; set; }
    // web: .filter(b => b.payment_status === "Unpaid"); names fall back to "Unknown Patient" / "Unknown Doctor"
    public IReadOnlyList<UnpaidCompletedRow> Unpaid => _unpaid
        .Where(r => r.PaymentStatus == PaymentStatus.Unpaid)
        .Select(r => r with
        {
            PatientName = string.IsNullOrWhiteSpace(r.PatientName) ? "Unknown Patient" : r.PatientName,
            DoctorName = string.IsNullOrWhiteSpace(r.DoctorName) ? "Unknown Doctor" : r.DoctorName,
        })
        .ToList();
    public IReadOnlyList<DailySummaryRow> Daily => _daily.OrderBy(r => r.AppointmentDate).ToList();
    public IReadOnlyList<PendingFollowUpRow> FollowUps => _followUps;
    public bool RangeInvalid => DateOnly.FromDateTime(DateFrom) > DateOnly.FromDateTime(DateTo);
    public bool LoadFailed { get; private set; }
    public bool Loaded { get; private set; }
    public bool CanExport => !IsBusy && (Unpaid.Count > 0 || _daily.Count > 0 || _followUps.Count > 0);

    public async Task LoadWebAsync()
    {
        if (RangeInvalid) { Loaded = true; LoadFailed = false; return; }
        Error = "";
        try { await LoadAsync(); LoadFailed = HasError; }
        catch { LoadFailed = true; }
        Loaded = true;
    }

    /// <summary>Same CSV as the web (through IFileSaver). Returns the toast text, or null when cancelled.</summary>
    public async Task<string?> ExportWebAsync()
    {
        var from = DateOnly.FromDateTime(DateFrom);
        var to = DateOnly.FromDateTime(DateTo);
        if (RangeInvalid) return null;
        try
        {
            var csv = AdminReportService.BuildCsv(from, to, Unpaid, Daily, _followUps);
            var fileName = AdminReportService.CsvFileName(from, to);
            var path = _files is null
                ? AdminLocalExport.WriteUtf8(fileName, csv)
                : await _files.SaveAsync(fileName, csv, "text/csv");
            return path is null ? null : $"Saved {path}";
        }
        catch { return "The CSV could not be saved. Try again."; }
    }
    //JJQR - END

    async Task ExportAsync()
    {
        await RunAsync(async () =>
        {
            var from = DateOnly.FromDateTime(DateFrom);
            var to = DateOnly.FromDateTime(DateTo);
            var invalid = AdminReportService.InvalidDateRange(from, to);
            if (invalid is not null) { Error = invalid; return; }
            if (_daily.Count == 0 && _unpaid.Count == 0 && _followUps.Count == 0)
                await LoadAsync();
            if (HasError) return;
            var csv = AdminReportService.BuildCsv(from, to, _unpaid, _daily, _followUps);
            var fileName = AdminReportService.CsvFileName(from, to);
            var path = _files is null
                ? AdminLocalExport.WriteUtf8(fileName, csv)
                : await _files.SaveAsync(fileName, csv, "text/csv");
            if (path is null) { StatusMessage = "Export cancelled."; return; }
            StatusMessage = $"Saved {path}";
        });
    }
}

public sealed record UnpaidRow(Guid BookingId, Guid PatientId, string Title, string Subtitle);
// JJQR - END
