//JJQR - AI (Nov 21, 2026) - test kit for online-first lanes: a fake HttpMessageHandler + a DI rig with the REAL Local services on a temp SQLite db.
// Copy this file's pattern for your lane's tests (see docs/ONLINE-FIRST.md §7).
using System.Net;
using System.Text;
using System.Text.Json;
using ClinicApp.Infrastructure;
using ClinicNode.Api.Http;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using ClinicNode.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.Api.Tests;

/// <summary>Scriptable server. Return a response, or throw (HttpRequestException = connection refused, TaskCanceledException = timeout) to simulate the network.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Route { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);
    public List<(HttpMethod Method, string PathAndQuery, string? Auth, string? Body)> Requests { get; } = [];
    public IEnumerable<string> Paths => Requests.Select(r => r.PathAndQuery);
    public bool Saw(string pathStart) => Requests.Any(r => r.PathAndQuery.StartsWith(pathStart, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        lock (Requests) Requests.Add((request.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.Parameter, body));
        return Route(request);
    }

    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK, bool camel = false) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(body, camel ? ApiJson.Auth : ApiJson.Data), Encoding.UTF8, "application/json") };
    public static HttpResponseMessage Text(string raw, HttpStatusCode status) => new(status) { Content = new StringContent(raw, Encoding.UTF8, "application/json") };
    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);
}

public sealed class CountingSync : ISyncBridge
{
    public int Calls;
    public void RequestSync() => Interlocked.Increment(ref Calls);
    public Task<bool> IsRevokedAsync(CancellationToken ct = default) => Task.FromResult(false);
}

public sealed class ApiRig : IAsyncDisposable
{
    public FakeHandler Http { get; } = new();
    public CountingSync Sync { get; } = new();
    public ServiceProvider Services { get; private set; } = null!;
    public Guid DoctorId { get; private set; }
    private string _dir = "";

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    public IApiConnectivity Connectivity => Get<IApiConnectivity>();
    public IApiTokenStore Tokens => Get<IApiTokenStore>();

    /// <param name="register">your lane's one-liner(s), e.g. <c>s =&gt; s.AddOnlineFirstDoctorPerformance()</c></param>
    public static async Task<ApiRig> StartAsync(Action<IServiceCollection> register, bool signedIn = true, bool healthy = true)
    {
        var rig = new ApiRig();
        rig._dir = Path.Combine(Path.GetTempPath(), "clinic-node-api-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rig._dir);
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddClinicNodeCore(new NodeOptions { DataDirectory = rig._dir, Demo = true });
        s.AddClinicNodeSync();
        s.RemoveAll<ISyncBridge>();
        s.AddSingleton<ISyncBridge>(rig.Sync);
        s.AddClinicNodeApi(_ => "http://api.test", o => { o.HttpHandlerFactory = () => rig.Http; o.AutoStart = false; });
        register(s);
        rig.Services = s.BuildServiceProvider();
        await rig.Get<LocalDatabase>().InitializeAsync();
        var login = await rig.Get<IAuthService>().LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, rememberEmail: false);
        Assert.True(login.Succeeded, login.Error);
        await using var db = await rig.Get<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>().CreateDbContextAsync();
        rig.DoctorId = (await db.Doctors.Include(d => d.StaffAccount).AsNoTracking().FirstAsync(d => d.StaffAccount!.Email == "doctor@clinic.test")).DoctorId;
        if (signedIn) rig.Tokens.Set("access-1", "refresh-1");
        // default server: healthy /health, everything else 404 until the test scripts it
        rig.Http.Route = r => r.RequestUri!.AbsolutePath == "/health" && healthy ? FakeHandler.Status(HttpStatusCode.OK)
            : r.RequestUri!.AbsolutePath == "/health" ? throw new HttpRequestException("connection refused") : FakeHandler.Status(HttpStatusCode.NotFound);
        return rig;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
//JJQR - END
