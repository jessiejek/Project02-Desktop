//JJQR - AI (21 Nov 2026) - doctor-side write-through (same pattern as ViewModels/Staff/StaffLocalWriteThrough, which is NOT changed):
// after an ONLINE consultation save the Rx / lab request / certificate / slip are printed from the local SQLite copy (IPrintDocumentFactory),
// but the server rows only arrive with the next sync pull, so a print right after "Save" could show the previous version.
// This copies the server's rows into SQLite right away with the sync pull's own apply rules (AggregateDocuments, ServerResult):
//   parents  StaffLocalWriteThrough.EnsureBookingAsync (doctor → patient → booking → payment)
//   GET /api/consultations/by-booking/{id}                  → "consultation" document: consultation + diagnoses (consultation_diagnoses embed)
//   GET /api/prescription-groups?bookingId=                  → prescription { group, line_items }   (none → prescription removed)
//   GET /api/lab-orders/by-booking/{id}                      → lab_orders (this consultation's rows)
//   GET /api/patient-vaccinations/by-consultation/{cid}      → vaccinations
//   GET /api/follow-ups?consultationId= (or the embed)       → follow_up
//   GET /api/medical-certificates/by-consultation/{cid}      → medical_certificate (404 → removed)
//   GET /api/vitals?bookingId=                               → vital_reading rows (append-only)
// A child set that could not be read is left OUT of the document (absent key = keep the local set, contract §11).
// Offline / offline sign-in → nothing to do (the save already went to SQLite). A consultation with unpushed local changes is never overwritten.
// Every failure is swallowed (the print then behaves as before). Nothing is logged (patient data).
using System.Text.Json.Nodes;
using ClinicApp.Infrastructure;
using ClinicNode.Api.Http;
using ClinicNode.App.ViewModels.Staff;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Doctor;

public static class ConsultLocalWriteThrough
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Copies this visit's consultation (and its children) from the server into SQLite. Safe to call before every print.</summary>
    public static async Task EnsureConsultationAsync(IServiceProvider sp, Guid bookingId, CancellationToken ct = default)
    {
        var api = sp.GetService<IApiClient>();
        var tokens = sp.GetService<IApiTokenStore>();
        var connectivity = sp.GetService<IApiConnectivity>();
        var factory = sp.GetService<IDbContextFactory<ClinicAppDbContext>>();
        if (api is null || tokens is null || connectivity is null || factory is null) return;
        if (tokens.AccessToken is not { Length: > 0 } || !connectivity.IsOnline) return;

        // parents first (booking FK); it has its own gate and swallows its own failures
        await StaffLocalWriteThrough.EnsureBookingAsync(sp, bookingId, withPayment: true, ct);

        await Gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10)); // never hold up the print for long
            var t = timeout.Token;

            JsonObject? consult;
            try { consult = await api.GetAsync<JsonObject>($"/api/consultations/by-booking/{bookingId:D}", null, t); }
            catch (ApiNotFoundException) { return; } // nothing saved yet
            if (consult is null || !Guid.TryParse(Str(consult, "consultation_id", "consultationId"), out var cid)) return;

            var doc = new JsonObject { ["consultation"] = Strip(consult) };
            if (consult["consultation_diagnoses"] is JsonArray dx) doc["diagnoses"] = Rows(dx);

            await TryAsync(async () =>
            {
                var groups = await api.GetAsync<JsonArray>("/api/prescription-groups", Q("bookingId", bookingId), t) ?? new JsonArray();
                var group = groups.OfType<JsonObject>().FirstOrDefault();
                doc["prescription"] = group is null ? null : new JsonObject
                {
                    ["group"] = Strip(group),
                    ["line_items"] = group["prescription_line_items"] is JsonArray items ? Rows(items) : new JsonArray(),
                };
            });
            await TryAsync(async () =>
            {
                var labs = await api.GetAsync<JsonArray>($"/api/lab-orders/by-booking/{bookingId:D}", null, t) ?? new JsonArray();
                doc["lab_orders"] = Rows(labs.OfType<JsonObject>().Where(l => !Guid.TryParse(Str(l, "consultation_id", "consultationId"), out var lc) || lc == cid));
            });
            await TryAsync(async () =>
                doc["vaccinations"] = Rows(await api.GetAsync<JsonArray>($"/api/patient-vaccinations/by-consultation/{cid:D}", null, t) ?? new JsonArray()));
            await TryAsync(async () =>
            {
                var fu = consult["follow_ups"] as JsonObject
                         ?? (await api.GetAsync<JsonArray>("/api/follow-ups", Q("consultationId", cid), t) ?? new JsonArray()).OfType<JsonObject>().FirstOrDefault();
                doc["follow_up"] = fu is null ? null : Strip(fu);
            });
            await TryAsync(async () =>
            {
                try
                {
                    var cert = await api.GetAsync<JsonObject>($"/api/medical-certificates/by-consultation/{cid:D}", null, t);
                    doc["medical_certificate"] = cert is null ? null : Strip(cert);
                }
                catch (ApiNotFoundException) { doc["medical_certificate"] = null; }
            });
            JsonArray vitals = new JsonArray();
            await TryAsync(async () => vitals = await api.GetAsync<JsonArray>("/api/vitals", Q("bookingId", bookingId), t) ?? new JsonArray());

            await using var db = await factory.CreateDbContextAsync(t);
            await SyncLocalStore.EnsureSchemaAsync(db, t);
            if (await SyncLocalStore.HasPendingAsync(db, Aggregates.Consultation, cid.ToString(), t)) return; // never overwrite unpushed local work
            await AggregateDocuments.ApplyAsync(db, new PulledChange(Aggregates.Consultation, cid.ToString(), false, doc), ApplyMode.ServerResult, t);

            foreach (var v in vitals.OfType<JsonObject>())
            {
                var id = Str(v, "id", "reading_id", "readingId");
                if (string.IsNullOrEmpty(id)) continue;
                try { await AggregateDocuments.ApplyAsync(db, new PulledChange(Aggregates.VitalReading, id, false, Strip(v)), ApplyMode.ServerResult, t); }
                catch (Exception ex) when (ex is not OperationCanceledException) { db.ChangeTracker.Clear(); }
            }
        }
        catch (Exception)
        {
            // offline mid-way, server refusal, FK miss …: the next sync pull brings the rows; the print path reports its own error
        }
        finally { Gate.Release(); }
    }

    private static async Task TryAsync(Func<Task> read)
    {
        try { await read(); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* leave that child set out: the local copy is kept */ }
    }

    private static Dictionary<string, string?> Q(string key, Guid id) => new() { [key] = id.ToString("D") };

    private static JsonArray Rows(IEnumerable<JsonNode?> rows)
    {
        var arr = new JsonArray();
        foreach (var r in rows) if (r is JsonObject o) arr.Add(Strip(o));
        return arr;
    }

    /// <summary>REST rows carry embeds: keep only the row's own scalar columns.</summary>
    private static JsonObject Strip(JsonObject row)
    {
        var copy = new JsonObject();
        foreach (var (k, v) in row)
            if (v is not JsonObject and not JsonArray) copy[k] = v?.DeepClone();
        return copy;
    }

    private static string? Str(JsonObject o, params string[] keys)
    {
        foreach (var k in keys)
            if (o[k] is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        return null;
    }
}
//JJQR - END
