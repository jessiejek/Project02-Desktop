// JJQR - AI (Nov 21, 2026) - A-AUDIT. wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs).
using System.Collections.ObjectModel;
using System.Windows.Input;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminAuditViewModel : AdminViewModelBase
{
    readonly IAdminAuditLogService _audit;
    int _page = 1;

    public AdminAuditViewModel(IAdminAuditLogService audit)
    {
        _audit = audit;
        SearchCommand = new Command(async () => { _page = 1; await LoadAsync(); });
        NextPageCommand = new Command(async () => { if (CanNext) { _page++; await LoadAsync(); } }, () => CanNext);
        PrevPageCommand = new Command(async () => { if (CanPrev) { _page--; await LoadAsync(); } }, () => CanPrev);
        //JJQR - AI (21 Nov 2026) - web "Record type" options
        EntityOptions = ["All record types", .. Enum.GetNames<AuditEntityType>()];
        //JJQR - END
    }

    public string Query { get => _query; set { _query = value; Raise(); } }
    string _query = "";
    public IReadOnlyList<string> EntityOptions { get; }
    //JJQR - AI (21 Nov 2026)
    public string SelectedEntity { get; set; } = "All record types";
    //JJQR - END
    public string PageLabel { get; private set; } = "";
    public bool CanPrev { get; private set; }
    public bool CanNext { get; private set; }
    public ObservableCollection<AuditRow> Items { get; } = [];
    public ICommand SearchCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand PrevPageCommand { get; }

    //JJQR - AI (21 Nov 2026) - web /admin/audit-logs load: a newer filter/page supersedes an in-flight one (web cancels the stale effect;
    // RunAsync's busy guard used to drop the newer request). A failure shows ErrorState "Audit log unavailable" even if rows were showing.
    int _loadSeq;
    public bool IsLoading { get; private set; }
    public bool LoadFailed { get; private set; }

    public async Task LoadAsync()
    {
        var seq = ++_loadSeq;
        IsLoading = true; Raise(nameof(IsLoading));
        AuditEntityType? type = null;
        if (SelectedEntity != "All record types" && Enum.TryParse<AuditEntityType>(SelectedEntity, out var parsed))
            type = parsed;
        AdminPage<AuditLogListItem> page;
        try { page = await _audit.SearchAsync(string.IsNullOrWhiteSpace(Query) ? null : Query.Trim(), type, _page, 25); }
        catch
        {
            if (seq != _loadSeq) return;
            LoadFailed = true; IsLoading = false;
            Raise(nameof(LoadFailed)); Raise(nameof(IsLoading));
            return;
        }
        if (seq != _loadSeq) return;
        LoadFailed = false;
        Total = page.TotalCount;
        PageCount = Math.Max(1, (int)Math.Ceiling(page.TotalCount / 25d));
        Items.Clear();
        foreach (var i in page.Items)
            Items.Add(new AuditRow(ClinicDateTime(i.PerformedAt), i.Action, i.EntityType.ToString(), i.EntityId.ToString(), Dash(i.PerformedBy), Dash(i.Details)));
        CanPrev = _page > 1;
        CanNext = _page < PageCount;
        IsLoading = false;
        Raise(nameof(LoadFailed)); Raise(nameof(IsLoading)); Raise(nameof(CanPrev)); Raise(nameof(CanNext));
        (NextPageCommand as Command)?.ChangeCanExecute();
        (PrevPageCommand as Command)?.ChangeCanExecute();
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - web /admin/audit-logs: "Record type" + "Search" (250 ms debounce, page 1 on change), "a–b of N" / Previous / Page x of y / Next
    public int Total { get; private set; }
    public int PageCount { get; private set; } = 1;
    public int CurrentPage => _page;
    public string RangeLabel => Total == 0 ? "No matching entries" : $"{(_page - 1) * 25 + 1}–{Math.Min(_page * 25, Total)} of {Total}";
    public string EmptyMessage => !string.IsNullOrWhiteSpace(Query) || SelectedEntity != "All record types" ? "No entries match these filters." : "No audit entries yet.";

    public async Task ApplyFiltersAsync(string? entity, string? query)
    {
        SelectedEntity = string.IsNullOrEmpty(entity) ? "All record types" : entity;
        Query = query ?? "";
        _page = 1;
        await LoadAsync();
    }

    public async Task GoToPageAsync(int page)
    {
        _page = Math.Clamp(page, 1, PageCount);
        await LoadAsync();
    }
    //JJQR - END
}

public sealed record AuditRow(string When, string Action, string EntityType, string EntityId, string Who, string Details);
// JJQR - END
