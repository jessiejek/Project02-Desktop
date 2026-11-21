// JJQR - AI (Nov 21, 2026) - A-STAFF. wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs).
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminStaffViewModel : AdminViewModelBase
{
    readonly IAdminStaffService _staff;

    public AdminStaffViewModel(IAdminStaffService staff)
    {
        _staff = staff;
        SearchCommand = new Command(async () => await LoadAsync());
        OpenInviteCommand = new Command(() => { InviteOpen = true; Raise(nameof(InviteOpen)); });
        CancelInviteCommand = new Command(() => { InviteOpen = false; Raise(nameof(InviteOpen)); });
        SaveInviteCommand = new Command(async () => await InviteAsync());
        OpenEditCommand = new Command<StaffListRow>(row =>
        {
            Editing = row;
            EditName = row.Name;
            EditContact = row.Contact == "—" ? "" : row.Contact;
            Raise(nameof(Editing)); Raise(nameof(EditName)); Raise(nameof(EditContact)); Raise(nameof(IsEditing));
        });
        CancelEditCommand = new Command(() => { Editing = null; Raise(nameof(Editing)); Raise(nameof(IsEditing)); });
        SaveEditCommand = new Command(async () => await SaveEditAsync());
        RevokeCommand = new Command<StaffListRow>(async row => await RevokeAsync(row));
        DeactivateCommand = new Command<StaffListRow>(async row => await SetStatusAsync(row, StaffStatus.Inactive));
        ReactivateCommand = new Command<StaffListRow>(async row => await SetStatusAsync(row, StaffStatus.Active));
    }

    public string Query { get => _query; set { _query = value; Raise(); } }
    string _query = "";
    public IReadOnlyList<string> RoleOptions { get; } = ["All roles", "Staff", "Admin"];
    //JJQR - AI (21 Nov 2026) - web /admin/staff lists role=Staff only (doctors live under Doctors)
    public string SelectedRole { get; set; } = "Staff";
    //JJQR - END
    public IReadOnlyList<string> InviteRoleOptions { get; } = ["Staff", "Admin"];
    public bool InviteOpen { get; private set; }
    public string InviteEmail { get; set; } = "";
    public string InviteName { get; set; } = "";
    public string InviteRole { get; set; } = "Staff";
    public string InviteContact { get; set; } = "";
    public string InvitePassword { get; set; } = "";
    public StaffListRow? Editing { get; private set; }
    public bool IsEditing => Editing is not null;
    public string EditName { get; set; } = "";
    public string EditContact { get; set; } = "";
    public ObservableCollection<StaffListRow> Items { get; } = [];
    public ICommand SearchCommand { get; }
    public ICommand OpenInviteCommand { get; }
    public ICommand CancelInviteCommand { get; }
    public ICommand SaveInviteCommand { get; }
    public ICommand OpenEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand RevokeCommand { get; }
    public ICommand DeactivateCommand { get; }
    public ICommand ReactivateCommand { get; }

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var role = SelectedRole == "All roles" ? null : SelectedRole;
            var page = await _staff.SearchAsync(Query, role, page: 1, pageSize: 100, sort: "name");
            Items.Clear();
            foreach (var s in page.Items)
            {
                Items.Add(new StaffListRow(
                    s.StaffId, s.UserId, s.FullName, s.Email, Dash(s.ContactNumber),
                    s.Role.ToString(), StatusText(s.Status), s.Status == StaffStatus.Invited,
                    s.Status is StaffStatus.Active or StaffStatus.OnLeave,
                    s.Status == StaffStatus.Inactive,
                    //JJQR - AI (21 Nov 2026)
                    s.Status.ToString()));
                    //JJQR - END
            }
            StatusMessage = page.TotalCount == 0 ? "No staff accounts match this search." : $"{page.TotalCount} account{(page.TotalCount == 1 ? "" : "s")}";
        });
    }

    async Task InviteAsync()
    {
        await RunAsync(async () =>
        {
            var result = await _staff.InviteAsync(new StaffInviteRequest(
                InviteEmail, InviteName, InviteRole,
                ContactNumber: string.IsNullOrWhiteSpace(InviteContact) ? null : InviteContact,
                InitialPassword: string.IsNullOrWhiteSpace(InvitePassword) ? null : InvitePassword));
            if (!result.Succeeded) { Error = result.Error; return; }
            InviteOpen = false; Raise(nameof(InviteOpen));
            InviteEmail = InviteName = InviteContact = InvitePassword = "";
            InviteRole = "Staff";
            Raise(nameof(InviteEmail)); Raise(nameof(InviteName)); Raise(nameof(InviteContact));
            Raise(nameof(InvitePassword)); Raise(nameof(InviteRole));
            StatusMessage = result.Value!.PasswordSet
                ? "Staff invited and a local password was set."
                : "Staff invited. Set a password later — this node has no mail send.";
            await LoadAsync();
        });
    }

    //JJQR - AI (21 Nov 2026) - web /admin/staff actions: inline invite (Full name, Email; role Staff), Activate/Deactivate toggle, Revoke invite.
    // Errors go to the form alert (invite) or the Team alert (rows), with the web wording.
    public string InviteError { get; private set; } = "";
    public string RowError { get; private set; } = "";
    public Guid? BusyId { get; private set; }
    public bool CanSendInvite => !string.IsNullOrWhiteSpace(InviteName) && !string.IsNullOrWhiteSpace(InviteEmail);

    public async Task<bool> SendWebInviteAsync()
    {
        if (!CanSendInvite) return false;
        InviteError = "";
        IsBusy = true;
        try
        {
            var result = await _staff.InviteAsync(new StaffInviteRequest(InviteEmail.Trim().ToLowerInvariant(), InviteName, "Staff"));
            if (!result.Succeeded) { InviteError = result.Error ?? "The invite could not be sent. Try again."; return false; }
            InviteName = ""; InviteEmail = "";
            Raise(nameof(InviteName)); Raise(nameof(InviteEmail));
        }
        catch (Exception ex) { InviteError = ex.Message; return false; }
        finally { IsBusy = false; Raise(nameof(InviteError)); }
        await LoadAsync();
        return true;
    }

    public async Task ToggleStatusAsync(StaffListRow row)
    {
        var next = row.StatusKey == nameof(StaffStatus.Active) ? StaffStatus.Inactive : StaffStatus.Active;
        BusyId = row.StaffId; RowError = "";
        try
        {
            var result = await _staff.UpdateAsync(row.StaffId, row.Name, row.Contact == "—" ? null : row.Contact, next);
            if (!result.Succeeded) RowError = AdminOnlineOnly.IsNeedsInternet(result.Error) ? result.Error! //JJQR - AI (21 Nov 2026) offline: say so //JJQR - END
                : $"Could not {(next == StaffStatus.Active ? "activate" : "deactivate")} {row.Name}. Try again.";
        }
        catch { RowError = $"Could not {(next == StaffStatus.Active ? "activate" : "deactivate")} {row.Name}. Try again."; }
        finally { BusyId = null; Raise(nameof(RowError)); }
        await LoadAsync();
    }

    public async Task RevokeWebAsync(StaffListRow row)
    {
        BusyId = row.StaffId; RowError = "";
        try
        {
            var result = await _staff.RevokeInviteAsync(row.UserId);
            if (!result.Succeeded) RowError = result.Error ?? "Could not revoke the invite. Try again.";
        }
        catch { RowError = "Could not revoke the invite. Try again."; }
        finally { BusyId = null; Raise(nameof(RowError)); }
        await LoadAsync();
    }
    //JJQR - END

    async Task SaveEditAsync()
    {
        if (Editing is null) return;
        await RunAsync(async () =>
        {
            var result = await _staff.UpdateAsync(Editing.StaffId, EditName, string.IsNullOrWhiteSpace(EditContact) ? null : EditContact, null);
            if (!result.Succeeded) { Error = result.Error; return; }
            Editing = null; Raise(nameof(Editing)); Raise(nameof(IsEditing));
            StatusMessage = "Staff account updated.";
            await LoadAsync();
        });
    }

    async Task RevokeAsync(StaffListRow row)
    {
        if (Shell.Current is Page page)
        {
            var ok = await page.DisplayAlertAsync("Revoke this invite?", $"{row.Name} ({row.Email}) will be removed and will not be able to accept the invite.", "Revoke invite", "Back");
            if (!ok) return;
        }
        await RunAsync(async () =>
        {
            var result = await _staff.RevokeInviteAsync(row.UserId);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Invite revoked.";
            await LoadAsync();
        });
    }

    async Task SetStatusAsync(StaffListRow row, StaffStatus status)
    {
        if (Shell.Current is Page page)
        {
            var ok = await page.DisplayAlertAsync(
                status == StaffStatus.Inactive ? "Deactivate this account?" : "Reactivate this account?",
                status == StaffStatus.Inactive ? $"{row.Name} will lose access and their signed-in sessions will be revoked." : $"{row.Name} will be able to sign in again.",
                status == StaffStatus.Inactive ? "Deactivate" : "Reactivate",
                "Back");
            if (!ok) return;
        }
        await RunAsync(async () =>
        {
            var result = await _staff.UpdateAsync(row.StaffId, row.Name, row.Contact == "—" ? null : row.Contact, status);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = status == StaffStatus.Inactive ? "Account deactivated." : "Account reactivated.";
            await LoadAsync();
        });
    }
}

public sealed record StaffListRow(
    Guid StaffId, Guid UserId, string Name, string Email, string Contact, string Role, string Status,
    bool CanRevoke, bool CanDeactivate, bool CanReactivate,
    //JJQR - AI (21 Nov 2026) - raw status for the StatusPill
    string StatusKey = "");
    //JJQR - END
// JJQR - END
