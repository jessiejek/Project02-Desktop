//JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.5: every badge state
using ClinicNode.Sync;

namespace ClinicNode.Tests;

public class SyncBadgeTests
{
    //            reachable  phase                 syncing pending problem  → badge
    [Theory]
    [InlineData(true,  SyncPhase.Idle,        false, 0, false, SyncBadgeKind.Online)]
    [InlineData(true,  SyncPhase.Idle,        true,  0, false, SyncBadgeKind.Syncing)]
    [InlineData(true,  SyncPhase.Idle,        false, 3, false, SyncBadgeKind.Syncing)]
    [InlineData(false, SyncPhase.Idle,        false, 0, false, SyncBadgeKind.Offline)]   // server down or database not ready
    [InlineData(false, SyncPhase.Idle,        false, 5, false, SyncBadgeKind.Offline)]
    [InlineData(true,  SyncPhase.Offline,     false, 2, false, SyncBadgeKind.Offline)]
    [InlineData(true,  SyncPhase.Error,       false, 0, true,  SyncBadgeKind.Problem)]
    [InlineData(true,  SyncPhase.Revoked,     false, 0, true,  SyncBadgeKind.Problem)]
    [InlineData(true,  SyncPhase.NeedsUpdate, false, 0, true,  SyncBadgeKind.Problem)]
    [InlineData(true,  SyncPhase.Idle,        false, 1, true,  SyncBadgeKind.Problem)]   // a stuck change
    [InlineData(false, SyncPhase.Error,       false, 1, true,  SyncBadgeKind.Offline)]   // unreachable beats everything: the changes are safe and waiting
    public void Resolve_gives_the_three_words(bool reachable, SyncPhase phase, bool syncing, int pending, bool problem, SyncBadgeKind expected)
        => Assert.Equal(expected, SyncBadge.Resolve(reachable, phase, syncing, pending, problem));

    [Fact]
    public void Text_is_exactly_the_owners_wording()
    {
        Assert.Equal("Offline, 1 change waiting", SyncBadge.Text(SyncBadgeKind.Offline, 1));
        Assert.Equal("Offline, 7 changes waiting", SyncBadge.Text(SyncBadgeKind.Offline, 7));
        Assert.Equal("Sync problem", SyncBadge.Text(SyncBadgeKind.Problem, 3));
        Assert.Equal("Online, up to date", SyncBadge.Text(SyncBadgeKind.Online, 0));
    }
}
//JJQR - END
