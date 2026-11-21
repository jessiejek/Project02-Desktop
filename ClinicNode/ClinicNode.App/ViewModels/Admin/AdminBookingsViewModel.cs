// JJQR - AI (Nov 21, 2026) - A-BOOK-LIST / A-BOOK-DETAIL / A-CAL (week tab). wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs).
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Core;
using ClinicNode.Core.Admin;
using ClinicNode.Core.Events;
using ClinicNode.Core.Print;
using ClinicNode.Core.Printing;
using ClinicNode.App.Services;
using ClinicNode.App.Services.Files;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminBookingsViewModel : AdminViewModelBase
{
    readonly IAdminBookingService _bookings;
    readonly IAdminDoctorService _doctors;
    readonly IClinicEventBus _events;

    public AdminBookingsViewModel(IAdminBookingService bookings, IAdminDoctorService doctors, IClinicEventBus events)
    {
        _bookings = bookings;
        _doctors = doctors;
        _events = events;
        SearchCommand = new Command(async () => await LoadAsync());
        ShowListCommand = new Command(() => { IsWeek = false; Raise(nameof(IsWeek)); Raise(nameof(IsList)); });
        ShowWeekCommand = new Command(async () => { IsWeek = true; Raise(nameof(IsWeek)); Raise(nameof(IsList)); await LoadWeekAsync(); });
        PrevWeekCommand = new Command(async () => { WeekOffset--; await LoadWeekAsync(); });
        NextWeekCommand = new Command(async () => { WeekOffset++; await LoadWeekAsync(); });
        ThisWeekCommand = new Command(async () => { WeekOffset = 0; await LoadWeekAsync(); });
        OpenWalkInCommand = new Command(async () => await GoAsync("//admin-walk-in"));
        OpenBookingCommand = new Command<Guid>(async id => await GoAsync($"admin-booking-detail?id={id}"));
        ClearFiltersCommand = new Command(async () =>
        {
            Query = ""; SelectedDoctor = DoctorOptions[0]; SelectedStatus = StatusOptions[0]; DateFilter = null; HasDateFilter = false;
            Raise(nameof(Query)); Raise(nameof(SelectedDoctor)); Raise(nameof(SelectedStatus)); Raise(nameof(DateFilter)); Raise(nameof(HasDateFilter));
            await LoadAsync();
        });
        _events.Changed += OnEvent;
    }

    public void Detach() => _events.Changed -= OnEvent;
    //JJQR - AI (21 Nov 2026) - re-subscribe when the page shows again (remove first so it is never subscribed twice)
    public void Attach()
    {
        _events.Changed -= OnEvent;
        _events.Changed += OnEvent;
    }
    //JJQR - END
    void OnEvent(ClinicEvent ev) => MainThread.BeginInvokeOnMainThread(() => { _ = IsWeek ? LoadWeekAsync() : LoadAsync(); });

    public bool IsWeek { get; private set; }
    public bool IsList => !IsWeek;
    public int WeekOffset { get; private set; }
    public string WeekLabel { get; private set; } = "";
    public string Query { get; set; } = "";
    public DateTime? DateFilter { get; set; }
    public bool HasDateFilter { get; set; }
    public ObservableCollection<string> DoctorOptions { get; } = ["All doctors"];
    public ObservableCollection<string> StatusOptions { get; } = ["All statuses"];
    public string SelectedDoctor { get; set; } = "All doctors";
    public string SelectedStatus { get; set; } = "All statuses";
    readonly Dictionary<string, Guid> _doctorIds = new();
    public ObservableCollection<BookingRow> Items { get; } = [];
    public ObservableCollection<string> WeekCells { get; } = [];
    public ICommand SearchCommand { get; }
    public ICommand ShowListCommand { get; }
    public ICommand ShowWeekCommand { get; }
    public ICommand PrevWeekCommand { get; }
    public ICommand NextWeekCommand { get; }
    public ICommand ThisWeekCommand { get; }
    public ICommand OpenWalkInCommand { get; }
    public ICommand OpenBookingCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            if (DoctorOptions.Count == 1)
            {
                foreach (var d in await _doctors.ListAllAsync(includeInactive: true))
                {
                    var label = string.IsNullOrWhiteSpace(d.Name) ? d.Specialization : d.Name;
                    DoctorOptions.Add(label);
                    _doctorIds[label] = d.DoctorId;
                }
                foreach (var s in Enum.GetNames<BookingStatus>())
                    StatusOptions.Add(StatusText(s));
            }

            Guid? doctorId = SelectedDoctor != "All doctors" && _doctorIds.TryGetValue(SelectedDoctor, out var id) ? id : null;
            BookingStatus? status = null;
            if (SelectedStatus != "All statuses")
            {
                foreach (var name in Enum.GetNames<BookingStatus>())
                    if (StatusText(name) == SelectedStatus && Enum.TryParse(name, out BookingStatus st)) { status = st; break; }
            }
            DateOnly? date = HasDateFilter && DateFilter is { } dt ? DateOnly.FromDateTime(dt) : null;
            var page = await _bookings.ListAsync(Query, doctorId, status, date, null, null, 1, 200);
            Items.Clear();
            foreach (var b in page.Items) Items.Add(ToRow(b));
            //JJQR - AI (21 Nov 2026) - web: "N bookings[ match these filters]" and the empty message
            var filtered = !string.IsNullOrWhiteSpace(Query) || doctorId is not null || status is not null || date is not null;
            IsFiltered = filtered;
            EmptyMessage = filtered ? "No bookings match these filters." : "No bookings yet.";
            Raise(nameof(IsFiltered)); Raise(nameof(EmptyMessage));
            StatusMessage = $"{page.TotalCount} {(page.TotalCount == 1 ? "booking" : "bookings")}{(filtered ? " match these filters" : "")}";
            //JJQR - END
        });
        //JJQR - AI (21 Nov 2026) - the list is code-built like the web DataTable; tell the page to redraw
        ListFailed = HasError && Items.Count == 0;
        Loaded = true;
        ListChanged?.Invoke(this, EventArgs.Empty);
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - web /admin/bookings List tab: filter card + "N bookings" line + table; error state "Bookings unavailable"
    public event EventHandler? ListChanged;
    public bool ListFailed { get; private set; }
    public bool Loaded { get; private set; }
    public const string ListErrorText = "Bookings could not be loaded. Nothing has changed; try again.";

    public async Task ApplyFiltersAsync(string? query, string? doctor, string? status, DateTime? date)
    {
        Query = query ?? "";
        SelectedDoctor = string.IsNullOrEmpty(doctor) ? "All doctors" : doctor;
        SelectedStatus = string.IsNullOrEmpty(status) ? "All statuses" : status;
        DateFilter = date;
        HasDateFilter = date is not null;
        await LoadAsync();
    }

    public void ShowList() { IsWeek = false; Raise(nameof(IsWeek)); Raise(nameof(IsList)); }
    public async Task ShowWeekAsync() { IsWeek = true; Raise(nameof(IsWeek)); Raise(nameof(IsList)); await LoadWeekAsync(); }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - week view like the web: Mon–Sun columns, one row per active doctor (doctor column only with 2+ doctors),
    // each cell lists patient + start time (Cancelled / Expired left out), header "MMM d – MMM d, yyyy", "Back to this week" when away.
    public ObservableCollection<string> WeekDayHeaders { get; } = [];
    public ObservableCollection<WeekDoctorRow> WeekRows { get; } = [];
    public bool ShowDoctorColumn { get; private set; }
    public bool IsAwayFromThisWeek => WeekOffset != 0;
    public bool IsFiltered { get; private set; }
    public string EmptyMessage { get; private set; } = "No bookings yet.";
    public string WeekEmptyMessage { get; private set; } = "";
    //JJQR - AI (21 Nov 2026)
    public bool WeekFailed { get; private set; }
    //JJQR - END
    /// <summary>Raised after the week data is rebuilt so the page can redraw its grid.</summary>
    public event EventHandler? WeekChanged;

    public async Task LoadWeekAsync()
    {
        await RunAsync(async () =>
        {
            var today = ClinicClock.Today.ToDateTime(TimeOnly.MinValue);
            var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).AddDays(WeekOffset * 7);
            var from = DateOnly.FromDateTime(monday);
            var to = from.AddDays(6);
            var en = System.Globalization.CultureInfo.GetCultureInfo("en-PH");
            WeekLabel = $"{from.ToDateTime(TimeOnly.MinValue).ToString("MMM d", en)} – {to.ToDateTime(TimeOnly.MinValue).ToString("MMM d", en)}, {to.Year}";
            Raise(nameof(WeekLabel));
            Raise(nameof(IsAwayFromThisWeek));
            var rows = (await _bookings.ListWeekAsync(from, to))
                .Where(b => b.Status is not BookingStatus.Cancelled and not BookingStatus.Expired)
                .OrderBy(b => b.SlotStart)
                .ToList();
            var doctors = (await _doctors.ListAllAsync(includeInactive: false)).ToList();
            WeekCells.Clear();
            WeekDayHeaders.Clear();
            WeekRows.Clear();
            for (var d = 0; d < 7; d++)
            {
                var day = from.AddDays(d);
                WeekDayHeaders.Add($"{day.ToDateTime(TimeOnly.MinValue).ToString("ddd", en)} {day.Day}");
            }
            ShowDoctorColumn = doctors.Count > 1;
            WeekEmptyMessage = doctors.Count == 0 ? "No doctors found." : "";
            foreach (var doc in doctors)
            {
                var cells = new List<WeekDayCell>();
                for (var d = 0; d < 7; d++)
                {
                    var day = from.AddDays(d);
                    var visits = rows.Where(b => b.DoctorId == doc.DoctorId && b.AppointmentDate == day)
                        .Select(b => new WeekVisit(b.BookingId, string.IsNullOrWhiteSpace(b.PatientName) ? "Unknown patient" : b.PatientName, Clock(b.SlotStart)))
                        .ToList();
                    cells.Add(new WeekDayCell(day, visits));
                }
                WeekRows.Add(new WeekDoctorRow(doc.DoctorId, Dash(doc.Name), cells));
            }
            Raise(nameof(ShowDoctorColumn));
            Raise(nameof(WeekEmptyMessage));
        });
        //JJQR - AI (21 Nov 2026) - web ErrorState "Week unavailable" instead of the raw exception text
        WeekFailed = HasError;
        if (WeekFailed) Error = null;
        Raise(nameof(WeekFailed));
        //JJQR - END
        WeekChanged?.Invoke(this, EventArgs.Empty);
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026)
    internal static bool WebShowsPayment(BookingStatus status, PaymentStatus? payment) =>
        status == BookingStatus.Completed || (payment ?? PaymentStatus.Unpaid) != PaymentStatus.Unpaid;
    //JJQR - END

    static bool ShouldShowPayment(BookingStatus status) =>
        status is not BookingStatus.Cancelled and not BookingStatus.Expired and not BookingStatus.Rescheduled;

    static BookingRow ToRow(AdminBookingListItem b) => new(
        b.BookingId,
        b.PatientName,
        ClinicDateShort(b.AppointmentDate),
        Dash(b.DoctorName),
        StatusText(b.Status),
        //JJQR - AI (21 Nov 2026) - web lib/paymentDisplay.showPaymentStatus: Completed visits, or any payment that is not Unpaid
        WebShowsPayment(b.Status, b.PaymentStatus) ? StatusText(b.PaymentStatus?.ToString() ?? "Unpaid") : "—",
        //JJQR - END
        $"{b.PatientName}  ·  {b.PatientCode}",
        //JJQR - AI (21 Nov 2026) - raw values for the web table (name + mono code, StatusPill keys)
        b.PatientName, b.PatientCode, b.Status.ToString(),
        WebShowsPayment(b.Status, b.PaymentStatus) ? b.PaymentStatus?.ToString() ?? "Unpaid" : "");
        //JJQR - END
}

