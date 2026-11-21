//JJQR - AI (Nov 21, 2026) - staff reprint badge/list logic against a fake ISyncStatus.
using System.ComponentModel;
using ClinicNode.App.ViewModels.Staff;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;

namespace ClinicNode.Tests.Staff;

public class StaffReprintViewModelTests
{
    sealed class FakeSync : ISyncStatus
    {
        public List<ReprintFlag> Flags = [];
        public int ReprintCount => Flags.Count;
        public SyncPhase Phase => SyncPhase.Idle;
        public int PendingCount => 0;
        public int ConflictCount => 0;
        public DateTimeOffset? LastSyncedAt => null;
        public string? LastError => null;
        public bool IsOnline => true;
        public bool IsSyncing => false;
        public string Banner => "";
        public SyncStatus Current => default!;
        public event Action<SyncStatus>? Changed { add { } remove { } }
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<ReprintFlag>? ReprintRequired { add { } remove { } }
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void RequestSync() { }
        public Task<IReadOnlyList<SyncConflict>> GetOpenConflictsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SyncConflict>>([]);
        public Task ResolveConflictAsync(Guid conflictId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ReprintFlag>> GetReprintFlagsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReprintFlag>>(Flags.ToList());
        public Task DismissReprintFlagAsync(Guid flagId, CancellationToken ct = default)
        {
            Flags.RemoveAll(f => f.Id == flagId);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReprintCount)));
            return Task.CompletedTask;
        }
    }

    static ReprintFlag Slip(Guid booking, string oldQ = "S-004", string newQ = "S-007") =>
        new() { Kind = "queue_slip", EntityId = booking.ToString(), OldValue = oldQ, NewValue = newQ, Message = "Queue number changed" };
    static ReprintFlag Card(Guid patient) =>
        new() { Kind = "patient_card", EntityId = patient.ToString(), OldValue = "MF-0001", NewValue = "MF-0042", Message = "Patient code changed" };

    [Fact]
    public async Task Badge_counts_only_queue_slip_and_patient_card_flags()
    {
        var sync = new FakeSync();
        sync.Flags.Add(Slip(Guid.NewGuid()));
        sync.Flags.Add(Card(Guid.NewGuid()));
        sync.Flags.Add(new ReprintFlag { Kind = "something_else", EntityId = "x" });
        using var vm = new StaffReprintViewModel(sync);
        Assert.Equal(0, vm.Count);
        await vm.RefreshAsync();
        Assert.Equal(2, vm.Count);
        Assert.Equal("2 to reprint", vm.BadgeText);
        Assert.Contains(vm.Items, i => i.CanReprint && i.Title.Contains("queue slip") && i.Detail.Contains("S-004 → S-007"));
        Assert.Contains(vm.Items, i => !i.CanReprint && i.CanOpenPatient && i.PatientId is not null);
    }

    [Fact]
    public async Task Reprint_prints_the_booking_then_dismisses_and_refreshes_the_badge()
    {
        var sync = new FakeSync();
        var booking = Guid.NewGuid();
        sync.Flags.Add(Slip(booking));
        Guid? printed = null;
        using var vm = new StaffReprintViewModel(sync, id => { printed = id; return Task.CompletedTask; });
        await vm.RefreshAsync();
        Assert.True(await vm.ReprintAsync(vm.Items[0]));
        Assert.Equal(booking, printed);
        Assert.Empty(sync.Flags);
        Assert.Equal(0, vm.Count);
        Assert.Equal("", vm.BadgeText);
    }

    [Fact]
    public async Task Failed_print_keeps_the_flag_open_and_reports_the_error()
    {
        var sync = new FakeSync();
        sync.Flags.Add(Slip(Guid.NewGuid()));
        using var vm = new StaffReprintViewModel(sync, _ => throw new InvalidOperationException("no paper"));
        await vm.RefreshAsync();
        Assert.False(await vm.ReprintAsync(vm.Items[0]));
        Assert.Single(sync.Flags);
        Assert.Equal(1, vm.Count);
        Assert.Contains("no paper", vm.Error);
    }

    [Fact]
    public async Task Patient_card_flag_cannot_reprint_but_can_be_dismissed()
    {
        var sync = new FakeSync();
        sync.Flags.Add(Card(Guid.NewGuid()));
        var calls = 0;
        using var vm = new StaffReprintViewModel(sync, _ => { calls++; return Task.CompletedTask; });
        await vm.RefreshAsync();
        Assert.False(await vm.ReprintAsync(vm.Items[0]));
        Assert.Equal(0, calls);
        Assert.Equal(1, vm.Count);
        await vm.DismissAsync(vm.Items[0].FlagId);
        Assert.Equal(0, vm.Count);
        Assert.Null(vm.Error);
    }

    [Fact]
    public async Task Sync_count_change_refreshes_the_list()
    {
        var sync = new FakeSync();
        using var vm = new StaffReprintViewModel(sync);
        var changed = new TaskCompletionSource();
        vm.Changed += () => changed.TrySetResult();
        sync.Flags.Add(Slip(Guid.NewGuid()));
        await sync.DismissReprintFlagAsync(Guid.NewGuid()); // fires PropertyChanged(ReprintCount)
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, vm.Count);
    }
}
//JJQR - END
