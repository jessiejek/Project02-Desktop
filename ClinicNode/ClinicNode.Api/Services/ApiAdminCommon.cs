//JJQR - AI (21 Nov 2026) - shared helpers for the admin Api services (ApiAdmin*Service): query strings, the server's PagedResult, and how a real server answer becomes an AdminResult.
// Only ApiOfflineException / ApiUnauthorizedException / ApiNotSupportedException must escape to the online-first router (it then uses the local copy);
// every other ApiException is the server's answer and is shown to the admin as AdminResult.Fail(message), exactly like the web shows the API message.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClinicApp.Domain.Entities;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

/// <summary>Project02-be <c>PagedResult&lt;T&gt;</c> (its property names are pinned camelCase on the wire).</summary>
internal sealed class ApiPaged<T>
{
    [JsonPropertyName("items")] public List<T>? Items { get; set; }
    [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("pageSize")] public int PageSize { get; set; }
}

internal static class ApiAdmin
{
    public static Dictionary<string, string?> Q(params (string Key, object? Value)[] pairs)
    {
        var d = new Dictionary<string, string?>();
        foreach (var (k, v) in pairs)
        {
            d[k] = v switch
            {
                null => null,
                string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Guid g => g.ToString("D"),
                bool b => b ? "true" : "false",
                Enum e => e.ToString(),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => v.ToString(),
            };
        }
        return d;
    }

    /// <summary>True for an answer from the server (validation, 403, 404, 409, 5xx). False for "could not reach it" / "session gone", which the router must see.</summary>
    public static bool IsAnswer(ApiException ex) => ex is not ApiOfflineException and not ApiUnauthorizedException;

    public static string PatientName(Patient? p) => p is null ? "" : $"{p.FirstName} {p.LastName}".Trim();

    public static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static AdminBookingListItem ToListItem(Booking b) => new(
        b.BookingId,
        b.PatientId,
        PatientName(b.Patient),
        b.Patient?.PatientCode ?? "",
        b.DoctorId,
        b.Doctor?.StaffAccount?.FullName ?? "",
        b.AppointmentDate,
        b.SlotStartTime,
        b.SlotEndTime,
        b.Status,
        b.Payment?.Status,
        b.QueueNumber,
        b.IsWalkIn);

    /// <summary>Reads a Guid from an object answer whose casing differs by endpoint (POST /api/auth/invite answers userId / staffId or user_id / staff_id).</summary>
    public static Guid ReadGuid(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object) return Guid.Empty;
        foreach (var p in root.EnumerateObject())
            foreach (var n in names)
                if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String && Guid.TryParse(p.Value.GetString(), out var g))
                    return g;
        return Guid.Empty;
    }
}
//JJQR - END