public sealed class AdminBookingDetailViewModel : AdminViewModelBase, IQueryAttributable
{
    readonly IAdminBookingService _bookings;
    readonly IPrintDocumentFactory? _printDocs;
    readonly IReceiptPrinter? _receiptPrinter;
    readonly IFileSaver? _files;
    Guid _id;
    BookingStatus _status;
    PaymentStatus? _paymentStatus;
    decimal _amountDue;
    decimal _totalFee;
    string? _orNumber;
    string _patientName = "";
    string _doctorName = "";
    DateOnly _appointmentDate;

    //JJQR - AI (21 Nov 2026) - allen0 I-1: wiring note resolved; wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs). //JJQR - END
    public AdminBookingDetailViewModel(IAdminBookingService bookings, IPrintDocumentFactory? printDocs = null, IReceiptPrinter? receiptPrinter = null, IFileSaver? files = null)
    {
        _bookings = bookings;
        _printDocs = printDocs;
        _receiptPrinter = receiptPrinter;
        _files = files;
        ConfirmCommand = new Command(async () => await ChangeStatus(BookingStatus.Confirmed), () => CanConfirm);
        ConfirmPaymentCommand = new Command(async () => await ConfirmPaymentAsync(), () => CanConfirmPayment);
        //JJQR - AI (21 Nov 2026) - reject/cancel/no-show/reschedule/waive/refund open the web modals the page draws (OpenModal); the old prompt flows are gone
        RejectCommand = new Command(async () => await OpenAsync("reject"), () => CanReject);
        CancelCommand = new Command(async () => await OpenAsync("cancel"), () => CanCancel);
        //JJQR - END
        CompleteCommand = new Command(async () => await ChangeStatus(BookingStatus.Completed), () => CanComplete);
        //JJQR - AI (21 Nov 2026)
        NoShowCommand = new Command(async () => await OpenAsync("noshow"), () => CanNoShow);
        OpenRescheduleCommand = new Command(async () => await OpenAsync("reschedule"), () => CanReschedule);
        WaiveCommand = new Command(async () => await OpenAsync("waive"), () => CanWaive);
        RefundCommand = new Command(async () => await OpenAsync("refund"), () => CanRefund);
        RetryLoadCommand = new Command(async () => { LoadFailed = false; Raise(nameof(LoadFailed)); Raise(nameof(ShowContent)); await LoadAsync(); });
        //JJQR - END
        PrintReceiptCommand = new Command(async () => await PrintReceiptAsync(), () => CanPrint);
        PrintSummaryCommand = new Command(async () => await PrintSummaryAsync(), () => CanPrint);
        OpenPatientCommand = new Command(async () => { if (PatientId is { } pid) await GoAsync($"admin-patient-detail?id={pid}"); });
    }

