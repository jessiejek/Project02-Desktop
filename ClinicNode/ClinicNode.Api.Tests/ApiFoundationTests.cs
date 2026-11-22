//JJQR - AI (Nov 21, 2026) - the foundation itself: error mapping, login/refresh, connectivity monitor, and the router's rules on a toy service (no database).
using System.Net;
using ClinicNode.Api.Http;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Core.Sync;

namespace ClinicNode.Api.Tests;

public class ApiFoundationTests
{
    private static (ApiClient Client, FakeHandler Http, InMemoryApiTokenStore Tokens, ApiConnectivity Conn, ApiAuthClient Auth) Make(Action<ApiOptions>? cfg = null)
    {
        var http = new FakeHandler();
        var o = new ApiOptions { BaseUrl = () => "http://api.test/", HttpHandlerFactory = () => http };
        cfg?.Invoke(o);
        var tokens = new InMemoryApiTokenStore();
        var conn = new ApiConnectivity(o);
        var auth = new ApiAuthClient(o, tokens, conn);
        return (new ApiClient(o, tokens, auth, conn), http, tokens, conn, auth);
    }

    // ── error mapping ─────────────────────────────────────────────────────
    [Theory]
    [InlineData(400, typeof(ApiValidationException))] [InlineData(422, typeof(ApiValidationException))]
    [InlineData(401, typeof(ApiUnauthorizedException))] [InlineData(403, typeof(ApiForbiddenException))]
    [InlineData(404, typeof(ApiNotFoundException))] [InlineData(409, typeof(ApiConflictException))]
    [InlineData(500, typeof(ApiServerException))]
    [InlineData(502, typeof(ApiOfflineException))] [InlineData(503, typeof(ApiOfflineException))] [InlineData(504, typeof(ApiOfflineException))]
    public async Task Status_codes_map_to_typed_exceptions(int status, Type expected)
    {
        var (client, http, _, _, _) = Make();
        http.Route = _ => FakeHandler.Text("{}", (HttpStatusCode)status);
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => client.GetAsync<object>("/api/x"));
        Assert.IsType(expected, ex);
    }

    [Fact]
    public async Task ProblemDetails_validation_errors_are_exposed_per_field()
    {
        var (client, http, _, _, _) = Make();
        http.Route = _ => FakeHandler.Text("""{"type":"x","title":"One or more validation errors occurred.","status":400,"errors":{"Email":["Email is required."],"Name":["Too long.","Bad."]}}""", HttpStatusCode.BadRequest);
        var ex = await Assert.ThrowsAsync<ApiValidationException>(() => client.GetAsync<object>("/api/x"));
        Assert.Equal("Email is required.", ex.Message);
        Assert.Equal(["Too long.", "Bad."], ex.Errors["Name"]);
    }

    [Theory]
    [InlineData("""{"message":"Slot taken."}""", "Slot taken.")]
    [InlineData("""{"error":"Nope."}""", "Nope.")]
    [InlineData("""{"title":"Conflict","detail":"Already booked."}""", "Already booked.")]
    public async Task Message_shapes_the_backend_uses_are_understood(string body, string message)
    {
        var (client, http, _, _, _) = Make();
        http.Route = _ => FakeHandler.Text(body, HttpStatusCode.Conflict);
        var ex = await Assert.ThrowsAsync<ApiConflictException>(() => client.GetAsync<object>("/api/x"));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Network_errors_and_timeouts_become_ApiOfflineException_and_mark_offline()
    {
        var (client, http, _, conn, _) = Make();
        http.Route = _ => throw new HttpRequestException("refused");
        await Assert.ThrowsAsync<ApiOfflineException>(() => client.GetAsync<object>("/api/x"));
        Assert.False(conn.IsOnline);

        var (client2, http2, _, _, _) = Make(o => o.RequestTimeout = TimeSpan.FromMilliseconds(50));
        http2.Route = _ => throw new TaskCanceledException();
        await Assert.ThrowsAsync<ApiOfflineException>(() => client2.GetAsync<object>("/api/x"));
    }

    [Fact]
    public async Task No_base_url_is_offline_and_empty_204_is_null()
    {
        var (client, http, _, _, _) = Make(o => o.BaseUrl = () => "  ");
        await Assert.ThrowsAsync<ApiOfflineException>(() => client.GetAsync<object>("/api/x"));
        var (c2, h2, _, _, _) = Make();
        h2.Route = _ => FakeHandler.Status(HttpStatusCode.NoContent);
        Assert.Null(await c2.GetAsync<Dictionary<string, string>>("/api/x"));
    }

    [Fact]
    public void Query_strings_are_escaped_and_nulls_skipped()
    {
        var u = ApiClient.Build("http://h:5055", "/api/a", new Dictionary<string, string?> { ["q"] = "a b&c", ["skip"] = null, ["n"] = "1" });
        Assert.Equal("http://h:5055/api/a?q=a%20b%26c&n=1", u.AbsoluteUri);
    }

    // ── login / refresh ───────────────────────────────────────────────────
    [Fact]
    public async Task Login_stores_both_tokens_and_marks_online_and_a_wrong_password_is_a_typed_401()
    {
        var (_, http, tokens, conn, auth) = Make();
        http.Route = r =>
        {
            var body = r.Content!.ReadAsStringAsync().Result;
            return body.Contains("good") ? FakeHandler.Json(new { accessToken = "a1", refreshToken = "r1", user = new { id = Guid.NewGuid(), fullName = "Dr. X", email = "x@c.test", role = "Doctor", isFirstLogin = false } }, camel: true)
                : FakeHandler.Text("""{"message":"Invalid email or password"}""", HttpStatusCode.Unauthorized);
        };
        var s = await auth.LoginAsync("x@c.test", "good");
        Assert.Equal("Doctor", s.User.Role);
        Assert.Equal("a1", tokens.AccessToken); Assert.Equal("r1", tokens.RefreshToken);
        Assert.True(conn.IsOnline);
        var ex = await Assert.ThrowsAsync<ApiUnauthorizedException>(() => auth.LoginAsync("x@c.test", "bad"));
        Assert.Equal("Invalid email or password", ex.Message);
        Assert.Contains("/api/auth/login", http.Paths);
    }

    [Fact]
    public async Task Concurrent_401s_trigger_a_single_refresh()
    {
        var (client, http, tokens, _, _) = Make();
        tokens.Set("old", "r1");
        http.Route = r => r.RequestUri!.AbsolutePath switch
        {
            "/api/auth/refresh-token" => FakeHandler.Json(new { accessToken = "new", refreshToken = "r2" }, camel: true),
            _ => r.Headers.Authorization?.Parameter == "new" ? FakeHandler.Json(new { ok = true }) : FakeHandler.Status(HttpStatusCode.Unauthorized),
        };
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetAsync<Dictionary<string, bool>>("/api/x")));
        Assert.Equal(1, http.Requests.Count(r => r.PathAndQuery == "/api/auth/refresh-token"));
    }

    // ── connectivity ──────────────────────────────────────────────────────
    [Fact]
    public async Task Connectivity_raises_Changed_only_on_transitions_and_pings_health_with_the_configured_base_url()
    {
        var (_, http, _, conn, _) = Make();
        var up = true;
        http.Route = r => r.RequestUri!.AbsolutePath == "/health/ready" && up ? FakeHandler.Status(HttpStatusCode.OK) : throw new HttpRequestException("down"); //JJQR - AI (Nov 22, 2026) - step 0.2: the ping is /health/ready //JJQR - END
        var events = new List<bool>();
        conn.Changed += events.Add;

        Assert.False(conn.IsOnline);                 // unknown until checked
        Assert.True(await conn.CheckNowAsync());
        Assert.True(await conn.CheckNowAsync());     // no change → no event
        up = false;
        Assert.False(await conn.CheckNowAsync());
        Assert.False(await conn.CheckNowAsync());
        up = true;
        Assert.True(await conn.CheckNowAsync());
        Assert.Equal([true, false, true], events);
        Assert.All(http.Paths, p => Assert.Equal("/health/ready", p));
    }

    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.2: "online" means the API answers AND the database is ready
    [Fact]
    public async Task Database_not_ready_counts_as_not_online_and_is_reported()
    {
        var (_, http, _, conn, _) = Make();
        http.Route = r => r.RequestUri!.AbsolutePath == "/health/ready" ? FakeHandler.Text("{\"db_ok\":false}", HttpStatusCode.ServiceUnavailable) : FakeHandler.Status(HttpStatusCode.OK);
        Assert.False(await conn.CheckNowAsync());
        Assert.False(conn.IsOnline);
        Assert.False(conn.DatabaseReady);   // API answered, database did not
    }

    [Fact]
    public async Task Database_ready_is_online_and_reported()
    {
        var (_, http, _, conn, _) = Make();
        http.Route = _ => FakeHandler.Status(HttpStatusCode.OK);
        Assert.True(await conn.CheckNowAsync());
        Assert.True(conn.DatabaseReady);
    }

    [Fact]
    public async Task An_older_server_without_health_ready_falls_back_to_health()
    {
        var (_, http, _, conn, _) = Make();
        http.Route = r => r.RequestUri!.AbsolutePath == "/health" ? FakeHandler.Status(HttpStatusCode.OK) : FakeHandler.Status(HttpStatusCode.NotFound);
        Assert.True(await conn.CheckNowAsync());
        Assert.Null(conn.DatabaseReady);    // unknown: that server cannot tell us
        Assert.Equal(["/health/ready", "/health"], http.Paths.ToArray());
    }

    [Fact]
    public async Task Server_unreachable_is_not_online_and_database_unknown()
    {
        var (_, http, _, conn, _) = Make();
        http.Route = _ => throw new HttpRequestException("down");
        Assert.False(await conn.CheckNowAsync());
        Assert.Null(conn.DatabaseReady);
    }
    //JJQR - END

    [Fact]
    public async Task Concurrent_checks_share_one_ping()
    {
        var (_, http, _, conn, _) = Make();
        http.Route = _ => FakeHandler.Status(HttpStatusCode.OK);
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => conn.EnsureCheckedAsync()));
        Assert.InRange(http.Requests.Count(r => r.PathAndQuery == "/health/ready"), 1, 2); //JJQR - AI (Nov 22, 2026) - step 0.2 //JJQR - END
    }

    [Fact]
    public async Task Background_loop_recovers_when_the_server_comes_back()
    {
        var (_, http, _, conn, _) = Make(o => o.OfflineBackoff = [TimeSpan.FromMilliseconds(30)]);
        var up = false;
        http.Route = _ => up ? FakeHandler.Status(HttpStatusCode.OK) : throw new HttpRequestException("down");
        var back = new TaskCompletionSource<bool>();
        conn.Changed += on => { if (on) back.TrySetResult(true); };
        Assert.False(await conn.CheckNowAsync());
        conn.Start();
        up = true;
        Assert.True(await Task.WhenAny(back.Task, Task.Delay(5000)) == back.Task, "the loop must re-check while offline and flip back online");
        conn.Dispose();
    }

    // ── router rules on a toy service ─────────────────────────────────────
    public interface IToy
    {
        Task<string> GetNameAsync();
        Task<string> SaveNameAsync(string name);
        Task TouchAsync();
        [OnlineRoute(OnlineRoute.ApiOnly)] Task<string> ResetPasswordAsync();
        [OnlineRoute(OnlineRoute.LocalOnly)] Task<string> PrintAsync();
        [OnlineWrite] Task<string> ApproveAsync();
        Task<string> NotYetAsync();
        string Sync { get; }
    }

    private sealed class Toy(string tag, bool throwOffline = false) : IToy
    {
        public readonly List<string> Calls = [];
        private Task<string> Do(string m) { Calls.Add(m); return throwOffline ? throw new ApiOfflineException("x") : Task.FromResult($"{tag}:{m}"); }
        public Task<string> GetNameAsync() => Do(nameof(GetNameAsync));
        public Task<string> SaveNameAsync(string name) => Do(nameof(SaveNameAsync) + name);
        public Task TouchAsync() { Calls.Add(nameof(TouchAsync)); return throwOffline ? throw new ApiOfflineException("x") : Task.CompletedTask; }
        public Task<string> ResetPasswordAsync() => Do(nameof(ResetPasswordAsync));
        public Task<string> PrintAsync() => Do(nameof(PrintAsync));
        public Task<string> ApproveAsync() => Do(nameof(ApproveAsync));
        public Task<string> NotYetAsync() => tag == "api" ? throw new ApiNotSupportedException() : Do(nameof(NotYetAsync));
        public string Sync => tag;
    }

    private sealed class FakeConn(bool online) : IApiConnectivity
    {
        public bool IsOnline { get; private set; } = online;
        public event Action<bool>? Changed;
        public int Failures;
        public Task<bool> EnsureCheckedAsync(CancellationToken ct = default) => Task.FromResult(IsOnline);
        public Task<bool> CheckNowAsync(CancellationToken ct = default) => Task.FromResult(IsOnline);
        public void ReportNetworkFailure() { Failures++; IsOnline = false; Changed?.Invoke(false); }
        public void ReportSuccess() { IsOnline = true; }
        public void Start() { }
    }

    private static (IToy Proxy, Toy Api, Toy Local, CountingSync Sync, FakeConn Conn) Router(bool online, bool apiThrowsOffline = false, bool session = true)
    {
        var api = new Toy("api", apiThrowsOffline); var local = new Toy("local"); var sync = new CountingSync(); var conn = new FakeConn(online);
        return (OnlineFirstProxy<IToy>.Create(api, local, conn, sync, () => session), api, local, sync, conn);
    }

    [Fact]
    public async Task Router_online_uses_api_reads_dont_sync_writes_do()
    {
        var (p, api, local, sync, _) = Router(online: true);
        Assert.Equal("api:GetNameAsync", await p.GetNameAsync());
        Assert.Equal(0, sync.Calls);
        Assert.Equal("api:SaveNameAsyncBob", await p.SaveNameAsync("Bob"));
        await p.TouchAsync();
        Assert.Equal("api:ApproveAsync", await p.ApproveAsync()); // [OnlineWrite] on a name that looks like a read? "Approve" is a write by default; attribute makes it explicit
        Assert.Equal(3, sync.Calls);
        Assert.Empty(local.Calls);
    }

    [Fact]
    public async Task Router_offline_uses_local_and_never_touches_api_or_sync()
    {
        var (p, api, local, sync, _) = Router(online: false);
        Assert.Equal("local:GetNameAsync", await p.GetNameAsync());
        Assert.Equal("local:SaveNameAsyncBob", await p.SaveNameAsync("Bob"));
        await p.TouchAsync();
        Assert.Empty(api.Calls);
        Assert.Equal(0, sync.Calls);
        Assert.Equal("local", p.Sync); // synchronous members are always local
    }

    [Fact]
    public async Task Router_network_failure_falls_back_to_local_and_reports_it_for_reads_writes_and_void()
    {
        var (p, api, local, sync, conn) = Router(online: true, apiThrowsOffline: true);
        Assert.Equal("local:GetNameAsync", await p.GetNameAsync());
        Assert.Equal(1, conn.Failures);
        var (p2, _, local2, _, _) = Router(online: true, apiThrowsOffline: true);
        Assert.Equal("local:SaveNameAsyncAnn", await p2.SaveNameAsync("Ann"));
        await p2.TouchAsync();
        Assert.Equal([nameof(IToy.SaveNameAsync) + "Ann", nameof(IToy.TouchAsync)], local2.Calls);
        Assert.Equal(0, sync.Calls); // a failed api write is not "a successful write → pull"
    }

    [Fact]
    public async Task Router_ApiOnly_never_falls_back_and_LocalOnly_never_calls_the_api()
    {
        var (p, api, local, _, _) = Router(online: false);
        await Assert.ThrowsAsync<ApiOfflineException>(() => p.ResetPasswordAsync());
        Assert.Equal("local:PrintAsync", await p.PrintAsync());
        var (p2, api2, _, _, _) = Router(online: true);
        Assert.Equal("local:PrintAsync", await p2.PrintAsync()); // even online
        Assert.DoesNotContain("PrintAsync", api2.Calls);
        var (p3, _, local3, _, _) = Router(online: true, apiThrowsOffline: true);
        await Assert.ThrowsAsync<ApiOfflineException>(() => p3.ResetPasswordAsync());
        Assert.Empty(local3.Calls);
    }

    [Fact]
    public async Task Router_ApiNotSupported_uses_local_without_marking_offline()
    {
        var (p, _, local, _, conn) = Router(online: true);
        Assert.Equal("local:NotYetAsync", await p.NotYetAsync());
        Assert.Equal(0, conn.Failures);
        Assert.True(conn.IsOnline);
    }

    [Fact]
    public async Task Router_without_an_api_session_stays_local()
    {
        var (p, api, _, _, _) = Router(online: true, session: false);
        Assert.Equal("local:GetNameAsync", await p.GetNameAsync());
        Assert.Empty(api.Calls);
    }
}
//JJQR - END
