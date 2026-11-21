//JJQR - AI (21 Nov 2026) - doctor overflow screens, online-first: wire DTOs (snake_case on the wire via ApiJson.Data; enums by name) shared by the
// ApiDoctorExtra* services, and the "real server answer -> DoctorResult.Fail(message)" helper. Network / timeout / 401-after-refresh / not-supported
// are NOT caught here: they reach the OnlineFirstProxy, which then serves the call from the local SQLite copy.
using System.Globalization;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.Api.Services;

internal static class ApiDoctorExtra
{
    // ── embeds (§6) ───────────────────────────────────────────────────────
    internal sealed record StaffEmbed(string? FullName, string? Email, string? ContactNumber);
    internal sealed record DoctorEmbed(Guid? DoctorId, string? Specialization, string? Bio, decimal? ConsultationFee, string? LicenseNumber,
        string? PtrNumber, string? S2Number, int? DailyPatientLimit, StaffEmbed? StaffAccounts);
    internal sealed record PatientEmbed(Guid? PatientId, string? FirstName, string? LastName, string? PatientCode, string? ContactNumber, string? Email,
        SexType? Sex, DateOnly? DateOfBirth);
    internal sealed record PaymentEmbed(PaymentStatus? Status, string? OrNumber);
    internal sealed record ServiceEmbed(string? Name);
    internal sealed record BookingServiceEmbed(ServiceEmbed? Services);
    internal sealed record BookingDto(Guid BookingId, Guid PatientId, Guid DoctorId, DateOnly AppointmentDate, TimeOnly? SlotStartTime, BookingStatus Status,
        string? QueueNumber, decimal? TotalFee, decimal? AmountDue, VisitType? VisitType, DateTimeOffset? CreatedAt,
        PatientEmbed? Patients, DoctorEmbed? Doctors, PaymentEmbed? Payments, List<BookingServiceEmbed>? BookingServices);

    internal sealed record DiagnosisDto(string? Icd10Code, string? CustomDescription);
    internal sealed record FollowUpDto(DateOnly? FollowUpDate, string? Instructions);
    internal sealed record ConsultBookingEmbed(DateOnly? AppointmentDate);
    internal sealed record ConsultationDto(Guid ConsultationId, Guid BookingId, Guid PatientId, Guid DoctorId, ConsultationStatus Status,
        string? ChiefComplaint, string? Subjective, string? Objective, string? Assessment, string? Plan, DateTimeOffset? CreatedAt,
        ConsultBookingEmbed? Bookings, DoctorEmbed? Doctors, List<DiagnosisDto>? ConsultationDiagnoses, FollowUpDto? FollowUps);

    /// <summary>prescription_line_items / prescription_template_items row.</summary>
    internal sealed record RxItemDto(Guid? Id, Guid? MedicineId, string? GenericName, string? Dosage, string? Quantity, string? Instruction,
        bool? IsControlledSubstance, string? Timing, string? MealRelation, string? DurationKind, int? DurationValue, string? Indication, string? Route, bool? AsNeeded);
    internal sealed record RxGroupDto(Guid GroupId, Guid PatientId, Guid DoctorId, Guid BookingId, DateTimeOffset CreatedAt, List<RxItemDto>? PrescriptionLineItems);

    internal static string PatientName(PatientEmbed? p) => p is null ? "" : $"{p.FirstName} {p.LastName}".Trim();

    internal static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string Id(Guid g) => g.ToString("D");

    internal static Dictionary<string, string?> Q(params (string Key, string? Value)[] pairs)
    {
        var d = new Dictionary<string, string?>();
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    internal static TplRxLine Line(RxItemDto i) => new(
        i.MedicineId ?? Guid.Empty, i.GenericName ?? "", i.Dosage ?? "", i.Quantity ?? "", i.Instruction, i.IsControlledSubstance ?? false,
        i.Route, i.MealRelation, i.Timing, i.DurationKind, i.DurationValue, i.Indication, i.AsNeeded);

    /// <summary>Line -> wire item (web toItem / rxItemsPayload: free-typed medicine = NIL GUID).</summary>
    internal static object Item(TplRxLine l) => new
    {
        medicine_id = l.MedicineId,
        generic_name = l.GenericName.Trim(),
        dosage = l.Dosage.Trim(),
        quantity = l.Quantity.Trim(),
        instruction = string.IsNullOrWhiteSpace(l.Instruction) ? null : l.Instruction.Trim(),
        is_controlled_substance = l.IsControlled,
        timing = l.Timing,
        meal_relation = l.MealRelation,
        duration_kind = l.DurationKind,
        duration_value = l.DurationValue,
        indication = string.IsNullOrWhiteSpace(l.Indication) ? null : l.Indication.Trim(),
        route = string.IsNullOrWhiteSpace(l.Route) ? null : l.Route,
        as_needed = l.AsNeeded,
    };

    /// <summary>Is this a real answer from the server (shown on screen, never retried locally)?</summary>
    internal static bool IsServerAnswer(Exception ex) =>
        ex is ApiValidationException or ApiForbiddenException or ApiNotFoundException or ApiConflictException or ApiServerException;

    /// <summary>Run a write; a server refusal becomes <c>DoctorResult.Fail(server message)</c> (the web shows err.body.message or its own fallback).</summary>
    internal static async Task<DoctorResult> Write(Func<Task<Guid?>> call, string fallback)
    {
        try
        {
            return DoctorResult.Ok(await call());
        }
        catch (Exception ex) when (IsServerAnswer(ex))
        {
            return DoctorResult.Fail(string.IsNullOrWhiteSpace(ex.Message) ? fallback : ex.Message);
        }
    }

    internal static Task<DoctorResult> Write(Func<Task> call, Guid? id, string fallback) => Write(async () => { await call(); return id; }, fallback);

    /// <summary>A nice-to-have read (the web wraps these in try/catch): a server refusal yields <paramref name="fallback"/>; offline still propagates.</summary>
    internal static async Task<T> Optional<T>(Func<Task<T>> call, T fallback)
    {
        try { return await call(); }
        catch (Exception ex) when (IsServerAnswer(ex)) { return fallback; }
    }
}
//JJQR - END
