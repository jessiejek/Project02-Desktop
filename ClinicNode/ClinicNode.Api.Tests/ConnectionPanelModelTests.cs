//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.5: the panel's numbers equal what the monitor holds
using ClinicNode.Api.Http;

namespace ClinicNode.Api.Tests;

public class ConnectionPanelModelTests
{
    private static HealthSample S(int min, int ms, bool ok = true, bool? db = true) =>
        new(new DateTimeOffset(2026, 11, 22, 8, min, 0, TimeSpan.Zero), ms, ok ? 200 : 503, db, ok ? null : (db == false ? "database_not_ready" : "unreachable"));

    [Fact]
    public void Nothing_checked_yet()
    {
        var n = ConnectionPanelModel.Compute([]);
        Assert.Null(n.LastMs); Assert.Null(n.LastContactAt); Assert.Equal(0, n.Checks);
    }

    [Fact]
    public void Last_median_worst_and_last_contact_match_the_samples()
    {
        var n = ConnectionPanelModel.Compute([S(0, 40), S(1, 900), S(2, 60), S(3, 80), S(4, 2000, ok: false, db: null)]);
        Assert.Equal(2000, n.LastMs);
        Assert.Equal(80, n.MedianMs);                        // 40 60 80 900 2000 -> middle value
        Assert.Equal(2000, n.WorstMs);
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 8, 3, 0, TimeSpan.Zero), n.LastContactAt);   // the newest check the server answered
        Assert.Equal(5, n.Checks); Assert.Equal(1, n.Failed);
        Assert.Equal("Unknown (server not answering)", n.Database);
    }

    [Theory]
    [InlineData(true, "Ready")]
    [InlineData(false, "Not ready")]
    public void Database_health_comes_from_the_newest_check(bool dbOk, string expected)
        => Assert.Equal(expected, ConnectionPanelModel.Compute([S(0, 50, ok: dbOk, db: dbOk)]).Database);

    [Fact]
    public void An_older_server_without_the_database_check_is_called_unknown()
        => Assert.Equal("Unknown (older server)", ConnectionPanelModel.Compute([new HealthSample(DateTimeOffset.UtcNow, 30, 200, null, null)]).Database);
}
//JJQR - END
