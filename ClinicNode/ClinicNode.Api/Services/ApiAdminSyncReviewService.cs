//JJQR - AI (Nov 23, 2026) - Api implementation of IAdminSyncReviewService (twin of the web's lib/data/syncReview.ts):
// GET /api/sync/review-items, POST /api/sync/review-items/{id}/resolve, GET /api/patients/merge-preview, POST /api/patients/merge. Admin only (the server refuses everyone else).
using System.Text.Json;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminSyncReviewService(IApiClient api) : IAdminSyncReviewService
{
    private sealed class ItemDto
    {
        public Guid Id { get; set; }
        public string? Kind { get; set; }
        public string? Status { get; set; }
        public string? Title { get; set; }
        public Dictionary<string, JsonElement>? Details { get; set; }
        public Guid? PatientId { get; set; }
        public Guid? OtherPatientId { get; set; }
        public string? Aggregate { get; set; }
        public string? EntityId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? ResolvedAt { get; set; }
        public string? Resolution { get; set; }
        public string? ResolutionNote { get; set; }
    }

    private sealed class ListDto
    {
        public List<ItemDto>? Items { get; set; }
        public int Total { get; set; }
        public int OpenTotal { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    private sealed class CardDto
    {
        public Guid PatientId { get; set; }
        public string? PatientCode { get; set; }
        public string? FirstName { get; set; }
        public string? MiddleName { get; set; }
        public string? LastName { get; set; }
        public string? DateOfBirth { get; set; }
        public string? Sex { get; set; }
        public string? ContactNumber { get; set; }
        public string? Email { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public bool HasAccount { get; set; }
        public Dictionary<string, int>? Counts { get; set; }
    }

    private sealed class PreviewDto
    {
        public CardDto? Keep { get; set; }
        public CardDto? Merge { get; set; }
        public bool BothHaveAccounts { get; set; }
    }

    public async Task<SyncReviewList> ListAsync(string? status, string? kind, int page, int pageSize, CancellationToken ct = default)
    {
        var q = new Dictionary<string, string?>
        {
            ["status"] = string.IsNullOrWhiteSpace(status) || status == "all" ? null : status,
            ["kind"] = string.IsNullOrWhiteSpace(kind) ? null : kind,
            ["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["page_size"] = pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        var dto = await api.GetAsync<ListDto>("/api/sync/review-items", q, ct) ?? new ListDto();
        var items = (dto.Items ?? []).Select(i => new SyncReviewItem(
            i.Id, i.Kind ?? "", i.Status ?? "open", i.Title ?? "",
            (i.Details ?? []).Where(kv => kv.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) && kv.Value.ToString().Length > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString()),
            i.PatientId, i.OtherPatientId, i.Aggregate, i.EntityId, i.CreatedAt, i.ResolvedAt, i.Resolution, i.ResolutionNote)).ToList();
        return new SyncReviewList(items, dto.Total, dto.OpenTotal, dto.Page, dto.PageSize, true);
    }

    public Task ResolveAsync(Guid id, string resolution, string note, CancellationToken ct = default) =>
        api.SendAsync(HttpMethod.Post, $"/api/sync/review-items/{id:D}/resolve", new { Resolution = resolution, Note = note.Trim() }, null, ct);

    static MergeCard Card(CardDto c) => new(
        c.PatientId, c.PatientCode ?? "", c.FirstName ?? "", c.MiddleName, c.LastName ?? "", c.DateOfBirth, c.Sex,
        c.ContactNumber, c.Email, c.Address, c.City, c.HasAccount, c.Counts ?? new Dictionary<string, int>());

    public async Task<MergePreview> PreviewMergeAsync(Guid keepPatientId, Guid mergePatientId, CancellationToken ct = default)
    {
        var q = new Dictionary<string, string?> { ["keep"] = keepPatientId.ToString("D"), ["merge"] = mergePatientId.ToString("D") };
        var dto = await api.GetAsync<PreviewDto>("/api/patients/merge-preview", q, ct);
        if (dto?.Keep is null || dto.Merge is null) throw new ApiNotFoundException("These patients could not be loaded.", null);
        return new MergePreview(Card(dto.Keep), Card(dto.Merge), dto.BothHaveAccounts);
    }

    public Task MergeAsync(Guid keepPatientId, Guid mergePatientId, string note, CancellationToken ct = default) =>
        api.SendAsync(HttpMethod.Post, "/api/patients/merge", new { KeepPatientId = keepPatientId, MergePatientId = mergePatientId, Note = note.Trim() }, null, ct);
}
//JJQR - END
