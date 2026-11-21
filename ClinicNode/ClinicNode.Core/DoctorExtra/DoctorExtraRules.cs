//JJQR - AI (Nov 21, 2026) - doctor portal rules ported from Project02-fe (appointments/model.ts, schedule, profile) and Project02-be (Rules.Doctor / Rules.Staff, Check.cs).
using System.Globalization;
using ClinicApp.Auth;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;

namespace ClinicNode.Core.DoctorExtra;

public enum AppointmentScope { Today, All }
public enum AppointmentFilter { All, Active, Waiting, InProgress, Completed }

/// <summary>Web <c>Appointment</c> row (appointments/model.ts mapAppointments).</summary>
public sealed record AppointmentRow(
    Guid BookingId, string PatientName, string? PatientCode, DateOnly AppointmentDate,
    string? QueueNumber, BookingStatus Status, string? PaymentStatus, string VisitType);

/// <summary>Booking detail state (appointments/[id]/page.tsx isActive / isCompleted / isClosed).</summary>
public enum AppointmentState { Active, Completed, Closed, Other }

public sealed record DoctorResult(bool Succeeded, string? Error = null, Guid? Id = null)
{
    public static DoctorResult Ok(Guid? id = null) => new(true, null, id);
    public static DoctorResult Fail(string error) => new(false, error);
}

public static class DoctorExtraRules
{
    public const int AppointmentPageSize = 20;
    public const int PatientPageSize = 20;

    public static readonly (AppointmentFilter Id, string Label)[] Filters =
    [
        (AppointmentFilter.Active, "In clinic"), (AppointmentFilter.Waiting, "Waiting"), (AppointmentFilter.InProgress, "In progress"),
        (AppointmentFilter.Completed, "Completed"), (AppointmentFilter.All, "All statuses"),
    ];

    /// <summary>Web isVoidBooking: a cancelled / no-show / expired visit owes nothing — "—", never "Unpaid".</summary>
    public static bool IsVoidBooking(BookingStatus s) => s is BookingStatus.Cancelled or BookingStatus.NoShow or BookingStatus.Expired;

    public static string? ListPaymentLabel(BookingStatus s, PaymentStatus? p) => IsVoidBooking(s) ? "—" : p?.ToString();

    /// <summary>Detail page: "Not recorded" when there is no payment row.</summary>
    public static string DetailPaymentLabel(BookingStatus s, PaymentStatus? p) => IsVoidBooking(s) ? "—" : p?.ToString() ?? "Not recorded";

    public static string VisitTypeLabel(VisitType v) => v == VisitType.FollowUp ? "Follow-up" : "Consultation";