    public string Title { get; private set; } = "Booking";
    public string Progress { get; private set; } = "";
    public string PatientBlock { get; private set; } = "";
    public string VisitBlock { get; private set; } = "";
    public Guid? PatientId { get; private set; }
    public bool CanConfirm { get; private set; }
    public bool CanConfirmPayment { get; private set; }
    public bool CanReject { get; private set; }
    public bool CanCancel { get; private set; }
    public bool CanComplete { get; private set; }
    public bool CanNoShow { get; private set; }
    public bool CanReschedule { get; private set; }
    public bool CanWaive { get; private set; }
    public bool CanRefund { get; private set; }
    public bool CanPrint { get; private set; }
    //JJQR - AI (21 Nov 2026) - removed unused RescheduleOpen / RescheduleDate / RescheduleReason / SaveRescheduleCommand: the web "Reschedule Booking" modal (page code-behind) owns its date + reason //JJQR - END
    //JJQR - AI (21 Nov 2026) - web booking page fields
    public static readonly string[] TimelineSteps = ["Pending", "ProofSubmitted", "Confirmed", "Completed"];
    public bool IsProofReject { get; private set; }
    public bool HasActions { get; private set; }
    public string PatientNameText { get; private set; } = "";
    public string DateDoctorLine { get; private set; } = "";
    public string StatusKey { get; private set; } = "";
    public string? PaymentKey { get; private set; }
    public int TimelineIndex { get; private set; } = -1;
    public string PatientCodeText { get; private set; } = "—";
    public string PatientContactText { get; private set; } = "—";
    public string PatientEmailText { get; private set; } = "—";
    public string DoctorText { get; private set; } = "—";
    public string SpecialtyText { get; private set; } = "—";
    public string PaymentModeText { get; private set; } = "";
    public string AmountDueText { get; private set; } = "";
    public bool ShowOrNumber { get; private set; }
    public string OrNumberText { get; private set; } = "";
    public ICommand BackToBookingsCommand { get; } = new Command(async () => await GoAsync("//admin-bookings"));
    //JJQR - END
    //JJQR - AI (21 Nov 2026) - web ErrorState "Booking unavailable" (first load only) + modal wiring
    bool _hasLoaded;
    public bool LoadFailed { get; private set; }
    public bool ShowContent => !LoadFailed;
    public ICommand RetryLoadCommand { get; }
    /// <summary>Set by the page: draws the web modal for "reject" | "cancel" | "noshow" | "reschedule" | "waive" | "refund".</summary>
    public Func<string, Task>? OpenModal { get; set; }
    public BookingStatus Status => _status;
    public string WaiveTitle => $"Waive {Peso(_amountDue)}?";
    public string RefundAmountPlaceholder => _amountDue.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    Task OpenAsync(string key) => OpenModal?.Invoke(key) ?? Task.CompletedTask;
    //JJQR - END
    public ICommand ConfirmCommand { get; }
    public ICommand ConfirmPaymentCommand { get; }
    public ICommand RejectCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CompleteCommand { get; }
    public ICommand NoShowCommand { get; }
    public ICommand OpenRescheduleCommand { get; }
    public ICommand WaiveCommand { get; }
    public ICommand RefundCommand { get; }
    public ICommand PrintReceiptCommand { get; }
    public ICommand PrintSummaryCommand { get; }
    public ICommand OpenPatientCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var raw) && Guid.TryParse(raw?.ToString(), out var id))
            _id = id;
        //JJQR - AI (21 Nov 2026)
        _hasLoaded = false;
        //JJQR - END
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_id == Guid.Empty) return;
        //JJQR - AI (21 Nov 2026)
        var loaded = false;
        //JJQR - END
        await RunAsync(async () =>
        {
            var result = await _bookings.GetAsync(_id);
            if (!result.Succeeded || result.Value is null) { Error = result.Error ?? "Booking unavailable"; return; }
            //JJQR - AI (21 Nov 2026)
            loaded = true;
            //JJQR - END
            var d = result.Value;
            var b = d.Booking;
            _status = b.Status;
            _paymentStatus = b.PaymentStatus;
            _amountDue = d.AmountDue;
            _totalFee = d.TotalFee;
            _orNumber = d.OrNumber;
            _patientName = b.PatientName;
            _doctorName = b.DoctorName;
            _appointmentDate = b.AppointmentDate;
            Title = $"Booking for {b.PatientName}";
            Progress = StatusText(b.Status);
            PatientId = b.PatientId;
            PatientBlock = $"Patient code  {b.PatientCode}\nContact  {Dash(d.PatientContact)}\nEmail  {Dash(d.PatientEmail)}";
            VisitBlock = $"Doctor  {Dash(b.DoctorName)}\nSpecialty  {Dash(d.DoctorSpecialty)}\nVisit date  {ClinicDate(b.AppointmentDate)}  {Clock(b.SlotStart)}–{Clock(b.SlotEnd)}\nQueue  {Dash(b.QueueNumber)}\nPayment mode  {d.PaymentMode}\nAmount due  {Peso(d.AmountDue)}\nPayment  {StatusText(b.PaymentStatus?.ToString() ?? "Unpaid")}\nOR number  {Dash(d.OrNumber)}\nNotes  {Dash(d.Notes)}";
            //JJQR - AI (21 Nov 2026) - actions exactly as the web booking page shows them, per status:
            // Pending → Confirm booking / Reject booking / Cancel booking; ProofSubmitted → Confirm payment / Reject proof;
            // Confirmed → Mark complete / Mark no-show / Reschedule / Cancel booking; Completed → Print receipt / Print summary;
            // payment Unpaid → Waive payment; payment Paid → Refund payment.
            CanConfirm = _status == BookingStatus.Pending;
            CanConfirmPayment = _status == BookingStatus.ProofSubmitted;
            CanReject = _status is BookingStatus.Pending or BookingStatus.ProofSubmitted;
            IsProofReject = _status == BookingStatus.ProofSubmitted;
            CanCancel = _status is BookingStatus.Pending or BookingStatus.Confirmed;
            CanComplete = _status == BookingStatus.Confirmed;
            CanNoShow = _status == BookingStatus.Confirmed;
            CanReschedule = _status == BookingStatus.Confirmed;
            CanWaive = _paymentStatus == PaymentStatus.Unpaid;
            CanRefund = _paymentStatus == PaymentStatus.Paid;
            CanPrint = _status == BookingStatus.Completed;
            HasActions = CanConfirm || CanConfirmPayment || CanReject || CanCancel || CanComplete || CanNoShow || CanReschedule || CanWaive || CanRefund || CanPrint;

            // Header, pills, timeline and info fields (web: "Booking for" / name / "date · doctor", status + payment pills, Pending → Proof submitted → Confirmed → Completed).
            PatientNameText = string.IsNullOrWhiteSpace(b.PatientName) ? "Unknown patient" : b.PatientName;
            DateDoctorLine = $"{ClinicDate(b.AppointmentDate)} · {(string.IsNullOrWhiteSpace(b.DoctorName) ? "Doctor not recorded" : b.DoctorName)}";
            StatusKey = b.Status.ToString();
            PaymentKey = AdminBookingsViewModel.WebShowsPayment(b.Status, b.PaymentStatus) ? (b.PaymentStatus ?? PaymentStatus.Unpaid).ToString() : null;
            TimelineIndex = Array.IndexOf(TimelineSteps, b.Status.ToString());
            PatientCodeText = Dash(b.PatientCode);
            PatientContactText = Dash(d.PatientContact);
            PatientEmailText = Dash(d.PatientEmail);
            DoctorText = Dash(b.DoctorName);
            SpecialtyText = Dash(d.DoctorSpecialty);
            PaymentModeText = d.PaymentMode == PaymentMode.PayAtClinic ? "Pay at clinic" : "Online";
            AmountDueText = Peso(d.AmountDue);
            ShowOrNumber = b.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Waived && !string.IsNullOrWhiteSpace(d.OrNumber);
            OrNumberText = d.OrNumber ?? "";
            foreach (var name in new[] { nameof(IsProofReject), nameof(HasActions), nameof(PatientNameText), nameof(DateDoctorLine), nameof(StatusKey), nameof(PaymentKey),
                         nameof(TimelineIndex), nameof(PatientCodeText), nameof(PatientContactText), nameof(PatientEmailText), nameof(DoctorText), nameof(SpecialtyText),
                         nameof(PaymentModeText), nameof(AmountDueText), nameof(ShowOrNumber), nameof(OrNumberText) })
                Raise(name);
            //JJQR - END
            //JJQR - AI (21 Nov 2026) - RescheduleDate set + Raise removed here (member removed, see above) //JJQR - END
            Raise(nameof(Title)); Raise(nameof(Progress)); Raise(nameof(PatientBlock)); Raise(nameof(VisitBlock));
            Raise(nameof(CanConfirm)); Raise(nameof(CanConfirmPayment)); Raise(nameof(CanReject));
            Raise(nameof(CanCancel)); Raise(nameof(CanComplete)); Raise(nameof(CanNoShow)); Raise(nameof(CanReschedule));
            Raise(nameof(CanWaive)); Raise(nameof(CanRefund)); Raise(nameof(CanPrint));
            RefreshCanExecute();
        });
        //JJQR - AI (21 Nov 2026) - web: a booking that never loaded shows only ErrorState "Booking unavailable" with Try again
        if (loaded) _hasLoaded = true;
        else if (!_hasLoaded) { Error = null; LoadFailed = true; }
        if (loaded) LoadFailed = false;
        Raise(nameof(LoadFailed)); Raise(nameof(ShowContent)); Raise(nameof(WaiveTitle)); Raise(nameof(RefundAmountPlaceholder)); Raise(nameof(Status));
        //JJQR - END
    }

    void RefreshCanExecute()
    {
        (ConfirmCommand as Command)?.ChangeCanExecute();
        (ConfirmPaymentCommand as Command)?.ChangeCanExecute();
        (RejectCommand as Command)?.ChangeCanExecute();
        (CancelCommand as Command)?.ChangeCanExecute();
        (CompleteCommand as Command)?.ChangeCanExecute();
        (NoShowCommand as Command)?.ChangeCanExecute();
        (OpenRescheduleCommand as Command)?.ChangeCanExecute();
        (WaiveCommand as Command)?.ChangeCanExecute();
        (RefundCommand as Command)?.ChangeCanExecute();
        (PrintReceiptCommand as Command)?.ChangeCanExecute();
        (PrintSummaryCommand as Command)?.ChangeCanExecute();
    }

    bool CanTransition(BookingStatus to) => BookingStatusMachine.CanTransition(_status, to);

    async Task ChangeStatus(BookingStatus status, string? reason = null)
    {
        await RunAsync(async () =>
        {
            var result = await _bookings.UpdateStatusAsync(_id, status, reason);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = $"Status is now {StatusText(status)}.";
            await LoadAsync();
        });
    }

    //JJQR - AI (21 Nov 2026) - web parity: Confirm payment is one click (no confirm dialog); the reason/waive/refund/no-show/reschedule
    // writes run from the page's web modals and return null on success or the error text the modal shows (the modal stays open).
    async Task ConfirmPaymentAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _bookings.ConfirmPaymentAsync(_id);
            if (!result.Succeeded) { Error = result.Error ?? "The booking could not be updated. Try again."; return; }
            StatusMessage = "Payment confirmed. The booking is now Confirmed.";
            await LoadAsync();
        });
    }

    /// <summary>Reject (booking or proof) and Cancel both end the booking as Cancelled with the reason (web reason modal).</summary>
    public async Task<string?> SubmitReasonAsync(bool isCancel, string reason)
    {
        reason = (reason ?? "").Trim();
        if (isCancel ? reason.Length < 5 : reason.Length == 0) return isCancel ? "At least 5 characters." : "A reason is required.";
        var result = await _bookings.UpdateStatusAsync(_id, BookingStatus.Cancelled, reason);
        if (!result.Succeeded) return result.Error ?? "The change could not be saved. Try again.";
        StatusMessage = "Status is now " + StatusText(BookingStatus.Cancelled) + ".";
        await LoadAsync();
        return null;
    }

    public async Task<string?> SubmitNoShowAsync(string? reason)
    {
        var result = await _bookings.UpdateStatusAsync(_id, BookingStatus.NoShow, string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
        if (!result.Succeeded) return result.Error ?? "Could not update booking.";
        StatusMessage = "Status is now " + StatusText(BookingStatus.NoShow) + ".";
        await LoadAsync();
        return null;
    }

    public async Task<string?> SubmitWaiveAsync(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return "A reason is required.";
        var result = await _bookings.WaivePaymentAsync(_id, reason.Trim());
        if (!result.Succeeded) return result.Error ?? "The change could not be saved. Try again.";
        StatusMessage = "Payment waived.";
        await LoadAsync();
        return null;
    }

    public async Task<string?> SubmitRefundAsync(string amountText, string reason)
    {
        if (string.IsNullOrWhiteSpace(amountText) || string.IsNullOrWhiteSpace(reason)) return "Enter the amount and a reason.";
        if (!decimal.TryParse(amountText.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var amount)
            && !decimal.TryParse(amountText.Trim(), out amount))
            return "Enter a valid refund amount.";
        var result = await _bookings.RefundPaymentAsync(_id, amount, reason.Trim());
        if (!result.Succeeded) return result.Error ?? "The change could not be saved. Try again.";
        StatusMessage = "Payment refunded.";
        await LoadAsync();
        return null;
    }

    /// <summary>Web RescheduleBookingModal: on success the page moves to the new booking.</summary>
    public async Task<string?> SubmitRescheduleAsync(DateTime date, string? reason)
    {
        var result = await _bookings.RescheduleAsync(_id, DateOnly.FromDateTime(date), string.IsNullOrWhiteSpace(reason) ? null : reason.Trim());
        if (!result.Succeeded || result.Value is null) return result.Error ?? "Could not reschedule.";
        _id = result.Value.NewBookingId;
        StatusMessage = null;
        await LoadAsync();
        return null;
    }
    //JJQR - END

    async Task PrintReceiptAsync()
    {
        var model = new ReceiptPrintModel
        {
            OrNumber = _orNumber,
            BookingId = _id,
            PatientName = _patientName,
            DoctorName = _doctorName,
            AppointmentDate = _appointmentDate,
            Amount = _totalFee,
            PaymentStatus = StatusText(_paymentStatus?.ToString() ?? "Paid"),
            IsPendingSync = false,
        };
        var path = await WriteExportAsync(
            $"receipt_{(_orNumber ?? _id.ToString("N"))}.html",
            PrintHtmlDocument.Wrap(ReceiptDocument.Title(model), ReceiptDocument.BodyHtml(model), bare: false),
            "text/html");
        if (path is null) { StatusMessage = "Save cancelled."; return; }
        if (_receiptPrinter is not null)
        {
            await _receiptPrinter.PrintReceiptAsync(model);
            StatusMessage = $"Receipt sent to the printer. A copy was also saved at {path}.";
            return;
        }
        if (Shell.Current is Page page && _printDocs is not null)
        {
            await PrintActions.RunAsync(page, () => _printDocs.PaymentReceiptAsync(_id));
            StatusMessage = $"Receipt printed. A copy was also saved at {path}.";
            return;
        }
        StatusMessage = $"Saved receipt to {path}";
    }

    async Task PrintSummaryAsync()
    {
        var title = $"Appointment summary {_id:N}";
        var body = $"<h1>Appointment summary</h1><pre>{System.Net.WebUtility.HtmlEncode(PatientBlock + "\n\n" + VisitBlock + "\n\nStatus  " + StatusText(_status.ToString()))}</pre>";
        var html = PrintHtmlDocument.Wrap(title, body, bare: false);
        var path = await WriteExportAsync($"appointment-summary_{_id:N}.html", html, "text/html");
        if (path is null) { StatusMessage = "Save cancelled."; return; }
        if (Shell.Current is Page page && page.Handler?.MauiContext?.Services.GetService<IPrinter>() is { } printer)
        {
            await printer.PrintAsync(new PrintDocument(title, PrintPageSize.A5, html));
            StatusMessage = $"Summary printed. A copy was also saved at {path}.";
            return;
        }
        StatusMessage = $"Saved appointment summary to {path}";
        await Task.CompletedTask;
    }


    async Task<string?> WriteExportAsync(string fileName, string contents, string contentType)
    {
        if (_files is not null) return await _files.SaveAsync(fileName, contents, contentType);
        return AdminLocalExport.WriteUtf8(fileName, contents);
    }

        static bool ShouldShowPayment(BookingStatus status) =>
        status is not BookingStatus.Cancelled and not BookingStatus.Expired and not BookingStatus.Rescheduled;
}

public sealed record BookingRow(Guid BookingId, string Patient, string VisitDate, string Doctor, string Status, string Payment, string Subtitle,
    //JJQR - AI (21 Nov 2026)
    string PatientName = "", string PatientCode = "", string StatusKey = "", string PaymentKey = "");
    //JJQR - END
//JJQR - AI (21 Nov 2026) - week grid rows
public sealed record WeekVisit(Guid BookingId, string Patient, string Time);
public sealed record WeekDayCell(DateOnly Date, IReadOnlyList<WeekVisit> Visits);
public sealed record WeekDoctorRow(Guid DoctorId, string Doctor, IReadOnlyList<WeekDayCell> Days);
//JJQR - END
// JJQR - END
