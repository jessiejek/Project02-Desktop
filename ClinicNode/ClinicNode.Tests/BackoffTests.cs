// JJQR - AI (Nov 21, 2026)
using ClinicNode.Sync;

namespace ClinicNode.Tests;

public class BackoffTests
{
    [Fact]
    public void Delay_grows_and_is_capped_at_five_minutes()
    {
        var rng = new Random(1);
        var first = Backoff.DelayAfter(1, rng);
        var second = Backoff.DelayAfter(2, rng);
        var late = Backoff.DelayAfter(20, rng);
        Assert.InRange(first.TotalSeconds, 0.8, 1.2);
        Assert.True(second > first);
        Assert.True(late <= Backoff.Cap);
        Assert.Equal(TimeSpan.FromMinutes(5), Backoff.Cap);
    }
}
// JJQR - END
