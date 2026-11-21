//JJQR - AI (Nov 21, 2026)
// In-memory fake of the cloud sync API (docs/SYNC-API-CONTRACT.md v1) as an HttpMessageHandler.
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClinicNode.Tests.Sync;

public sealed class FakeSyncServer : HttpMessageHandler
{
    public bool Offline { get; set; }
    public bool Revoked { get; set; }
    /// <summary>Number of upcoming sync calls (not token) answered 401.</summary>
    public int UnauthorizedResponses { get; set; }
    public int TokensIssued { get; private set; }
    public string CurrentToken { get; private set; } = "";

    public ConcurrentQueue<JsonObject> PushedOperations { get; } = new();
    public List<HttpRequestMessage> Requests { get; } = new();
    public List<string?> PullCursors { get; } = new();

    /// <summary>Per-op decision; default "applied" with an increasing row_version.</summary>
    public Func<JsonObject, JsonObject>? OnPush { get; set; }
    /// <summary>Whole-batch error (e.g. 400 invalid_operation); null result = process normally.</summary>
    public Func<JsonArray, (HttpStatusCode Code, JsonNode Body)?>? BatchError { get; set; }
    /// <summary>Queued pull pages; when empty the server returns no changes and the last cursor.</summary>
    public Queue<JsonObject> PullPages { get; } = new();
    private long _rowVersion;
    private string _cursor = "0";

    public void EnqueuePull(string nextCursor, bool hasMore, params JsonObject[] changes) =>
        PullPages.Enqueue(new JsonObject
        {
            ["server_time"] = "2026-11-21T06:15:00+00:00",
            ["next_cursor"] = nextCursor,
            ["has_more"] = hasMore,
            ["changes"] = new JsonArray(changes.Select(c => (JsonNode)c).ToArray()),
        });

    public static JsonObject Change(string aggregate, Guid id, JsonNode? data, long rowVersion = 1, bool delete = false) => new()
    {
        ["aggregate"] = aggregate,
        ["entity_id"] = id.ToString(),
        ["operation"] = delete ? "delete" : "upsert",
        ["row_version"] = rowVersion,
        ["origin_node_id"] = null,
        ["changed_at"] = "2026-11-21T06:00:00+00:00",
        ["data"] = data,
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (Requests) Requests.Add(request);
        if (Offline) throw new HttpRequestException("No route to host (fake offline)");
        var path = request.RequestUri!.AbsolutePath;

        if (path == "/health") return Json(HttpStatusCode.OK, new JsonObject { ["status"] = "ok" });

        if (path == "/api/sync/token")
        {
            if (Revoked) return Json(HttpStatusCode.Forbidden, new JsonObject { ["error"] = "node_revoked" });
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            if (string.IsNullOrEmpty(body["node_secret"]?.GetValue<string>()))
                return Json(HttpStatusCode.Unauthorized, new JsonObject { ["error"] = "invalid_node_credentials" });
            TokensIssued++;
            CurrentToken = "tok-" + TokensIssued;
            return Json(HttpStatusCode.OK, new JsonObject
            {
                ["access_token"] = CurrentToken, ["token_type"] = "Bearer", ["expires_in"] = 3600,
                ["expires_at"] = DateTimeOffset.UtcNow.AddHours(1).ToString("O"), ["node_id"] = body["node_id"]?.DeepClone(),
            });
        }

        if (Revoked) return Json(HttpStatusCode.Forbidden, new JsonObject { ["error"] = "node_revoked" });
        if (UnauthorizedResponses > 0)
        {
            UnauthorizedResponses--;
            return Json(HttpStatusCode.Unauthorized, new JsonObject { ["error"] = "token_expired" });
        }
        if (request.Headers.Authorization?.Parameter != CurrentToken)
            return Json(HttpStatusCode.Unauthorized, new JsonObject { ["error"] = "bad_token" });

        if (path == "/api/sync/push")
        {
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            var batchErr = BatchError?.Invoke(body["operations"]!.AsArray());
            if (batchErr is { } be) return Json(be.Code, be.Body);
            var results = new JsonArray();
            foreach (var op in body["operations"]!.AsArray().OfType<JsonObject>())
            {
                PushedOperations.Enqueue((JsonObject)op.DeepClone());
                var result = OnPush?.Invoke(op) ?? Applied(op);
                results.Add(result);
            }
            return Json(HttpStatusCode.OK, new JsonObject { ["server_time"] = "2026-11-21T06:15:00+00:00", ["results"] = results });
        }

        if (path == "/api/sync/pull")
        {
            var q = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
            PullCursors.Add(q["since"]);
            if (PullPages.TryDequeue(out var page))
            {
                _cursor = page["next_cursor"]!.GetValue<string>();
                return Json(HttpStatusCode.OK, page);
            }
            return Json(HttpStatusCode.OK, new JsonObject
            {
                ["server_time"] = "2026-11-21T06:15:00+00:00", ["next_cursor"] = _cursor, ["has_more"] = false, ["changes"] = new JsonArray(),
            });
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    public JsonObject Applied(JsonObject op, string status = "applied", string? reason = null, JsonNode? current = null) => new()
    {
        ["operation_id"] = op["operation_id"]!.DeepClone(),
        ["aggregate"] = op["aggregate"]!.DeepClone(),
        ["entity_id"] = op["entity_id"]!.DeepClone(),
        ["status"] = status,
        ["row_version"] = Interlocked.Increment(ref _rowVersion),
        ["reason_code"] = reason,
        ["message"] = null,
        ["conflict_fields"] = null,
        ["current"] = current,
        ["replayed"] = false,
    };

    private static HttpResponseMessage Json(HttpStatusCode code, JsonNode body) => new(code)
    {
        Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
    };
}
//JJQR - END
