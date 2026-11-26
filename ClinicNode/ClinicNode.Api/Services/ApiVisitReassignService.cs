//JJQR - AI (2026-11-26) - GAV-11: "Wrong patient?" on this computer, online only (the web's src/lib/data/reassign.ts):
//   GET  /api/bookings/{id}/reassign-preview?target=   what moves, the allergies (added during this visit or not), the chosen person's same-day visits
//   POST /api/bookings/{id}/reassign-patient            one server transaction; 409 target_has_visit_that_day / possible_duplicate are questions
// Only ApiOfflineException / ApiUnauthorizedException escape (the router then answers "needs an internet connection"); every other server answer
// becomes the dialog's message. Nothing is logged (patient data).
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

public sealed class ApiVisitReassignService(IApiClient api) : IVisitReassignService
{
    public async Task<AdminResult<WrongPatientPreview>> GetPreviewAsync(Guid bookingId, Guid? targetPatientId = null, CancellationToken ct = default)
    {
        try
        {
            var q = targetPatientId is Guid t && t != Guid.Empty ? ApiAdmin.Q(("target", t)) : null;
            var o = await api.GetAsync<JsonObject>($"/api/bookings/{bookingId:D}/reassign-preview", q, ct);
            return o is null ? AdminResult<WrongPatientPreview>.Fail("Visit not found.") : AdminResult<WrongPatientPreview>.Ok(ParsePreview(o));
        }
        catch (ApiNotFoundException) { return AdminResult<WrongPatientPreview>.Fail("Visit not found."); }
        catch (ApiForbiddenException) { return AdminResult<WrongPatientPreview>.Fail("Only the visit's doctor or an admin can move a visit to another patient."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<WrongPatientPreview>.Fail(ex.Message); }
    }

    public async Task<WrongPatientOutcome> ReassignAsync(Guid bookingId, WrongPatientRequest r, CancellationToken ct = default)
    {
        if (WrongPatientRules.ReasonProblem(r.Reason) is { } bad) return WrongPatientOutcome.Fail(bad);
        var body = new
        {
            Mode = r.NewPatient ? "new" : "existing",
            NewPatient = r.NewPatient && r.Person is not null ? WrongPatientRules.NewPatientRow(r.Person) : null,
            TargetPatientId = r.NewPatient ? null : r.TargetPatientId,
            Reason = r.Reason.Trim(),
            MoveAllergyIds = r.MoveAllergyIds,
            AcknowledgeSameDayVisit = r.AcknowledgeSameDayVisit,
            AcknowledgePossibleDuplicate = r.AcknowledgePossibleDuplicate,
        };
        try
        {
            var o = await api.SendAsync<JsonObject>(HttpMethod.Post, $"/api/bookings/{bookingId:D}/reassign-patient", body, null, ct);
            if (o is null) return WrongPatientOutcome.Fail("The clinic server did not answer. Check the visit before trying again.");
            return WrongPatientOutcome.Ok(new WrongPatientMoved(
                bookingId, G(o, "from_patient_id"), G(o, "to_patient_id"), S(o, "patient_code") ?? "", S(o, "queue_number"), o["already"]?.GetValue<bool>() == true));
        }
        catch (ApiConflictException ex)
        {
            var o = Parse(ex.Body);
            var code = o is null ? null : S(o, "error");
            var message = (o is null ? null : S(o, "message")) ?? ex.Message;
            var warning = WrongPatientRules.WarningFor(code);
            if (warning == WrongPatientWarning.None) return WrongPatientOutcome.Fail(message);
            var matches = (o?["matches"] as JsonArray)?.OfType<JsonObject>().Select(Person).ToList();
            return WrongPatientOutcome.Warn(warning, message, matches);
        }
        catch (ApiForbiddenException) { return WrongPatientOutcome.Fail("Only the visit's doctor or an admin can move a visit to another patient."); }
        catch (ApiNotFoundException ex) { return WrongPatientOutcome.Fail(Parse(ex.Body) is { } o && S(o, "message") is { } m ? m : "Visit not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return WrongPatientOutcome.Fail(ex.Message); }
    }

    internal static WrongPatientPreview ParsePreview(JsonObject o)
    {
        var allergies = (o["allergies"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(a => new WrongPatientAllergy(G(a, "id"), S(a, "allergen") ?? "", S(a, "reaction"), S(a, "severity"), B(a, "is_nkda"), B(a, "added_during_visit"))).ToList();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (o["counts"] is JsonObject c) foreach (var (k, v) in c) if (v is JsonValue jv && jv.TryGetValue<int>(out var n)) counts[k] = n;
        WrongPatientTarget? target = null;
        if (o["target"] is JsonObject t)
        {
            var visits = (t["visits_that_day"] as JsonArray ?? []).OfType<JsonObject>()
                .Select(v => new WrongPatientSameDayVisit(G(v, "booking_id"), S(v, "queue_number"), S(v, "status") ?? "")).ToList();
            target = new WrongPatientTarget(Person(t), B(t, "unavailable"), visits);
        }
        return new WrongPatientPreview(
            G(o, "booking_id"), D(o, "appointment_date") ?? default, S(o, "queue_number"), S(o, "status") ?? "",
            S(o, "consultation_id") is { } cid && Guid.TryParse(cid, out var cg) ? cg : null, S(o, "consultation_status"), S(o, "payment_status"), S(o, "or_number"),
            o["patient"] is JsonObject p ? Person(p) : new WrongPatientPerson(Guid.Empty, "", "", ""), allergies, counts, target);
    }

    static WrongPatientPerson Person(JsonObject p) => new(G(p, "patient_id"), S(p, "patient_code") ?? "", S(p, "first_name") ?? "", S(p, "last_name") ?? "", D(p, "date_of_birth"));

    static JsonObject? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try { return JsonNode.Parse(body) as JsonObject; } catch (JsonException) { return null; }
    }

    static string? S(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    static bool B(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    static Guid G(JsonObject o, string k) => Guid.TryParse(S(o, k), out var g) ? g : Guid.Empty;
    static DateOnly? D(JsonObject o, string k) =>
        S(o, k) is { } s && DateOnly.TryParse(s.Length >= 10 ? s[..10] : s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
}
//JJQR - END
