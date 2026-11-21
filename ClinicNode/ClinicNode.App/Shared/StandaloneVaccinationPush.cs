//JJQR - AI (21 Nov 2026) - TASK STANDALONE VACCINE PUSH. Standalone vaccinations (no consultation) written on this computer never reach the cloud:
// the sync push engine ignores patient.vaccinations. The backend has ONE real route for them: POST /api/patient-vaccinations
// (VaccinationsController.Create, roles Doctor/Staff/Admin, body = CreateVaccinationRequest, client-supplied id; an id already in the cloud → 409).
// It needs the signed-in user's online session (not the node token), so it runs here, beside the live coordinator, only while online + signed in online.
// Rules: only rows with consultation_id NULL that were never pushed and never pulled (node_sync_swept key "standalone_vaccination");
// only AFTER their patient is known to the cloud (row version present, no pending patient op); 200/201 or 409 id_conflict → marked done;
// 400/401/403/404/other 409 → parked + one review item (see TASKS 3 + 4 below); 5xx/offline → next run. Upsert only: nothing is ever deleted, locally or in the cloud. Nothing is logged (patient data).
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using ClinicNode.Api.Http;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.App.Shared;

public sealed class StandaloneVaccinationPush(IApiClient api, IDbContextFactory<ClinicAppDbContext> factory)
{
    public const string SweptKey = SyncLocalStore.StandaloneVaccinationSweptKey;
    public const int BatchSize = 25;
    /// <summary>Server cap on reaction_notes (API-SURFACE: max 2000). A longer note is never cut: the row stays on this computer and is listed for review once.</summary>
    public const int ReactionNotesMax = 2000;
    //JJQR - AI (21 Nov 2026) - TASKS 3 + 4 (vaccine push rules)
    //   200 / 201 and 409 {error:"id_conflict"} → done (marked pushed).
    //   400 / 401 / 403 / 404 / other 409 → the row stays on this computer (never deleted, never marked pushed), ONE "skipped" review item per
    //     (row, reason, server message), and the row is NOT re-posted every run: it is parked until it changes (its updated_at moves, i.e. it was
    //     corrected) or the app restarts; a 401 park is also lifted when the sign-in changes (ClearSessionBlocks).
    //   5xx / offline / network → retried next run, as before.
    //   reaction_notes > 2000 → never cut, never sent, one review item; re-checked every run (sent once shortened).
    //   All skip checks run BEFORE the batch limit, so parked rows never use up the 25 posts of a run.
    public const string RejectedReason = "vaccination_rejected";          // 400
    public const string SessionExpiredReason = "vaccination_session_expired"; // 401
    public const string ForbiddenReason = "vaccination_forbidden";        // 403
    public const string NotFoundReason = "vaccination_not_found";         // 404
    public const string ConflictReason = "vaccination_conflict";          // 409 other than id_conflict
    public const string TooLongReason = "reaction_notes_too_long";

    private sealed record Parked(long Fingerprint, string Reason);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Parked> _parked = new();
    private readonly HashSet<(Guid Id, string Reason, string Message)> _told = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The sign-in changed: rows parked by a 401 may go up again.</summary>
    public void ClearSessionBlocks()
    {
        foreach (var kv in _parked)
            if (kv.Value.Reason == SessionExpiredReason) _parked.TryRemove(kv.Key, out _);
    }

    private static long Fingerprint(PatientVaccination v) => v.UpdatedAt.UtcTicks;

