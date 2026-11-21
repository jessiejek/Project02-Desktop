//JJQR - AI (21 Nov 2026) - Api implementation of IAdminOrClashService: GET /api/payments/or-clashes (Admin only, read-only).
// Response (snake_case): [{ or_number, count, payments: [{ payment_id, booking_id, status, amount, confirmed_at, origin_node_id, origin }] }]
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiAdminOrClashService(IApiClient api) : IAdminOrClashService
{
    private sealed class ClashRow
    {
        public string? OrNumber { get; set; }
        public int Count { get; set; }
        public List<PaymentRow>? Payments { get; set; }
    }

    private sealed class PaymentRow
    {
        public Guid PaymentId { get; set; }
        public Guid BookingId { get; set; }
        public string? Status { get; set; }
        public decimal Amount { get; set; }
        public DateTimeOffset? ConfirmedAt { get; set; }
        public Guid? OriginNodeId { get; set; }
        public string? Origin { get; set; }
    }

    public async Task<OrClashList> ListAsync(CancellationToken ct = default)
    {
        var rows = await api.GetAsync<List<ClashRow>>("/api/payments/or-clashes", null, ct) ?? [];
        var items = rows.Select(r => new OrClash(
                r.OrNumber ?? "",
                r.Count,
                (r.Payments ?? []).Select(p => new OrClashPayment(
                    p.PaymentId, p.BookingId, p.Status ?? "", p.Amount, p.ConfirmedAt, p.OriginNodeId,
                    string.IsNullOrWhiteSpace(p.Origin) ? (p.OriginNodeId is null ? "cloud" : "node") : p.Origin!)).ToList()))
            .ToList();
        return new OrClashList(items, true);
    }
}
//JJQR - END
