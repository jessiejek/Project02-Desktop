// JJQR - AI (Nov 21, 2026) - A-DOC-NEW / A-DOC-EDIT. wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs).
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminDoctorNewViewModel : AdminViewModelBase
    //JJQR - AI (21 Nov 2026)
    , IAdminDoctorForm
    //JJQR - END
{
    readonly IAdminStaffService _staff;

    public AdminDoctorNewViewModel(IAdminStaffService staff)
    {
        _staff = staff;
        SaveCommand = new Command(async () => await SaveAsync());
        CancelCommand = new Command(async () => await GoAsync("//admin-doctors"));
    }

    public string Email { get; set; } = "";
    public string FullName { get; set; } = "";
    public string ContactNumber { get; set; } = "";
    public string InitialPassword { get; set; } = "";
    public string Specialization { get; set; } = "";
    //JJQR - AI (21 Nov 2026) - web starts the fee empty
    public string ConsultationFee { get; set; } = "";
    //JJQR - END
    public string SlotDurationMinutes { get; set; } = "30";
    public string SlotCapacity { get; set; } = "1";
    public string DailyPatientLimit { get; set; } = "";
    public string Bio { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string PtrNumber { get; set; } = "";
    public string S2Number { get; set; } = "";
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public Task LoadAsync() => Task.CompletedTask;

    //JJQR - AI (21 Nov 2026) - web DoctorForm mode="create": Account / Practice / Credentials / Weekly hours (Mon–Fri 08:00–17:00 by default)
    public bool IsEdit => false;
    public string FormTitle => "Add a doctor";
    public string FormDescription => "A real invite email will be sent. The account starts as \u201cInvited\u201d and becomes Active on their first login.";
    public string FormError { get; private set; } = "";
    public ObservableCollection<ScheduleRow> Schedule { get; } = new(Enumerable.Range(0, 7).Select(i => new ScheduleRow
    {
        DayOfWeek = (short)i,
        DayName = DoctorFormRules.DayLabels[i],
        IsActive = i is not 0 and not 6,
        Start = new TimeSpan(8, 0, 0),
        End = new TimeSpan(17, 0, 0),
    }));

    public Task CancelWebAsync() => GoAsync("//admin-doctors");

    public async Task<bool> SaveWebAsync()
    {
        if (!DoctorFormRules.CanSave(this)) return false;
        if (DoctorFormRules.InvalidDay(Schedule) is { } bad) { FormError = bad; Raise(nameof(FormError)); return false; }
        FormError = ""; Raise(nameof(FormError));
        IsBusy = true;
        try
        {
            var result = await _staff.InviteAsync(new StaffInviteRequest(
                Email.Trim().ToLowerInvariant(), FullName, "Doctor",
                Doctor: new DoctorInviteProfile(
                    Specialization, DoctorFormRules.Fee(ConsultationFee), DoctorFormRules.Slot(SlotDurationMinutes),
                    Bio: string.IsNullOrWhiteSpace(Bio) ? null : Bio,
                    LicenseNumber: string.IsNullOrWhiteSpace(LicenseNumber) ? null : LicenseNumber,
                    PtrNumber: string.IsNullOrWhiteSpace(PtrNumber) ? null : PtrNumber,
                    S2Number: string.IsNullOrWhiteSpace(S2Number) ? null : S2Number,
                    Schedule: Schedule.Select(d => new ScheduleDayEdit(d.DayOfWeek, d.IsActive, TimeOnly.FromTimeSpan(d.Start), TimeOnly.FromTimeSpan(d.End))).ToList())));
            if (!result.Succeeded) { FormError = result.Error ?? "The doctor could not be invited. Try again."; Raise(nameof(FormError)); return false; }
        }
        catch (Exception ex) { FormError = ex.Message; Raise(nameof(FormError)); return false; }
        finally { IsBusy = false; }
        await GoAsync("//admin-doctors");
        return true;
    }
    //JJQR - END

    async Task SaveAsync()
    {
        await RunAsync(async () =>
        {
            if (!decimal.TryParse(ConsultationFee, out var fee)) { Error = "Consultation fee must be a number."; return; }
            if (!int.TryParse(SlotDurationMinutes, out var duration)) { Error = "Slot duration must be a whole number of minutes."; return; }
            if (!int.TryParse(SlotCapacity, out var capacity)) { Error = "Slot capacity must be a whole number."; return; }
            int? limit = string.IsNullOrWhiteSpace(DailyPatientLimit) ? null : int.TryParse(DailyPatientLimit, out var n) ? n : null;
            if (!string.IsNullOrWhiteSpace(DailyPatientLimit) && limit is null) { Error = "Daily patient limit must be a whole number."; return; }

            var result = await _staff.InviteAsync(new StaffInviteRequest(
                Email, FullName, "Doctor",
                ContactNumber: string.IsNullOrWhiteSpace(ContactNumber) ? null : ContactNumber,
                InitialPassword: string.IsNullOrWhiteSpace(InitialPassword) ? null : InitialPassword,
                Doctor: new DoctorInviteProfile(
                    Specialization, fee, duration, capacity, limit,
                    string.IsNullOrWhiteSpace(Bio) ? null : Bio,
                    string.IsNullOrWhiteSpace(LicenseNumber) ? null : LicenseNumber,
                    string.IsNullOrWhiteSpace(PtrNumber) ? null : PtrNumber,
                    string.IsNullOrWhiteSpace(S2Number) ? null : S2Number)));
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = result.Value!.PasswordSet
                ? "Doctor invited and a local password was set."
                : "Doctor invited. Set a password later — this node has no mail send.";
            await GoAsync("//admin-doctors");
        });
    }
}

public sealed class AdminDoctorEditViewModel : AdminViewModelBase, IQueryAttributable
    //JJQR - AI (21 Nov 2026)
    , IAdminDoctorForm
    //JJQR - END
{
    readonly IAdminDoctorService _doctors;
    readonly IAdminSettingsService _settings;
    Guid _id;
    public Guid DoctorStaffId => _id; //JJQR - AI (Nov 23, 2026) - DoctorId = StaffId; used by the admin Set password button //JJQR - END

    public AdminDoctorEditViewModel(IAdminDoctorService doctors, IAdminSettingsService settings)
    {
        _doctors = doctors;
        _settings = settings;
        SaveCommand = new Command(async () => await SaveAsync());
        SaveScheduleCommand = new Command(async () => await SaveScheduleAsync());
        AddBlockedCommand = new Command(async () => await AddBlockedAsync());
        RemoveBlockedCommand = new Command<Guid>(async id => await RemoveBlockedAsync(id));
        SaveServicesCommand = new Command(async () => await SaveServicesAsync());
        DeactivateCommand = new Command(async () => await SetStatusAsync(StaffStatus.Inactive), () => CanDeactivate);
        ReactivateCommand = new Command(async () => await SetStatusAsync(StaffStatus.Active), () => CanReactivate);
        BackCommand = new Command(async () => await GoAsync("//admin-doctors"));
    }

    public string Title { get; private set; } = "Edit doctor";
    public string FullName { get; set; } = "";
    public string ContactNumber { get; set; } = "";
    public string Email { get; private set; } = "";
    public string Specialization { get; set; } = "";
    public string ConsultationFee { get; set; } = "";
    public string SlotDurationMinutes { get; set; } = "30";
    public string SlotCapacity { get; set; } = "1";
    public string DailyPatientLimit { get; set; } = "";
    public string Bio { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string PtrNumber { get; set; } = "";
    public string S2Number { get; set; } = "";
    public string StatusLabel { get; private set; } = "";
    public bool CanDeactivate { get; private set; }
    public bool CanReactivate { get; private set; }
    public DateTime NewBlockedDate { get; set; } = DateTime.Today.AddDays(1);
    public string NewBlockedReason { get; set; } = "";
    public ObservableCollection<ScheduleRow> Schedule { get; } = [];
    public ObservableCollection<BlockedRow> BlockedDates { get; } = [];
    public ObservableCollection<ServiceChoiceRow> Services { get; } = [];
    public ICommand SaveCommand { get; }
    public ICommand SaveScheduleCommand { get; }
    public ICommand AddBlockedCommand { get; }
    public ICommand RemoveBlockedCommand { get; }
    public ICommand SaveServicesCommand { get; }
    public ICommand DeactivateCommand { get; }
    public ICommand ReactivateCommand { get; }
    public ICommand BackCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var raw) && Guid.TryParse(raw?.ToString(), out var id))
            _id = id;
        _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (_id == Guid.Empty) return;
        await RunAsync(async () =>
        {
            var doc = await _doctors.GetAsync(_id);
            if (doc is null) { Error = "Doctor not found."; return; }
            Title = $"Edit {Dash(doc.Name)}";
            FullName = doc.Name;
            ContactNumber = doc.ContactNumber ?? "";
            Email = doc.Email;
            Specialization = doc.Specialization;
            ConsultationFee = doc.ConsultationFee.ToString("0.##");
            SlotDurationMinutes = doc.SlotDurationMinutes.ToString();
            SlotCapacity = doc.SlotCapacity.ToString();
            DailyPatientLimit = doc.DailyPatientLimit?.ToString() ?? "";
            Bio = doc.Bio ?? "";
            LicenseNumber = doc.LicenseNumber ?? "";
            PtrNumber = doc.PtrNumber ?? "";
            S2Number = doc.S2Number ?? "";
            StatusLabel = StatusText(doc.Status);
            //JJQR - AI (21 Nov 2026)
            StatusChoice = StatusLabelFor(doc.Status);
            Raise(nameof(StatusChoice)); Raise(nameof(FormTitle));
            //JJQR - END
            CanDeactivate = doc.Status is StaffStatus.Active or StaffStatus.OnLeave or StaffStatus.Invited;
            CanReactivate = doc.Status == StaffStatus.Inactive;

            var days = await _doctors.GetSchedulesAsync(_id);
            var byDay = days.ToDictionary(d => d.DayOfWeek);
            Schedule.Clear();
            for (short i = 0; i <= 6; i++)
            {
                byDay.TryGetValue(i, out var existing);
                Schedule.Add(new ScheduleRow
                {
                    DayOfWeek = i,
                    DayName = DayName(i),
                    IsActive = existing?.IsActive ?? false,
                    Start = (existing?.StartTime ?? new TimeOnly(8, 0)).ToTimeSpan(),
                    End = (existing?.EndTime ?? new TimeOnly(17, 0)).ToTimeSpan(),
                });
            }

            BlockedDates.Clear();
            foreach (var b in await _doctors.GetBlockedDatesAsync(_id))
                BlockedDates.Add(new BlockedRow(b.Id, ClinicDate(b.BlockedDate), Dash(b.Reason)));

            var selected = new HashSet<Guid>(await _doctors.GetServiceIdsAsync(_id));
            Services.Clear();
            foreach (var s in await _settings.ListServicesAsync(activeOnly: false))
                Services.Add(new ServiceChoiceRow { ServiceId = s.ServiceId, Name = $"{s.Name}  ·  {s.Category}  ·  {Peso(s.Price)}", IsSelected = selected.Contains(s.ServiceId) });

            RaiseAll();
        });
    }

    void RaiseAll()
    {
        Raise(nameof(Title)); Raise(nameof(FullName)); Raise(nameof(ContactNumber)); Raise(nameof(Email));
        Raise(nameof(Specialization)); Raise(nameof(ConsultationFee)); Raise(nameof(SlotDurationMinutes));
        Raise(nameof(SlotCapacity)); Raise(nameof(DailyPatientLimit)); Raise(nameof(Bio));
        Raise(nameof(LicenseNumber)); Raise(nameof(PtrNumber)); Raise(nameof(S2Number));
        Raise(nameof(StatusLabel)); Raise(nameof(CanDeactivate)); Raise(nameof(CanReactivate));
        (DeactivateCommand as Command)?.ChangeCanExecute();
        (ReactivateCommand as Command)?.ChangeCanExecute();
    }

    //JJQR - AI (21 Nov 2026) - web DoctorForm mode="edit": one "Save changes" writes the profile, name + status and the weekly hours, then back to Doctors.
    // Contact number, slot capacity and daily limit are not on the web form; their current values are kept.
    public bool IsEdit => true;
    public string FormTitle => string.IsNullOrWhiteSpace(FullName) ? "Edit doctor" : $"Edit {FullName}";
    public string FormDescription => "Changes apply to booking, prescriptions and the doctor's own portal as soon as you save.";
    public string FormError { get; private set; } = "";
    public IReadOnlyList<string> StatusOptions { get; } = ["Active", "Inactive", "On leave"];
    public string StatusChoice { get; set; } = "Active";

    static string StatusLabelFor(StaffStatus s) => s switch
    {
        StaffStatus.Inactive => "Inactive",
        StaffStatus.OnLeave => "On leave",
        _ => "Active",
    };

    public Task CancelWebAsync() => GoAsync("//admin-doctors");

    public async Task<bool> SaveWebAsync()
    {
        if (!DoctorFormRules.CanSave(this)) return false;
        if (DoctorFormRules.InvalidDay(Schedule) is { } bad) { FormError = bad; Raise(nameof(FormError)); return false; }
        FormError = ""; Raise(nameof(FormError));
        IsBusy = true;
        try
        {
            var status = StatusChoice switch { "Inactive" => StaffStatus.Inactive, "On leave" => StaffStatus.OnLeave, _ => StaffStatus.Active };
            int capacity = int.TryParse(SlotCapacity, out var c) && c > 0 ? c : 1;
            int? limit = int.TryParse(DailyPatientLimit, out var l) ? l : null;
            AdminResult<AdminDoctorListItem>? saved = null;
            try
            {
                saved = await _doctors.UpdateAsync(_id, new DoctorEdit(
                    Specialization, DoctorFormRules.Fee(ConsultationFee), DoctorFormRules.Slot(SlotDurationMinutes), capacity, limit,
                    string.IsNullOrWhiteSpace(Bio) ? null : Bio,
                    string.IsNullOrWhiteSpace(LicenseNumber) ? null : LicenseNumber,
                    string.IsNullOrWhiteSpace(PtrNumber) ? null : PtrNumber,
                    string.IsNullOrWhiteSpace(S2Number) ? null : S2Number,
                    FullName: FullName,
                    ContactNumber: string.IsNullOrWhiteSpace(ContactNumber) ? null : ContactNumber,
                    Status: status));
            }
            catch (Exception ex) { FormError = string.IsNullOrWhiteSpace(ex.Message) ? "Could not save the doctor profile." : ex.Message; Raise(nameof(FormError)); return false; }
            if (!saved.Succeeded) { FormError = saved.Error ?? "Could not save the doctor profile."; Raise(nameof(FormError)); return false; }

            try
            {
                var days = Schedule.Select(d => new ScheduleDayEdit(d.DayOfWeek, d.IsActive, TimeOnly.FromTimeSpan(d.Start), TimeOnly.FromTimeSpan(d.End))).ToList();
                var hours = await _doctors.UpsertSchedulesAsync(_id, days);
                if (!hours.Succeeded) throw new InvalidOperationException(hours.Error);
            }
            catch
            {
                FormError = "The profile was saved, but the account status or weekly hours could not be updated. Try saving again.";
                Raise(nameof(FormError));
                return false;
            }
        }
        finally { IsBusy = false; }
        await GoAsync("//admin-doctors");
        return true;
    }
    //JJQR - END

    async Task SaveAsync()
    {
        await RunAsync(async () =>
        {
            if (!decimal.TryParse(ConsultationFee, out var fee)) { Error = "Consultation fee must be a number."; return; }
            if (!int.TryParse(SlotDurationMinutes, out var duration)) { Error = "Slot duration must be a whole number of minutes."; return; }
            if (!int.TryParse(SlotCapacity, out var capacity)) { Error = "Slot capacity must be a whole number."; return; }
            int? limit = string.IsNullOrWhiteSpace(DailyPatientLimit) ? null : int.TryParse(DailyPatientLimit, out var n) ? n : null;
            if (!string.IsNullOrWhiteSpace(DailyPatientLimit) && limit is null) { Error = "Daily patient limit must be a whole number."; return; }

            var result = await _doctors.UpdateAsync(_id, new DoctorEdit(
                Specialization, fee, duration, capacity, limit,
                string.IsNullOrWhiteSpace(Bio) ? null : Bio,
                string.IsNullOrWhiteSpace(LicenseNumber) ? null : LicenseNumber,
                string.IsNullOrWhiteSpace(PtrNumber) ? null : PtrNumber,
                string.IsNullOrWhiteSpace(S2Number) ? null : S2Number,
                FullName: FullName,
                ContactNumber: string.IsNullOrWhiteSpace(ContactNumber) ? null : ContactNumber));
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Doctor profile saved.";
            await LoadAsync();
        });
    }

    async Task SaveScheduleAsync()
    {
        await RunAsync(async () =>
        {
            var days = Schedule.Select(s => new ScheduleDayEdit(
                s.DayOfWeek, s.IsActive, TimeOnly.FromTimeSpan(s.Start), TimeOnly.FromTimeSpan(s.End))).ToList();
            var result = await _doctors.UpsertSchedulesAsync(_id, days);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Weekly schedule saved.";
        });
    }

    async Task AddBlockedAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _doctors.AddBlockedDateAsync(_id, DateOnly.FromDateTime(NewBlockedDate), string.IsNullOrWhiteSpace(NewBlockedReason) ? null : NewBlockedReason);
            if (!result.Succeeded) { Error = result.Error; return; }
            NewBlockedReason = ""; Raise(nameof(NewBlockedReason));
            StatusMessage = "Blocked date added.";
            await LoadAsync();
        });
    }

    async Task RemoveBlockedAsync(Guid id)
    {
        await RunAsync(async () =>
        {
            var result = await _doctors.RemoveBlockedDateAsync(id);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Blocked date removed.";
            await LoadAsync();
        });
    }

    async Task SaveServicesAsync()
    {
        await RunAsync(async () =>
        {
            var ids = Services.Where(s => s.IsSelected).Select(s => s.ServiceId).ToList();
            var result = await _doctors.SetServicesAsync(_id, ids);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Doctor services saved.";
        });
    }

    async Task SetStatusAsync(StaffStatus status)
    {
        if (Shell.Current is Page page)
        {
            var ok = await page.DisplayAlertAsync(
                status == StaffStatus.Inactive ? "Deactivate this doctor?" : "Reactivate this doctor?",
                status == StaffStatus.Inactive
                    ? "They will no longer appear as available for new bookings."
                    : "They will be available for bookings again.",
                status == StaffStatus.Inactive ? "Deactivate" : "Reactivate",
                "Back");
            if (!ok) return;
        }
        await RunAsync(async () =>
        {
            var doc = await _doctors.GetAsync(_id);
            if (doc is null) { Error = "Doctor not found."; return; }
            var result = await _doctors.UpdateAsync(_id, new DoctorEdit(
                doc.Specialization, doc.ConsultationFee, doc.SlotDurationMinutes, doc.SlotCapacity, doc.DailyPatientLimit,
                doc.Bio, doc.LicenseNumber, doc.PtrNumber, doc.S2Number, Status: status));
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = status == StaffStatus.Inactive ? "Doctor deactivated." : "Doctor reactivated.";
            await LoadAsync();
        });
    }

    static string DayName(short day) => day switch
    {
        0 => "Sunday", 1 => "Monday", 2 => "Tuesday", 3 => "Wednesday", 4 => "Thursday", 5 => "Friday", 6 => "Saturday",
        _ => day.ToString()
    };
}

