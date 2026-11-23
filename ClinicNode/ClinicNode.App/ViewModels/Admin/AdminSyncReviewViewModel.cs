//JJQR - AI (Nov 23, 2026) - web parity: admin "Sync review" (web /admin/sync-review). Lists what the clinic computers sent that needs a person, resolves or dismisses an item, and merges two patients. Online only.
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.ViewModels.Admin;

public sealed class AdminSyncReviewViewModel(IAdminSyncReviewService svc) : AdminViewModelBase
{
    public const int PageSize = 25;
    public static readonly string[] StatusOptions = ["Open", "Resolved", "All"];

    public List<SyncReviewItem> Items { get; private set; } = [];
    public int Total { get; private set; }
    public int OpenTotal { get; private set; }
    public int Page { get; set; } = 1;
    public string Status { get; set; } = "open";
    public string Kind { get; set; } = "";
    public bool FromServer { get; private set; }
    public bool Loaded { get; private set; }
    public bool LoadFailed { get; private set; }
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    public async Task LoadAsync()
    {
        LoadFailed = false;
        try
        {
            var list = await svc.ListAsync(Status, Kind, Page, PageSize);
            FromServer = list.FromServer;
            Items = list.Items.ToList();
            Total = list.Total;
            OpenTotal = list.OpenTotal;
        }
        catch { LoadFailed = true; }
        Loaded = true;
    }

    static string Describe(Exception ex, string fallback) => ex switch
    {
        ApiOfflineException => "The clinic server could not be reached. Nothing was changed.",
        ApiException a when !string.IsNullOrWhiteSpace(a.Message) => a.Message,
        InvalidOperationException i when !string.IsNullOrWhiteSpace(i.Message) => i.Message,
        _ => fallback,
    };

    /// <summary>Dismiss ("dismissed") or mark resolved ("fixed"). Returns null on success, else the message to show.</summary>
    public async Task<string?> ResolveAsync(SyncReviewItem item, string resolution, string note)
    {
        if (SyncReviewRules.NoteProblem(note) is { } bad) return bad;
        try { await svc.ResolveAsync(item.Id, resolution, note); return null; }
        catch (Exception ex) { return Describe(ex, "Could not save. Try again."); }
    }

    /// <summary>The two records for the merge screen. <paramref name="keepId"/> is the record already on file.</summary>
    public async Task<(MergePreview? Preview, string? Error)> PreviewAsync(Guid keepId, Guid mergeId)
    {
        try { return (await svc.PreviewMergeAsync(keepId, mergeId), null); }
        catch (Exception ex) { return (null, Describe(ex, "These patients could not be loaded.")); }
    }

    public async Task<string?> MergeAsync(Guid keepId, Guid mergeId, string note)
    {
        if (SyncReviewRules.NoteProblem(note) is { } bad) return bad;
        try { await svc.MergeAsync(keepId, mergeId, note); return null; }
        catch (Exception ex) { return Describe(ex, "The merge could not be completed. Nothing was changed."); }
    }
}
//JJQR - END