    public static string PatientName(string? first, string? last)
    {
        var n = string.Join(" ", new[] { first, last }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        return n.Length > 0 ? n : "Patient name unavailable";
    }

    static int Lane(string q) => q.StartsWith("S-", StringComparison.Ordinal) ? 0 : 1;

    /// <summary>Lane (S- before R-) then numeric-aware compare, like Intl.Collator numeric.</summary>
    public static int CompareQueueNumbers(string a, string b)
    {
        var lane = Lane(a) - Lane(b);
        if (lane != 0) return lane;
        return NaturalCompare(a, b);
    }

    static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = long.Parse(a[si..i], CultureInfo.InvariantCulture);
                var nb = long.Parse(b[sj..j], CultureInfo.InvariantCulture);
                if (na != nb) return na.CompareTo(nb);
            }
            else
            {
                var c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>Web orderedAppointments: History newest date first; then queue number (nulls last); then name.</summary>
    public static IReadOnlyList<AppointmentRow> Order(IEnumerable<AppointmentRow> rows, AppointmentScope scope)
    {
        var list = rows.ToList();
        list.Sort((a, b) =>
        {
            if (scope == AppointmentScope.All)
            {
                var d = b.AppointmentDate.CompareTo(a.AppointmentDate);
                if (d != 0) return d;
            }
            int q;
            if (a.QueueNumber == b.QueueNumber) q = 0;
            else if (a.QueueNumber is null) q = 1;
            else if (b.QueueNumber is null) q = -1;
            else q = CompareQueueNumbers(a.QueueNumber, b.QueueNumber);
            if (q != 0) return q;
            return string.Compare(a.PatientName, b.PatientName, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    /// <summary>Web filterAppointments: status chips + search over name, code, queue number.</summary>
    public static IReadOnlyList<AppointmentRow> Filter(IEnumerable<AppointmentRow> rows, AppointmentFilter filter, string? search)
    {
        BookingStatus[]? statuses = filter switch
        {
            AppointmentFilter.Active => [BookingStatus.CheckedIn, BookingStatus.InProgress],
            AppointmentFilter.Waiting => [BookingStatus.CheckedIn],
            AppointmentFilter.InProgress => [BookingStatus.InProgress],
            AppointmentFilter.Completed => [BookingStatus.Completed],
            _ => null,
        };
        var q = (search ?? "").Trim().ToLowerInvariant();
        return rows.Where(r => (statuses is null || statuses.Contains(r.Status))
            && $"{r.PatientName} {r.PatientCode ?? ""} {r.QueueNumber ?? ""}".ToLowerInvariant().Contains(q)).ToList();
    }

    /// <summary>Web focusAppointment: the visit in progress, else the next one waiting.</summary>
    public static AppointmentRow? Focus(IEnumerable<AppointmentRow> rows)
    {
        var list = rows.ToList();
        return list.FirstOrDefault(r => r.Status == BookingStatus.InProgress) ?? list.FirstOrDefault(r => r.Status == BookingStatus.CheckedIn);
    }

    /// <summary>Web appointmentAction: clinical rows open the consultation, others the appointment.</summary>
    public static (bool OpensConsult, string Label) ActionFor(BookingStatus s)
    {
        var clinical = s is BookingStatus.CheckedIn or BookingStatus.InProgress;
        return (clinical, s == BookingStatus.InProgress ? "Resume consultation" : clinical ? "Start consultation" : "View appointment");
    }

    /// <summary>Empty-state heading/body (appointments page).</summary>
    public static (string Title, string Body) EmptyState(bool hasSearch, int total, AppointmentScope scope) =>
        hasSearch ? ("No matching appointments", "Try a different patient name or queue number.")
        : total > 0 ? ("No appointments in this view", "Choose another status to see the rest of your appointments.")
        : (scope == AppointmentScope.Today ? "No appointments today" : "No appointments yet", "Appointments will appear here when they are booked.");

    public static AppointmentState StateOf(BookingStatus s) => s switch
    {
        BookingStatus.Confirmed or BookingStatus.CheckedIn or BookingStatus.InProgress => AppointmentState.Active,
        BookingStatus.Completed => AppointmentState.Completed,
        BookingStatus.Cancelled or BookingStatus.NoShow or BookingStatus.Rescheduled => AppointmentState.Closed,
        _ => AppointmentState.Other,
    };

    /// <summary>Web formatConsultationDiagnosisLabel: "CODE — desc", code, or desc.</summary>
    public static string DiagnosisLabel(string? code, string? desc)
    {
        var c = code?.Trim();
        var d = desc?.Trim();
        if (!string.IsNullOrEmpty(c) && !string.IsNullOrEmpty(d)) return $"{c} — {d}";
        if (!string.IsNullOrEmpty(c)) return c;
        return d ?? "";
    }

    /// <summary>Web shortRxSummaryForItems: first two generic names, then "+N more"; "—" when none.</summary>
    public static string ShortRxSummary(IReadOnlyList<string> genericNames)
    {
        if (genericNames.Count == 0) return "—";
        var suffix = genericNames.Count > 2 ? $" +{genericNames.Count - 2} more" : "";
        return string.Join(", ", genericNames.Take(2)) + suffix;
    }

    /// <summary>Web visitDate: "Nov 21, 2026".</summary>
    public static string VisitDate(DateOnly d) => d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

    public static int AgeOn(DateOnly dob, DateOnly today)
    {
        var age = today.Year - dob.Year;
        if (today < dob.AddYears(age)) age--;
        return Math.Max(0, age);
    }

    // ── schedule (doctor/schedule/page.tsx) ────────────────────────────
    public static readonly string[] DayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    /// <summary>"{Day}: end time must be after start time." for the first active day whose end is not after its start.</summary>
    public static string? ScheduleError(IEnumerable<ScheduleDay> days)
    {
        var bad = days.FirstOrDefault(d => d.IsActive && !(d.EndTime > d.StartTime));
        return bad is null ? null : $"{DayNames[bad.DayOfWeek]}: end time must be after start time.";
    }

    /// <summary>Web: blank = no limit; otherwise a non-negative whole number. Backend Rules.Doctor then requires 1–500.</summary>
    public static (bool Ok, int? Value, string? Error) ParseDailyLimit(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) return (true, null, null);
        if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0)
            return (false, null, "Daily patient limit must be a non-negative whole number, or blank for no limit.");
        if (n < 1 || n > 500) return (false, null, "Daily patient limit must be between 1 and 500.");
        return (true, n, null);
    }

    public static string AffectedBookingsText(int count) =>
        $"{count} patient{(count == 1 ? "" : "s")} booked on this date (excluding cancelled). Blocking will not automatically notify or reschedule them — Staff will need to follow up.";

    // ── profile (doctor/profile/page.tsx + Rules.Doctor / Rules.Staff) ──
    public static (bool Ok, decimal Fee, string? Error) ValidateProfile(DoctorProfileEdit e)
    {
        if (string.IsNullOrWhiteSpace(e.FullName)
            || !decimal.TryParse((e.ConsultationFee ?? "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var fee) || fee < 0)
            return (false, 0, "Enter your full name and a valid non-negative consultation fee.");
        string? Req(string? v, int max, string label) =>
            string.IsNullOrWhiteSpace(v) ? $"{label} is required." : v.Trim().Length > max ? $"{label} is too long (max {max} characters)." : null;
        string? Opt(string? v, int max, string label) =>
            !string.IsNullOrEmpty(v) && v.Trim().Length > max ? $"{label} is too long (max {max} characters)." : null;
        var err = Req(e.FullName, 200, "Full name")
            ?? Req(e.Specialization, 100, "Specialty")
            ?? (fee > 1_000_000m ? "Consultation fee is too large (max 1,000,000)." : null)
            ?? Opt(e.Bio, 2000, "Bio")
            ?? Opt(e.LicenseNumber, 50, "License number")
            ?? Opt(e.PtrNumber, 50, "PTR number")
            ?? Opt(e.S2Number, 50, "S2 number");
        return err is null ? (true, fee, null) : (false, 0, err);
    }

    /// <summary>Web canUpdate: current not blank, new ≥ min length, new == confirm.</summary>
    public static bool CanUpdatePassword(string? current, string? newPassword, string? confirm) =>
        !string.IsNullOrWhiteSpace(current) && (newPassword ?? "").Length >= PasswordPolicy.MinLength && newPassword == confirm;
}

public sealed record ScheduleDay(short DayOfWeek, bool IsActive, TimeOnly StartTime, TimeOnly EndTime);

public sealed record DoctorProfileEdit(string? FullName, string? Specialization, string? Bio, string? ConsultationFee, string? LicenseNumber, string? PtrNumber, string? S2Number);
//JJQR - END
