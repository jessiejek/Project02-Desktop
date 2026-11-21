// JJQR - AI (Nov 21, 2026) - A-DASH /admin/dashboard behaviour.
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClinicNode.Core.Admin;
using ClinicNode.Core.Events;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminDashboardViewModel : AdminViewModelBase
{
    readonly IAdminDashboardService _dash;
    readonly IClinicEventBus _events;

    public AdminDashboardViewModel(IAdminDashboardService dash, IClinicEventBus events)
    {
        _dash = dash;
        _events = events;
        RefreshCommand = new Command(async () => await LoadAsync(), () => IsNotBusy);
        OpenBookingsCommand = new Command(async () => await GoAsync("//admin-bookings"));
        OpenReportsCommand = new Command(async () => await GoAsync("//admin-reports"));
        OpenBookingCommand = new Command<Guid>(async id => await GoAsync($"admin-booking-detail?id={id}"));
        _events.Changed += OnEvent;
    }

    public void Detach() => _events.Changed -= OnEvent;
    //JJQR - AI (21 Nov 2026) - re-subscribe on every appearance (the page detaches when hidden), like AdminBookings
    public void Attach() { _events.Changed -= OnEvent; _events.Changed += OnEvent; }
    public bool Loaded => _hasLoaded;
    /// <summary>Raised after every load (success or failure) so the code-built page can re-render.</summary>
    public event EventHandler? Reloaded;
    //JJQR - END

    public string Title => "Clinic overview";
    public string Description { get; private set; } = "";
    public string ErrorTitle => HasError ? "Dashboard unavailable" : "";
    public bool ShowDashboard => !HasError && _hasLoaded;
    bool _hasLoaded;
    public string AppointmentsToday { get; private set; } = "0";
    public string RevenueToday { get; private set; } = "₱0";
    public string NoShowsToday { get; private set; } = "0";
    public string PaymentProofs { get; private set; } = "0";
    public string UnpaidCompleted { get; private set; } = "0";
    public string OnHoldThisMonth { get; private set; } = "0";
    public string FollowUpsDue { get; private set; } = "0";
    public string MonthRevenue { get; private set; } = "₱0";
    public string MonthAppointments { get; private set; } = "0";
    public ObservableCollection<DoctorShareRow> DoctorLoads { get; } = [];
    public ObservableCollection<DashboardRecentBookingRow> LatestBookings { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand OpenBookingsCommand { get; }
    public ICommand OpenReportsCommand { get; }
    public ICommand OpenBookingCommand { get; }

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var d = await _dash.LoadAsync();
            Description = $"Today is {ClinicDate(d.Today)}. Figures refresh automatically when the queue or payments change.";
            AppointmentsToday = d.AppointmentsToday.ToString();
            RevenueToday = Peso(d.RevenueToday);
            NoShowsToday = d.NoShowsToday.ToString();
            PaymentProofs = d.PaymentProofsToVerify.ToString();
            UnpaidCompleted = d.UnpaidCompleted.ToString();
            OnHoldThisMonth = d.OnHoldThisMonth.ToString();
            FollowUpsDue = d.FollowUpsDueIn7Days.ToString();
            MonthRevenue = Peso(d.RevenueThisMonth);
            MonthAppointments = d.AppointmentsThisMonth.ToString();
            var max = Math.Max(1, d.DoctorLoads.Count == 0 ? 1 : d.DoctorLoads.Max(x => x.BookingCount));
            DoctorLoads.Clear();
            foreach (var row in d.DoctorLoads)
            {
                DoctorLoads.Add(new DoctorShareRow(
                    row.DoctorId,
                    Dash(row.Name),
                    row.BookingCount,
                    row.BookingCount.ToString(),
                    (double)row.BookingCount / max));
            }
            LatestBookings.Clear();
            foreach (var b in d.LatestBookings)
            {
                LatestBookings.Add(new DashboardRecentBookingRow(
                    b.BookingId,
                    Dash(b.PatientName),
                    Dash(b.DoctorName),
                    Clock(b.SlotStart),
                    Dash(b.QueueNumber),
                    StatusText(b.Status),
                    b.Status.ToString())); //JJQR - AI (21 Nov 2026) raw status for the web StatusPill //JJQR - END
            }
            _hasLoaded = true;
            RaiseAll();
        });
        if (HasError)
        {
            Error = "Dashboard data is unavailable. Please try again; no totals are shown while the request has failed.";
            _hasLoaded = true;
            RaiseAll();
        }
        Reloaded?.Invoke(this, EventArgs.Empty); //JJQR - AI (21 Nov 2026) //JJQR - END
    }

    void RaiseAll()
    {
        Raise(nameof(Description));
        Raise(nameof(ErrorTitle));
        Raise(nameof(ShowDashboard));
        Raise(nameof(AppointmentsToday));
        Raise(nameof(RevenueToday));
        Raise(nameof(NoShowsToday));
        Raise(nameof(PaymentProofs));
        Raise(nameof(UnpaidCompleted));
        Raise(nameof(OnHoldThisMonth));
        Raise(nameof(FollowUpsDue));
        Raise(nameof(MonthRevenue));
        Raise(nameof(MonthAppointments));
    }

    void OnEvent(ClinicEvent ev) =>
        MainThread.BeginInvokeOnMainThread(() => { _ = LoadAsync(); });
}

public sealed record DoctorShareRow(Guid DoctorId, string Name, int BookingCount, string CountLabel, double BarProgress);
public sealed record DashboardRecentBookingRow(Guid BookingId, string PatientName, string DoctorName, string Time, string Queue, string Status,
    string StatusKey = ""); //JJQR - AI (21 Nov 2026) raw BookingStatus name for AdminPill //JJQR - END
// JJQR - END
