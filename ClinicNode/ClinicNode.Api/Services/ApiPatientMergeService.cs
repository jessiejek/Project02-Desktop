//JJQR - AI (2026-11-26) - GAV-12: "Merge to existing patient" on this computer, online only (the web's src/lib/data/mergePatient.ts):
//   GET  /api/patients/merge-preview?keep=&merge=   both records side by side, counts, visits open today
//   POST /api/patients/merge                        one server transaction (doctor + admin); 409 both_have_accounts / patient_deleted are answers
// Only ApiOfflineException / ApiUnauthorizedException escape (the router then answers "needs an internet connection"). Nothing is logged (patient data).
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiPatientMergeService(IApiClient api) : IPatientMergeService
{
    public async Task<AdminResult<MergeToExistingPreview>> PreviewAsync(Guid keepPatientId, Guid mergePatientId, CancellationToken ct = default)
    {
        try
        {
            var o = await api.GetAsync<JsonObject>("/api/patients/merge-preview", ApiAdmin.Q(("keep", keepPatientId), ("merge", mergePatientId)), ct);
            if (o?["keep"] is not JsonObject k || o["merge"] is not JsonObject m) return AdminResult<MergeToExistingPreview>.Fail("These patients could not be loaded.");
            return AdminResult<MergeToExistingPreview>.Ok(new MergeToExistingPreview(Side(k), Side(m), B(o, "both_have_accounts"), Active(o["keep_active_today"]), Active(o["merge_active_today"])));
        }
        catch (ApiNotFoundException) { return AdminResult<MergeToExistingPreview>.Fail("One of the patients was not found."); }
        catch (ApiForbiddenException) { return AdminResult<MergeToExistingPreview>.Fail("Only the doctor or an admin can merge patients."); }
        catch (ApiConflictException ex) { return AdminResult<MergeToExistingPreview>.Fail(Message(ex.Body) ?? ex.Message); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<MergeToExistingPreview>.Fail(ex.Message); }
    }

    public async Task<PatientMergeOutcome> MergeAsync(Guid keepPatientId, Guid mergePatientId, string note, CancellationToken ct = default)
    {
        if (PatientMergeRules.Problem(mergePatientId, keepPatientId, note) is { } bad) return PatientMergeOutcome.Fail(bad);
        try
        {
            var o = await api.SendAsync<JsonObject>(HttpMethod.Post, "/api/patients/merge", new { KeepPatientId = keepPatientId, MergePatientId = mergePatientId, Note = note.Trim() }, null, ct);
            if (o is null) return PatientMergeOutcome.Fail("The clinic server did not answer. Check both patients before trying again.");
            var moved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (o["moved"] is JsonObject mv) foreach (var (kk, v) in mv) if (v is JsonValue jv && jv.TryGetValue<int>(out var n)) moved[kk] = n;
            var filled = (o["filled_fields"] as JsonArray ?? []).Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList();
            return PatientMergeOutcome.Ok(new PatientMerged(G(o, "kept_patient_id"), S(o, "kept_patient_code") ?? "", G(o, "merged_patient_id"), S(o, "merged_patient_code") ?? "", moved, filled));
        }
        catch (ApiConflictException ex) { return PatientMergeOutcome.Fail(Message(ex.Body) ?? ex.Message); }
        catch (ApiForbiddenException) { return PatientMergeOutcome.Fail("Only the doctor or an admin can merge patients."); }
        catch (ApiNotFoundException ex) { return PatientMergeOutcome.Fail(Message(ex.Body) ?? "One of the patients was not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return PatientMergeOutcome.Fail(ex.Message); }
    }

    static MergeSide Side(JsonObject c)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (c["counts"] is JsonObject co) foreach (var (k, v) in co) if (v is JsonValue jv && jv.TryGetValue<int>(out var n)) counts[k] = n;
        return new MergeSide(G(c, "patient_id"), S(c, "patient_code") ?? "", S(c, "first_name") ?? "", S(c, "middle_name"), S(c, "last_name") ?? "", D(c, "date_of_birth"), S(c, "sex"),
            S(c, "contact_number"), S(c, "email"), S(c, "address"), S(c, "city"), S(c, "civil_status"), S(c, "blood_type"), S(c, "philhealth_number"), S(c, "hmo_provider"),
            B(c, "has_account"), DateTimeOffset.TryParse(S(c, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : null, counts);
    }

    static IReadOnlyList<MergeActiveVisit> Active(JsonNode? n) =>
        (n as JsonArray ?? []).OfType<JsonObject>().Select(v => new MergeActiveVisit(G(v, "booking_id"), S(v, "queue_number"), S(v, "status") ?? "")).ToList();

    static string? Message(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonNode.Parse(body) is JsonObject o ? S(o, "message") : null; } catch (JsonException) { return null; }
    }

    static string? S(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    static bool B(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    static Guid G(JsonObject o, string k) => Guid.TryParse(S(o, k), out var g) ? g : Guid.Empty;
    static DateOnly? D(JsonObject o, string k) =>
        S(o, k) is { } s && DateOnly.TryParse(s.Length >= 10 ? s[..10] : s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
//JJQR - END
