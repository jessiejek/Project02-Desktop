//JJQR - AI (21 Nov 2026) - staff lane, online-first: wire DTOs (snake_case, enums by name) shared by the ApiStaff* services, plus the
// "real server answer → StaffResult.Fail(message)" helper. Network / timeout / 401-after-refresh / not-supported are NOT caught here:
// they reach the OnlineFirstProxy, which then serves the call from the local SQLite copy (and sync catches up).
using System.Globalization;
using System.Text.Json.Serialization;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Staff;

namespace ClinicNode.Api.Services;

internal static class ApiStaff
{
    /// <summary>{ items, totalCount } paged wrapper of /api/bookings/staff/*, /api/patients/search (camelCase on the wire).</summary>
    internal sealed class Paged<T>
    {
        [JsonPropertyName("items")] public List<T>? Items { get; set; }
        [JsonPropertyName("totalCount")] public int? TotalCount { get; set; }
        [JsonPropertyName("page")] public int? Page { get; set; }
        [JsonPropertyName("pageSize")] public int? PageSize { get; set; }
    }

    internal sealed record StaffEmbed(string? FullName, string? Status);
    internal sealed record DoctorEmbed(Guid? DoctorId, string? Specialization, StaffEmbed? StaffAccounts);
    internal sealed record PatientEmbed(Guid? PatientId, string? FirstName, string? LastName, string? PatientCode, string? ContactNumber, string? Email,
        SexType? Sex, DateOnly? DateOfBirth, Guid? UserId, bool? IsGuest);
    internal sealed record PaymentEmbed(Guid? PaymentId, PaymentStatus? Status, string? WaivedReason, string? OrNumber, decimal? Amount, PaymentMethod? PaymentMethod,
        decimal? AmountReceived, string? ReferenceNumber, DateTimeOffset? ConfirmedAt);

    /// <summary>The canonical booking row (§4) with its §6 embeds: patients / doctors.staff_accounts / payments.</summary>
    internal sealed record BookingDto(
        Guid BookingId, Guid PatientId, Guid DoctorId, DateOnly AppointmentDate, TimeOnly? SlotStartTime, BookingStatus Status, PaymentMode? PaymentMode,
        string? QueueNumber, decimal? AmountDue, bool? IsWalkIn, VisitType? VisitType, string? DiscountCategory, DateTimeOffset? CreatedAt,
        PatientEmbed? Patients, DoctorEmbed? Doctors, PaymentEmbed? Payments);

    internal sealed record QueueSummaryDto(int Booked, int Waiting, int InProgress, int Completed, int NoShow, int Total);
    internal sealed record QueueItemDto(Guid BookingId, string? QueueNumber, bool? IsPriority, Guid PatientId, string? PatientName, string? PatientCode,
        BookingStatus Status, VisitType? VisitType, decimal? AmountDue, DateTimeOffset? CheckedInAt, bool? IsWalkIn);
    /// <summary>GET /api/queue (today, S- lane first, then first come first served).</summary>
    internal sealed record QueueBoardDto(DateOnly? Date, QueueSummaryDto? Summary, List<QueueItemDto>? Items);

    internal static string PatientName(PatientEmbed? p) => p is null ? "" : $"{p.FirstName} {p.LastName}".Trim();

    internal static TimeOnly ManilaTime(DateTimeOffset? t) =>
        t is { } v ? TimeOnly.FromDateTime(v.ToOffset(TimeSpan.FromHours(8)).DateTime) : TimeOnly.MinValue;

    internal static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static Dictionary<string, string?> Q(params (string Key, string? Value)[] pairs)
    {
        var d = new Dictionary<string, string?>();
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    /// <summary>Is this a real answer from the server (shown to the desk, never retried locally)?</summary>
    internal static bool IsServerAnswer(Exception ex) =>
        ex is ApiValidationException or ApiForbiddenException or ApiNotFoundException or ApiConflictException or ApiServerException;

    /// <summary>Run a write; a server refusal becomes <c>StaffResult.Fail(server message)</c> (as the web shows <c>err.body.message</c>).</summary>
    internal static async Task<StaffResult> Write(Func<Task> call, Guid? id, string fallback)
    {
        try
        {
            await call();
            return StaffResult.Ok(id);
        }
        catch (Exception ex) when (IsServerAnswer(ex))
        {
            return StaffResult.Fail(string.IsNullOrWhiteSpace(ex.Message) ? fallback : ex.Message);
        }
    }

    /// <summary>A nice-to-have read (the web wraps these in try/catch): a server refusal yields <paramref name="fallback"/>; offline still propagates.</summary>
    internal static async Task<T> Optional<T>(Func<Task<T>> call, T fallback)
    {
        try { return await call(); }
        catch (Exception ex) when (IsServerAnswer(ex)) { return fallback; }
    }
}
//JJQR - END
