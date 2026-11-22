//JJQR - AI (Nov 22, 2026) - regression: sign-in failed with "Cannot access a disposed object: SocketsHttpHandler" because the shared connection pool was closed when one HttpClient was disposed.
using ClinicNode.Core.Diagnostics;

namespace ClinicNode.Api.Tests;

public class SharedHandlerLeaseTests
{
    sealed class Probe : HttpMessageHandler
    {
        public bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Disposed ? throw new ObjectDisposedException(nameof(Probe)) : Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    [Fact]
    public async Task Disposing_one_client_does_not_break_the_others_sharing_the_pool()
    {
        var shared = new Probe();
        var a = new HttpClient(new DevLogHandler("api", new SharedHandlerLease(shared)), disposeHandler: true);
        var b = new HttpClient(new DevLogHandler("api", new SharedHandlerLease(shared)), disposeHandler: true);
        a.Dispose();                                   // e.g. the sign-in client finishing
        var res = await b.GetAsync("http://localhost/x"); // the next call must still work
        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        Assert.False(shared.Disposed);
    }
}
//JJQR - END