//JJQR - AI (21 Nov 2026) - web components/admin/DoctorForm.tsx, shared by "Add a doctor" and "Edit {name}" (AdminDoctorFormView builds the layout)
public interface IAdminDoctorForm
{
    bool IsEdit { get; }
    string FormTitle { get; }
    string FormDescription { get; }
    string FullName { get; }
    string Email { get; }
    string Specialization { get; }
    string ConsultationFee { get; }
    string SlotDurationMinutes { get; }
    ObservableCollection<ScheduleRow> Schedule { get; }
    string FormError { get; }
    Task<bool> SaveWebAsync();
    Task CancelWebAsync();
}

public static class DoctorFormRules
{
    public static readonly string[] DayLabels = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];
    public static readonly string[] DayShort = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    /// <summary>Web formProblem (B-31): negative / non-numeric fee, or a slot length outside 5–240.</summary>
    public static string? Problem(string fee, string slot)
    {
        if (!string.IsNullOrWhiteSpace(fee) && (!decimal.TryParse(fee.Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var f) || f < 0))
            return "Consultation fee cannot be negative.";
        if (!int.TryParse((slot ?? "").Trim(), out var n) || n < 5 || n > 240)
            return "Slot duration must be a whole number between 5 and 240 minutes.";
        return null;
    }

    public static bool CanSave(IAdminDoctorForm f) =>
        Problem(f.ConsultationFee, f.SlotDurationMinutes) is null
        && !string.IsNullOrWhiteSpace(f.FullName)
        && !string.IsNullOrWhiteSpace(f.Specialization)
        && !string.IsNullOrWhiteSpace(f.ConsultationFee)
        && (f.IsEdit || !string.IsNullOrWhiteSpace(f.Email));

    /// <summary>Web: "{Day}: end time must be after start time." for the first active day that fails.</summary>
    public static string? InvalidDay(IEnumerable<ScheduleRow> days)
    {
        var bad = days.FirstOrDefault(d => d.IsActive && !(d.End > d.Start));
        return bad is null ? null : $"{DayShort[Math.Clamp((int)bad.DayOfWeek, 0, 6)]}: end time must be after start time.";
    }

    public static decimal Fee(string fee) =>
        decimal.TryParse((fee ?? "").Trim(), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0m;

    public static int Slot(string slot) => int.TryParse((slot ?? "").Trim(), out var n) && n > 0 ? n : 30;
}
//JJQR - END

public sealed class ScheduleRow : AdminViewModelBase
{
    public short DayOfWeek { get; set; }
    public string DayName { get; set; } = "";
    bool _active;
    public bool IsActive { get => _active; set { _active = value; Raise(); } }
    TimeSpan _start = new(8, 0, 0);
    public TimeSpan Start { get => _start; set { _start = value; Raise(); } }
    TimeSpan _end = new(17, 0, 0);
    public TimeSpan End { get => _end; set { _end = value; Raise(); } }
}

public sealed record BlockedRow(Guid Id, string Date, string Reason);

public sealed class ServiceChoiceRow : AdminViewModelBase
{
    public Guid ServiceId { get; set; }
    public string Name { get; set; } = "";
    bool _selected;
    public bool IsSelected { get => _selected; set { _selected = value; Raise(); } }
}
// JJQR - END
