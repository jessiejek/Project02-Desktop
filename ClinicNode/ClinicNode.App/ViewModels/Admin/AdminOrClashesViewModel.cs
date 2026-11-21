//JJQR - AI (21 Nov 2026) - Admin "Receipt number clashes": OR numbers used by more than one payment (GET /api/payments/or-clashes). Read-only, online-only.
using System.Globalization;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.ViewModels.Admin;

public sealed record OrClashPaymentRow(Guid BookingId, string Status, string Amount, string Confirmed, string Origin);

public sealed record OrClashRow(string OrNumber, string CountLabel, IReadOnlyList<OrClashPaymentRow> Payments);

public sealed class AdminOrClashesViewModel(IAdminOrClashService svc) : AdminViewModelBase
{
    public List<OrClashRow> Items { get; } = [];
    public bool FromServer { get; private set; }
    public bool Loaded { get; private set; }
    public bool LoadFailed { get; private set; }

    public async Task LoadAsync()
    {
        LoadFailed = false;
        try
        {
            var list = await svc.ListAsync();
            FromServer = list.FromServer;
            Items.Clear();
            foreach (var c in list.Items)
            {
                Items.Add(new OrClashRow(
                    c.OrNumber,
                    $"{c.Count} payments",
                    c.Payments.Select(p => new OrClashPaymentRow(
                        p.BookingId,
                        p.Status,
                        Peso(p.Amount),
                        p.ConfirmedAt is { } at ? ClinicDateTime(at) : "Not confirmed",
                        p.Origin == "node"
                            ? "Clinic computer" + (p.OriginNodeId is { } n ? " " + n.ToString("D", CultureInfo.InvariantCulture)[..8] : "")
                            : "Cloud (web)")).ToList()));
            }
        }
        catch { LoadFailed = true; }
        Loaded = true;
    }

    public Task OpenBookingAsync(Guid bookingId) => GoAsync($"admin-booking-detail?id={bookingId}");
}
//JJQR - END
