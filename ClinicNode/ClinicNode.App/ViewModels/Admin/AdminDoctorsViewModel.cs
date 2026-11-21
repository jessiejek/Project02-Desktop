// JJQR - AI (Nov 21, 2026) - A-DOC-LIST. wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs).
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminDoctorsViewModel : AdminViewModelBase
{
    readonly IAdminDoctorService _doctors;

    public AdminDoctorsViewModel(IAdminDoctorService doctors)
    {
        _doctors = doctors;
        SearchCommand = new Command(async () => await LoadAsync());
        OpenNewCommand = new Command(async () => await GoAsync("admin-doctor-new"));
        OpenEditCommand = new Command<Guid>(async id => await GoAsync($"admin-doctor-edit?id={id}"));
        DeactivateCommand = new Command<Guid>(async id => await SetStatusAsync(id, StaffStatus.Inactive));
        ReactivateCommand = new Command<Guid>(async id => await SetStatusAsync(id, StaffStatus.Active));
    }

    public string Query { get => _query; set { _query = value; Raise(); } }
    string _query = "";
    public ObservableCollection<DoctorListRow> Items { get; } = [];
    public ICommand SearchCommand { get; }
    public ICommand OpenNewCommand { get; }
    public ICommand OpenEditCommand { get; }
    public ICommand DeactivateCommand { get; }
    public ICommand ReactivateCommand { get; }

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            //JJQR - AI (21 Nov 2026) - web lists every doctor (no search box); pageSize raised so none are cut off
            var page = await _doctors.SearchAsync(Query, page: 1, pageSize: 500, sort: "name");
            //JJQR - END
            Items.Clear();
            foreach (var d in page.Items)
            {
                Items.Add(new DoctorListRow(
                    d.DoctorId,
                    Dash(d.Name),
                    Dash(d.Email),
                    Dash(d.Specialization),
                    Peso(d.ConsultationFee),
                    StatusText(d.Status),
                    Dash(d.WorkingDays),
                    d.Status is StaffStatus.Active or StaffStatus.OnLeave or StaffStatus.Invited,
                    d.Status == StaffStatus.Inactive,
                    //JJQR - AI (21 Nov 2026)
                    d.Status.ToString()));
                    //JJQR - END
            }
            StatusMessage = page.TotalCount == 0 ? "No doctors match this search." : $"{page.TotalCount} doctor{(page.TotalCount == 1 ? "" : "s")}";
        });
    }

    //JJQR - AI (21 Nov 2026) - web Deactivate modal flow (the page shows "Deactivate {name}?" / "Keep active"); returns the error text or null
    public async Task<string?> DeactivateWebAsync(Guid doctorId)
    {
        try
        {
            var doc = await _doctors.GetAsync(doctorId);
            if (doc is null) return "The doctor could not be deactivated. Try again.";
            var result = await _doctors.UpdateAsync(doctorId, new DoctorEdit(
                doc.Specialization, doc.ConsultationFee, doc.SlotDurationMinutes, doc.SlotCapacity, doc.DailyPatientLimit,
                doc.Bio, doc.LicenseNumber, doc.PtrNumber, doc.S2Number, Status: StaffStatus.Inactive));
            if (!result.Succeeded) return AdminOnlineOnly.IsNeedsInternet(result.Error) ? result.Error! //JJQR - AI (21 Nov 2026) offline: say so //JJQR - END
                : "The doctor could not be deactivated. Try again.";
        }
        catch { return "The doctor could not be deactivated. Try again."; }
        await LoadAsync();
        return null;
    }
    //JJQR - END

    async Task SetStatusAsync(Guid doctorId, StaffStatus status)
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
            var doc = await _doctors.GetAsync(doctorId);
            if (doc is null) { Error = "Doctor not found."; return; }
            var result = await _doctors.UpdateAsync(doctorId, new DoctorEdit(
                doc.Specialization, doc.ConsultationFee, doc.SlotDurationMinutes, doc.SlotCapacity, doc.DailyPatientLimit,
                doc.Bio, doc.LicenseNumber, doc.PtrNumber, doc.S2Number, Status: status));
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = status == StaffStatus.Inactive ? "Doctor deactivated." : "Doctor reactivated.";
            await LoadAsync();
        });
    }
}

public sealed record DoctorListRow(Guid DoctorId, string Name, string Email, string Specialization, string Fee, string Status, string WorkingDays, bool CanDeactivate, bool CanReactivate,
    //JJQR - AI (21 Nov 2026) - raw status for the StatusPill
    string StatusKey = "");
    //JJQR - END
// JJQR - END