    /// <summary>Push up to <see cref="BatchSize"/> local-only standalone vaccinations. Never throws.</summary>
    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct)) return 0;
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            await SyncLocalStore.EnsureSchemaAsync(db, ct);
            var done = (await db.Database.SqlQueryRaw<string>(
                    "SELECT entity_id AS Value FROM node_sync_swept WHERE aggregate = {0}", SweptKey).ToListAsync(ct))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var rows = await db.PatientVaccinations.AsNoTracking()
                .Where(v => v.ConsultationId == null && v.DeletedAt == null)
                .ToListAsync(ct);
            rows = rows.OrderBy(v => v.CreatedAt).ToList(); // SQLite cannot ORDER BY a DateTimeOffset

            // 1) skip checks first (task 4): already pushed, parked and unchanged, too-long notes, patient not in the cloud yet
            var ready = new List<PatientVaccination>();
            foreach (var v in rows)
            {
                if (done.Contains(v.Id.ToString())) continue;
                if (_parked.TryGetValue(v.Id, out var p))
                {
                    if (p.Fingerprint == Fingerprint(v)) continue;  // unchanged since the server refused it
                    _parked.TryRemove(v.Id, out _);                // corrected: try again
                }
                if ((v.ReactionNotes?.Trim().Length ?? 0) > ReactionNotesMax)
                {
                    await TellAsync(db, v, TooLongReason,
                        $"A vaccination recorded on this computer was not sent: its reaction notes are longer than {ReactionNotesMax} characters. Shorten them, then it will be sent.", "", ct);
                    continue;
                }
                var pid = v.PatientId.ToString();
                if (await SyncLocalStore.GetRowVersionAsync(db, Aggregates.Patient, pid, ct) is null) continue; // patient not in the cloud yet
                if (await SyncLocalStore.HasPendingAsync(db, Aggregates.Patient, pid, ct)) continue;            // patient push still queued
                ready.Add(v);
            }

            // 2) post at most BatchSize rows that can actually go
            var pushed = 0;
            foreach (var v in ready.Take(BatchSize))
            {
                try
                {
                    await api.SendAsync(HttpMethod.Post, "/api/patient-vaccinations", Body(v), null, ct); // 200 or 201 (any 2xx) = created
                }
                catch (ApiConflictException ex) when (IsIdConflict(ex.Body)) { /* 409 id_conflict: this id is already in the cloud */ }
                catch (ApiException ex) when (ParkReason(ex) is { } reason)
                {
                    var msg = ServerMessage(ex);
                    _parked[v.Id] = new Parked(Fingerprint(v), reason);
                    await TellAsync(db, v, reason, $"A vaccination recorded on this computer was not sent: {Why(reason)} ({msg}). {Fix(reason)}", msg, ct);
                    continue;
                }
                catch (ApiException) { continue; }        // 5xx / offline: try again next run, keep the row
                catch (HttpRequestException) { continue; }
                await SyncLocalStore.MarkSweptAsync(db, SweptKey, v.Id.ToString(), ct);
                pushed++;
            }
            return pushed;
        }
        catch (Exception) { return 0; } // never break the app; the next run retries
        finally { _gate.Release(); }
    }

    private static string? ParkReason(ApiException ex) => ex.Status switch
    {
        400 => RejectedReason,
        401 => SessionExpiredReason,
        403 => ForbiddenReason,
        404 => NotFoundReason,
        409 => ConflictReason, // id_conflict was caught before
        _ => null,
    };

    private static string Why(string reason) => reason switch
    {
        RejectedReason => "the clinic server refused it",
        SessionExpiredReason => "the sign-in expired",
        ForbiddenReason => "this account is not allowed to add it",
        NotFoundReason => "the clinic server could not find its patient or route",
        ConflictReason => "the clinic server reported a conflict",
        _ => "the clinic server did not accept it",
    };

    private static string Fix(string reason) => reason switch
    {
        SessionExpiredReason => "Sign in again; it will be sent after that.",
        ForbiddenReason => "Sign in with a doctor, staff or admin account; it will be sent after that or when the record is corrected.",
        _ => "Correct it, then it will be sent.",
    };

    private async Task TellAsync(ClinicAppDbContext db, PatientVaccination v, string reason, string message, string serverMessage, CancellationToken ct)
    {
        if (!_told.Add((v.Id, reason, serverMessage))) return; // one review item per situation per app run
        await SyncLocalStore.AddConflictAsync(db, new SyncConflict
        {
            Aggregate = "patient_vaccination", EntityId = v.Id.ToString(), Kind = "skipped", ReasonCode = reason, Message = message,
        }, ct);
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - TASK VACCINE 400 helpers
    private static bool IsIdConflict(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.String
                && string.Equals(e.GetString(), "id_conflict", StringComparison.OrdinalIgnoreCase);
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    /// <summary>The server's own message (validation field messages when present). Never includes the request body.</summary>
    private static string ServerMessage(ApiException ex)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ex.Message)) parts.Add(ex.Message.Trim());
        if (ex is ApiValidationException { Errors.Count: > 0 } v)
            parts.AddRange(v.Errors.SelectMany(kv => kv.Value).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim()));
        var text = string.Join(" ", parts.Distinct());
        return string.IsNullOrWhiteSpace(text) ? "no reason given" : text.Length > 500 ? text[..500] : text;
    }
    //JJQR - END

    // Exactly the CreateVaccinationRequest fields, from the local row's own columns (no invented columns). The three fields from the brief:
    // reaction_notes, administered_date, status. Source is required by the request (enum, non-null) and is the row's own value.
    private static object Body(PatientVaccination v) => new
    {
        v.Id,
        v.PatientId,
        ConsultationId = (Guid?)null,
        v.VaccineName,
        v.Manufacturer,
        v.DoseNumber,
        v.Route,
        v.Site,
        v.LotNumber,
        v.ExpiryDate,
        v.AdministeredDate,
        v.AdministeredBy,
        v.NextDoseDate,
        v.Status,
        v.Source,
        v.Notes,
        ReactionNotes = string.IsNullOrWhiteSpace(v.ReactionNotes) ? null : v.ReactionNotes.Trim(), // ≤ 2000 (checked above)
    };
}
//JJQR - END
