//JJQR - AI (Nov 21, 2026) - pure front-desk rules ported 1:1 from Project02-fe (payment-amount.ts, validation.ts, staff pages) and Project02-be (Rules.cs / Check.cs, PasswordPolicy).
using System.Globalization;
using System.Text.RegularExpressions;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;

namespace ClinicNode.Core.Staff;

/// <summary>Result of checking what the cashier typed in "Amount received (₱)".</summary>
public sealed record AmountCheck(bool Ok, decimal Value, string? Error)
{
    public static AmountCheck Fail(string error) => new(false, 0, error);
    public static AmountCheck Pass(decimal value) => new(true, value, null);
}

public static partial class StaffRules
{
    /// <summary>Web MAX_AMOUNT_RECEIVED.</summary>
    public const decimal MaxAmountReceived = 100_000m;

    /// <summary>Min reason length on the booking-detail Cancel / Waive dialogs ("At least 5 characters.").</summary>
    public const int MinDialogReasonLength = 5;

    /// <summary>Port of <c>checkAmountReceived</c>: blank, not a number, ≤ 0, or anything other than the exact amount due is an error.</summary>
    public static AmountCheck CheckAmountReceived(string? raw, decimal amountDue)
    {
        var text = Regex.Replace(raw ?? "", @"[₱,\s]", "");
        if (text.Length == 0) return AmountCheck.Fail("Enter the amount received.");
        if (!AmountRx().IsMatch(text) || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return AmountCheck.Fail("Amount received must be a number, like 450 or 450.50.");
        if (value <= 0) return AmountCheck.Fail("Amount received must be more than ₱0.");
        if (value != amountDue) return AmountCheck.Fail($"Amount must be exactly {Peso(amountDue)} — the amount the doctor set.");
        return AmountCheck.Pass(value);
    }

    /// <summary>Legacy web <c>newOrNumber</c>: <c>OR-</c> + first 8 hex of the booking id, upper-case.
    /// Kept only as a display fallback for old payment rows that have no stored OR number.
    /// New receipts use <see cref="FormatOrNumber"/> (<c>OR-&lt;nodePrefix&gt;-&lt;yyyymmdd&gt;-&lt;seq&gt;</c>).</summary>
    public static string OrNumberFor(Guid bookingId) => "OR-" + bookingId.ToString("D")[..8].ToUpperInvariant();

    /// <summary>How many uppercase hex characters of <c>node_identity.node_id</c> go into the OR prefix.</summary>
    public const int OrNodePrefixLength = 4;

    /// <summary>Minimum zero-padded width of the daily sequence (values above 9999 just use more digits).</summary>
    public const int OrSequenceDigits = 4;

    /// <summary>Short stable node prefix: first 4 characters of the node id as uppercase hex without dashes
    /// (<c>Guid.ToString("N")</c>). The id lives in <c>node_identity.node_id</c> (created once by
    /// <c>SyncOutbox.GetOrCreateIdentityAsync</c>) and survives restarts. Not derived from node-secret.key.</summary>
    public static string NodePrefixFrom(Guid nodeId) => nodeId.ToString("N").ToUpperInvariant()[..OrNodePrefixLength];

    /// <summary>Manila calendar date as <c>yyyyMMdd</c>.</summary>
    public static string OrDateStamp(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Everything before the sequence: <c>OR-ABCD-20261121-</c>.</summary>
    public static string OrDayPrefix(string nodePrefix, DateOnly date) => $"OR-{nodePrefix}-{OrDateStamp(date)}-";

    /// <summary>Full official receipt number: <c>OR-&lt;nodePrefix&gt;-&lt;yyyymmdd&gt;-&lt;seq&gt;</c>, seq zero-padded to 4 digits, resetting daily.</summary>
    public static string FormatOrNumber(string nodePrefix, DateOnly date, int seq)
    {
        if (seq < 1) throw new ArgumentOutOfRangeException(nameof(seq));
        return OrDayPrefix(nodePrefix, date) + seq.ToString($"D{OrSequenceDigits}", CultureInfo.InvariantCulture);
    }

    /// <summary>The daily sequence encoded in <paramref name="orNumber"/> when it belongs to this day's prefix; otherwise null.</summary>
    public static int? SeqOf(string? orNumber, string dayPrefix)
    {
        if (string.IsNullOrWhiteSpace(orNumber) || string.IsNullOrEmpty(dayPrefix)) return null;
        if (!orNumber.StartsWith(dayPrefix, StringComparison.Ordinal)) return null;
        var rest = orNumber[dayPrefix.Length..];
        if (rest.Length == 0 || !int.TryParse(rest, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1) return null;
        return n;
    }

    /// <summary>Stored number if present; otherwise the legacy booking-id form, but only once the visit is settled (paid or waived).</summary>
    public static string? DisplayOrNumber(string? stored, Guid bookingId, bool settled) =>
        !string.IsNullOrWhiteSpace(stored) ? stored : settled ? OrNumberFor(bookingId) : null;

    /// <summary>Web <c>formatPeso</c> (en-PH, up to 2 decimals).</summary>
    public static string Peso(decimal amount) =>
        "₱" + amount.ToString("#,0.##", CultureInfo.InvariantCulture);

    // ── field rules (Project02-be Check.cs; Project02-fe validation.ts) ─────────────
    public static string? Required(string? value, int max, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) return $"{label} is required.";
        return value.Trim().Length > max ? $"{label} is too long (max {max} characters)." : null;
    }

    public static string? Length(string? value, int max, string label) =>
        (value ?? "").Trim().Length > max ? $"{label} is too long (max {max} characters)." : null;

    /// <summary>Digits with + ( ) - . and spaces; 7–20 characters, at least 7 digits. Empty is fine.</summary>
    public static string? Phone(string? value, string label = "Contact number")
    {
        var v = (value ?? "").Trim();
        if (v.Length == 0) return null;
        return !PhoneRx().IsMatch(v) || v.Count(char.IsDigit) < 7
            ? $"{label} must be a phone number (digits, spaces, + - ( ) only)."
            : null;
    }

    public static string? NotFuture(DateOnly? value, DateOnly today, string label = "Date of birth") =>
        value is { } d && d > today ? $"{label} cannot be in the future." : null;

    public static string? NotBefore(DateOnly? value, string label = "Date of birth", int minYear = 1900) =>
        value is { } d && d.Year < minYear ? $"{label} is not a valid date." : null;

    // ── walk-in / senior (RA 9994) ────────────────────────────────────────────
    public static int AgeOn(DateOnly dateOfBirth, DateOnly today)
    {
        var age = today.Year - dateOfBirth.Year;
        if (today.Month < dateOfBirth.Month || (today.Month == dateOfBirth.Month && today.Day < dateOfBirth.Day)) age--;
        return age;
    }

    public static bool IsSeniorCitizen(DateOnly dateOfBirth, DateOnly today) => AgeOn(dateOfBirth, today) >= 60;

    /// <summary>Wizard auto-tag: "Senior" when the picked patient is 60+, otherwise none.</summary>
    public static string? DefaultDiscountFor(DateOnly? dateOfBirth, DateOnly today) =>
        dateOfBirth is { } d && IsSeniorCitizen(d, today) ? "Senior" : null;

    public static string VisitTypeLabel(VisitType type) => type == VisitType.FollowUp ? "Follow-up" : "New";
    public static string VisitTypeLongLabel(VisitType type) => type == VisitType.FollowUp ? "Follow-up" : "New visit";

    public static string PaymentMethodLabel(PaymentMethod? method) => method switch
    {
        PaymentMethod.BankTransfer => "Bank transfer",
        null => "—",
        _ => method.Value.ToString(),
    };

    /// <summary>Web <c>accountStatus(userId, isGuest)</c>.</summary>
    public static string AccountStatus(Guid? userId, bool isGuest) =>
        userId is not null ? "LinkedAccount" : isGuest ? "NoAccount" : "AccountUnknown";

    // ── dashboard (staff/dashboard/page.tsx) ─────────────────────────────────
    /// <summary>Arrivals list = Confirmed or CheckedIn.</summary>
    public static bool IsArrival(BookingStatus status) => status is BookingStatus.Confirmed or BookingStatus.CheckedIn;

    /// <summary>Cancel only for a booking that has NOT been checked in and is not already paid.</summary>
    public static bool DashboardCanCancel(BookingStatus status, PaymentStatus paymentStatus) =>
        status == BookingStatus.Confirmed && paymentStatus != PaymentStatus.Paid;

    public static string CheckInLabel(BookingStatus status) => status == BookingStatus.Confirmed ? "Check in" : "Undo check-in";

    // ── queue board actions (staff/queue/page.tsx renderActions) ─────────────
    public static QueueActions QueueActionsFor(BookingStatus s) => new(
        // Web queue "Pending" = booked, not arrived; the node board can surface that as Pending or Confirmed.
        CheckIn: s is BookingStatus.Pending or BookingStatus.Confirmed,
        NoShow: s is BookingStatus.Pending or BookingStatus.Confirmed or BookingStatus.CheckedIn or BookingStatus.OnHold or BookingStatus.InProgress,
        CallIn: s is BookingStatus.CheckedIn or BookingStatus.OnHold,
        OpenConsult: s == BookingStatus.InProgress,
        Complete: s == BookingStatus.InProgress,
        Hold: s is BookingStatus.CheckedIn or BookingStatus.OnHold or BookingStatus.InProgress,
        Cancel: s is BookingStatus.CheckedIn or BookingStatus.OnHold);

    // ── booking detail (staff/bookings/[id]/page.tsx) ─────────────────────────
    public static BookingDetailActions BookingActionsFor(BookingStatus status, PaymentStatus payment)
    {
        var confirmed = status == BookingStatus.Confirmed;
        var checkedIn = status == BookingStatus.CheckedIn;
        var completed = status == BookingStatus.Completed;
        var settled = payment is PaymentStatus.Paid or PaymentStatus.Waived;
        return new BookingDetailActions(
            CheckIn: confirmed,
            Reschedule: confirmed,
            MarkNoShow: confirmed,
            UndoCheckIn: checkedIn,
            RecordPayment: completed && payment == PaymentStatus.Unpaid,
            Waive: completed && payment == PaymentStatus.Unpaid,
            ViewReceipt: completed && settled,
            Cancel: confirmed || checkedIn,
            Settled: settled);
    }

    [GeneratedRegex(@"^-?\d+(\.\d{1,2})?$")]
    private static partial Regex AmountRx();

    [GeneratedRegex(@"^[0-9+()\-.\s]{7,20}$")]
    private static partial Regex PhoneRx();
}

public sealed record QueueActions(bool CheckIn, bool NoShow, bool CallIn, bool OpenConsult, bool Complete, bool Hold, bool Cancel);

public sealed record BookingDetailActions(bool CheckIn, bool Reschedule, bool MarkNoShow, bool UndoCheckIn, bool RecordPayment, bool Waive, bool ViewReceipt, bool Cancel, bool Settled)
{
    public bool HasAny => CheckIn || UndoCheckIn || RecordPayment || ViewReceipt || Cancel;
}

/// <summary>Front-desk result: Ok, or a user-facing error sentence.</summary>
public sealed record StaffResult(bool Succeeded, string? Error = null, Guid? Id = null)
{
    public static StaffResult Ok(Guid? id = null) => new(true, null, id);
    public static StaffResult Fail(string error) => new(false, error);
}
//JJQR - END
