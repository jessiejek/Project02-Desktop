// JJQR - AI (Nov 21, 2026) - A-ANN. wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs).
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminAnnouncementsViewModel : AdminViewModelBase
{
    readonly IAdminAnnouncementService _svc;
    Guid? _editingId;

    public AdminAnnouncementsViewModel(IAdminAnnouncementService svc)
    {
        _svc = svc;
        OpenNewCommand = new Command(() => BeginEdit(null, "", "", true));
        CancelEditCommand = new Command(() => { FormOpen = false; _editingId = null; Raise(nameof(FormOpen)); Raise(nameof(FormTitle)); });
        SaveCommand = new Command(async () => await SaveAsync());
        EditCommand = new Command<AnnouncementRow>(row => BeginEdit(row.Id, row.Title, row.Body, row.IsActive));
        DeleteCommand = new Command<AnnouncementRow>(async row => await DeleteAsync(row));
    }

    public bool FormOpen { get; private set; }
    public string FormTitle => _editingId is null ? "New announcement" : "Edit announcement";
    public string DraftTitle { get; set; } = "";
    public string DraftBody { get; set; } = "";
    public bool DraftActive { get; set; } = true;
    public ObservableCollection<AnnouncementRow> Items { get; } = [];
    public ICommand OpenNewCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }

    void BeginEdit(Guid? id, string title, string body, bool active)
    {
        _editingId = id;
        DraftTitle = title; DraftBody = body; DraftActive = active;
        FormOpen = true;
        Raise(nameof(FormOpen)); Raise(nameof(FormTitle));
        Raise(nameof(DraftTitle)); Raise(nameof(DraftBody)); Raise(nameof(DraftActive));
    }

    public async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var rows = await _svc.ListAsync(activeOnly: false);
            //JJQR - AI (21 Nov 2026)
            Raw = rows.OrderByDescending(a => a.CreatedAt).ToList();
            Raise(nameof(Raw));
            //JJQR - END
            Items.Clear();
            foreach (var a in rows)
                Items.Add(new AnnouncementRow(a.Id, a.Title, a.Body, a.IsActive, a.IsActive ? "Active" : "Inactive", ClinicDateTime(a.UpdatedAt)));
            StatusMessage = rows.Count == 0 ? "No announcements yet." : $"{rows.Count} announcement{(rows.Count == 1 ? "" : "s")}";
        });
        //JJQR - AI (21 Nov 2026) - web loadError: only the list load itself flips the page to "Announcements unavailable"
        LoadFailed = HasError;
        Raise(nameof(LoadFailed));
        //JJQR - END
    }

    async Task SaveAsync()
    {
        await RunAsync(async () =>
        {
            var edit = new AnnouncementEdit(DraftTitle, DraftBody, DraftActive);
            var result = _editingId is { } id
                ? await _svc.UpdateAsync(id, edit)
                : await _svc.CreateAsync(edit);
            if (!result.Succeeded) { Error = result.Error; return; }
            FormOpen = false; _editingId = null;
            Raise(nameof(FormOpen));
            StatusMessage = "Announcement saved.";
            await LoadAsync();
        });
    }

    //JJQR - AI (21 Nov 2026) - web /admin/announcements: cards with Shown/Hidden switch, Edit, Delete; "New announcement" modal. Web error wording.
    public IReadOnlyList<ClinicApp.Domain.Entities.Announcement> Raw { get; private set; } = [];
    public string ListError { get; private set; } = "";
    public Guid? TogglingId { get; private set; }
    public int LiveCount => Raw.Count(a => a.IsActive);
    public bool LoadFailed { get; private set; }

    // The web updates its list in place after a toggle / save / delete and never shows the load error for it; re-read the list,
    // and if that read fails keep the current cards instead of flipping the page to the error state.
    async Task RefreshAfterWriteAsync()
    {
        try
        {
            var rows = await _svc.ListAsync(activeOnly: false);
            Raw = rows.OrderByDescending(a => a.CreatedAt).ToList();
            Raise(nameof(Raw));
        }
        catch { }
    }

    public async Task ToggleWebAsync(ClinicApp.Domain.Entities.Announcement a)
    {
        var next = !a.IsActive;
        TogglingId = a.Id; ListError = "";
        try
        {
            var result = await _svc.UpdateAsync(a.Id, new AnnouncementEdit(a.Title, a.Body, next));
            if (!result.Succeeded) ListError = AdminOnlineOnly.IsNeedsInternet(result.Error) ? result.Error! //JJQR - AI (21 Nov 2026) offline: say so //JJQR - END
                : $"\u201c{a.Title}\u201d could not be {(next ? "shown" : "hidden")}. Try again.";
        }
        catch { ListError = $"\u201c{a.Title}\u201d could not be {(next ? "shown" : "hidden")}. Try again."; }
        finally { TogglingId = null; }
        await RefreshAfterWriteAsync();
    }

    /// <summary>Null when saved; otherwise the web's error (the typed text stays in the modal).</summary>
    public async Task<string?> SaveWebAsync(Guid? id, string title, string body, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var edit = new AnnouncementEdit(title.Trim(), body.Trim(), isActive);
            var result = id is { } existing ? await _svc.UpdateAsync(existing, edit) : await _svc.CreateAsync(edit);
            if (!result.Succeeded) return AdminOnlineOnly.IsNeedsInternet(result.Error) ? result.Error! //JJQR - AI (21 Nov 2026) offline: say so //JJQR - END
                : "The announcement could not be saved. Your text is still here; try again.";
        }
        catch { return "The announcement could not be saved. Your text is still here; try again."; }
        await RefreshAfterWriteAsync();
        return null;
    }

    public async Task<string?> DeleteWebAsync(Guid id)
    {
        try
        {
            var result = await _svc.DeleteAsync(id);
            if (!result.Succeeded) return AdminOnlineOnly.IsNeedsInternet(result.Error) ? result.Error! //JJQR - AI (21 Nov 2026) offline: say so //JJQR - END
                : "The announcement could not be deleted. Try again.";
        }
        catch { return "The announcement could not be deleted. Try again."; }
        await RefreshAfterWriteAsync();
        return null;
    }
    //JJQR - END

    async Task DeleteAsync(AnnouncementRow row)
    {
        if (Shell.Current is Page page)
        {
            var ok = await page.DisplayAlertAsync("Delete this announcement?", row.Title, "Delete", "Back");
            if (!ok) return;
        }
        await RunAsync(async () =>
        {
            var result = await _svc.DeleteAsync(row.Id);
            if (!result.Succeeded) { Error = result.Error; return; }
            StatusMessage = "Announcement deleted.";
            await LoadAsync();
        });
    }
}

public sealed record AnnouncementRow(Guid Id, string Title, string Body, bool IsActive, string Status, string Updated);
// JJQR - END
