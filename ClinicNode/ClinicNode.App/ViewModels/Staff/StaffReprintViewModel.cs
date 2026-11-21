//JJQR - AI (Nov 21, 2026) - staff reprint flags (ISyncStatus): a queue slip (cloud renumbered the booking) or a patient card (code replaced) must be printed again.
// Pure logic, no MAUI types: the panel supplies the print callback. Badge count = open flags, refreshed after every reprint / dismiss and when the sync layer reports a change.
using ClinicNode.Sync;
using ClinicNode.Sync.Local;

namespace ClinicNode.App.ViewModels.Staff;

public sealed record ReprintItem(Guid FlagId, string Kind, string EntityId, string Title, string Detail, bool CanReprint, bool CanOpenPatient)
{
    public Guid? BookingId => Kind == StaffReprintViewModel.QueueSlip && Guid.TryParse(EntityId, out var g) ? g : null;
    public Guid? PatientId => Kind == StaffReprintViewModel.PatientCard && Guid.TryParse(EntityId, out var g) ? g : null;
}

public sealed class StaffReprintViewModel : IDisposable
{
    public const string QueueSlip = "queue_slip";
    public const string PatientCard = "patient_card";

    private readonly ISyncStatus _sync;
    private readonly Func<Guid, Task>? _printQueueSlip;

    /// <param name="printQueueSlip">Builds and prints the queue slip for a booking (IPrintDocumentFactory.QueueSlipAsync + IPrinter). Throw on failure so the flag stays open.
    /// There is no patient-card print layout yet, so patient_card flags are dismiss / open-patient only.</param>
    public StaffReprintViewModel(ISyncStatus sync, Func<Guid, Task>? printQueueSlip = null)
    {
        _sync = sync;
        _printQueueSlip = printQueueSlip;
        _sync.PropertyChanged += OnSyncChanged;
    }

    public IReadOnlyList<ReprintItem> Items { get; private set; } = [];
    /// <summary>Open queue_slip + patient_card flags (what the badge shows).</summary>
    public int Count => Items.Count;
    public string? Error { get; private set; }
    public string BadgeText => Count == 0 ? "" : Count == 1 ? "1 to reprint" : $"{Count} to reprint";

    /// <summary>Raised after Items changes; may arrive on the sync thread (marshal in the view).</summary>
    public event Action? Changed;

    public static bool IsStaffKind(string kind) => kind is QueueSlip or PatientCard;

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var flags = await _sync.GetReprintFlagsAsync(ct);
        Items = flags.Where(f => IsStaffKind(f.Kind)).OrderByDescending(f => f.CreatedAt).Select(ToItem).ToList();
        Changed?.Invoke();
    }

    /// <summary>Reprint (queue slip only), then dismiss the flag and refresh the badge. A print failure keeps the flag open and sets Error.</summary>
    public async Task<bool> ReprintAsync(ReprintItem item)
    {
        Error = null;
        if (item.BookingId is not { } bookingId || _printQueueSlip is null)
        {
            Error = "This item can’t be reprinted from here.";
            Changed?.Invoke();
            return false;
        }
        try
        {
            await _printQueueSlip(bookingId);
        }
        catch (Exception ex)
        {
            Error = "Print failed: " + ex.Message;
            Changed?.Invoke();
            return false;
        }
        await DismissAsync(item.FlagId);
        return true;
    }

    public async Task DismissAsync(Guid flagId)
    {
        Error = null;
        try { await _sync.DismissReprintFlagAsync(flagId); }
        catch (Exception ex) { Error = "Could not dismiss: " + ex.Message; }
        await RefreshAsync();
    }

    static ReprintItem ToItem(ReprintFlag f)
    {
        var isSlip = f.Kind == QueueSlip;
        var title = isSlip ? "Reprint queue slip" : "Reprint patient card";
        var change = !string.IsNullOrWhiteSpace(f.OldValue) || !string.IsNullOrWhiteSpace(f.NewValue)
            ? $"{(string.IsNullOrWhiteSpace(f.OldValue) ? "—" : f.OldValue)} → {(string.IsNullOrWhiteSpace(f.NewValue) ? "—" : f.NewValue)}"
            : null;
        var detail = string.Join(" · ", new[] { f.Message, change }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new ReprintItem(f.Id, f.Kind, f.EntityId, title, detail, CanReprint: isSlip, CanOpenPatient: !isSlip);
    }

    private void OnSyncChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ISyncStatus.ReprintCount))
            _ = RefreshAsync().ContinueWith(_ => { }, TaskScheduler.Default); // swallow: a failed background refresh keeps the last list
    }

    public void Dispose() => _sync.PropertyChanged -= OnSyncChanged;
}
//JJQR - END
