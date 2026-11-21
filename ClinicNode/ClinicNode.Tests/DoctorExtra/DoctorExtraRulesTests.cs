//JJQR - AI (Nov 21, 2026) - doctor portal rules (appointments/model.ts, schedule, profile) — pure functions.
using ClinicApp.Domain.Enums;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.Tests.DoctorExtra;

public class DoctorExtraRulesTests
{
    static AppointmentRow Row(string name, string? queue, BookingStatus s, int day = 21, string? code = null) =>
        new(Guid.NewGuid(), name, code, new DateOnly(2026, 11, day), queue, s, null, "Consultation");

    [Fact]
    public void Order_puts_priority_lane_first_numeric_then_nulls_then_name()
    {
        var rows = new[] { Row("Zed", null, BookingStatus.Confirmed), Row("Ana", "R-10", BookingStatus.CheckedIn), Row("Ben", "R-2", BookingStatus.CheckedIn), Row("Cy", "S-3", BookingStatus.CheckedIn), Row("Abe", null, BookingStatus.Confirmed) };
        var ordered = DoctorExtraRules.Order(rows, AppointmentScope.Today).Select(r => r.PatientName).ToList();
        Assert.Equal(["Cy", "Ben", "Ana", "Abe", "Zed"], ordered);
    }

    [Fact]
    public void History_order_is_newest_date_first()
    {
        var rows = new[] { Row("Old", "R-1", BookingStatus.Completed, 1), Row("New", "R-9", BookingStatus.Completed, 20) };
        Assert.Equal("New", DoctorExtraRules.Order(rows, AppointmentScope.All)[0].PatientName);
    }

    [Fact]
    public void Filter_and_search_match_the_web_chips()
    {
        var rows = new[] { Row("Ana Reyes", "R-1", BookingStatus.CheckedIn, code: "MF-0001"), Row("Ben Cruz", "R-2", BookingStatus.InProgress), Row("Cy Lim", "R-3", BookingStatus.Completed), Row("Di Go", null, BookingStatus.Confirmed) };
        Assert.Equal(2, DoctorExtraRules.Filter(rows, AppointmentFilter.Active, "").Count);
        Assert.Single(DoctorExtraRules.Filter(rows, AppointmentFilter.Waiting, ""));
        Assert.Single(DoctorExtraRules.Filter(rows, AppointmentFilter.InProgress, ""));
        Assert.Single(DoctorExtraRules.Filter(rows, AppointmentFilter.Completed, ""));
        Assert.Equal(4, DoctorExtraRules.Filter(rows, AppointmentFilter.All, null).Count);
        Assert.Single(DoctorExtraRules.Filter(rows, AppointmentFilter.All, "mf-0001"));
        Assert.Single(DoctorExtraRules.Filter(rows, AppointmentFilter.All, " r-3 "));
        Assert.Equal("Ben Cruz", DoctorExtraRules.Focus(rows)!.PatientName);
        Assert.Equal("Ana Reyes", DoctorExtraRules.Focus(rows.Where(r => r.Status != BookingStatus.InProgress))!.PatientName);
        Assert.Null(DoctorExtraRules.Focus([Row("x", null, BookingStatus.Completed)]));
    }

    [Fact]
    public void Actions_states_and_labels()
    {
        Assert.Equal((true, "Resume consultation"), DoctorExtraRules.ActionFor(BookingStatus.InProgress));
        Assert.Equal((true, "Start consultation"), DoctorExtraRules.ActionFor(BookingStatus.CheckedIn));
        Assert.Equal((false, "View appointment"), DoctorExtraRules.ActionFor(BookingStatus.Completed));
        Assert.Equal(AppointmentState.Active, DoctorExtraRules.StateOf(BookingStatus.Confirmed));
        Assert.Equal(AppointmentState.Completed, DoctorExtraRules.StateOf(BookingStatus.Completed));
        Assert.Equal(AppointmentState.Closed, DoctorExtraRules.StateOf(BookingStatus.Rescheduled));
        Assert.Equal(AppointmentState.Other, DoctorExtraRules.StateOf(BookingStatus.Pending));
        Assert.Equal("—", DoctorExtraRules.ListPaymentLabel(BookingStatus.NoShow, PaymentStatus.Unpaid));
        Assert.Equal("Unpaid", DoctorExtraRules.ListPaymentLabel(BookingStatus.Completed, PaymentStatus.Unpaid));
        Assert.Equal("Not recorded", DoctorExtraRules.DetailPaymentLabel(BookingStatus.Confirmed, null));
        Assert.Equal("Follow-up", DoctorExtraRules.VisitTypeLabel(VisitType.FollowUp));
        Assert.Equal("Patient name unavailable", DoctorExtraRules.PatientName(" ", null));
        Assert.Equal("Nov 21, 2026", DoctorExtraRules.VisitDate(new DateOnly(2026, 11, 21)));
        Assert.Equal(("No appointments today", "Appointments will appear here when they are booked."), DoctorExtraRules.EmptyState(false, 0, AppointmentScope.Today));
        Assert.Equal("No matching appointments", DoctorExtraRules.EmptyState(true, 3, AppointmentScope.All).Title);
    }

    [Fact]
    public void Diagnosis_and_rx_summaries()
    {
        Assert.Equal("J06.9 — URTI", DoctorExtraRules.DiagnosisLabel(" J06.9 ", "URTI"));
        Assert.Equal("J06.9", DoctorExtraRules.DiagnosisLabel("J06.9", null));
        Assert.Equal("Fever", DoctorExtraRules.DiagnosisLabel(null, "Fever"));
        Assert.Equal("—", DoctorExtraRules.ShortRxSummary([]));
        Assert.Equal("A, B +1 more", DoctorExtraRules.ShortRxSummary(["A", "B", "C"]));
    }

    [Fact]
    public void Schedule_and_daily_limit_rules()
    {
        var ok = new ScheduleDay(1, true, new TimeOnly(8, 0), new TimeOnly(12, 0));
        var off = new ScheduleDay(0, false, default, default);
        Assert.Null(DoctorExtraRules.ScheduleError([off, ok]));
        Assert.Equal("Tuesday: end time must be after start time.", DoctorExtraRules.ScheduleError([ok, new ScheduleDay(2, true, new TimeOnly(9, 0), new TimeOnly(9, 0))]));
        Assert.Equal((true, (int?)null, (string?)null), DoctorExtraRules.ParseDailyLimit("  "));
        Assert.Equal(30, DoctorExtraRules.ParseDailyLimit("30").Value);
        Assert.Equal("Daily patient limit must be a non-negative whole number, or blank for no limit.", DoctorExtraRules.ParseDailyLimit("2.5").Error);
        Assert.Equal("Daily patient limit must be a non-negative whole number, or blank for no limit.", DoctorExtraRules.ParseDailyLimit("-1").Error);
        Assert.Equal("Daily patient limit must be between 1 and 500.", DoctorExtraRules.ParseDailyLimit("0").Error);
        Assert.StartsWith("1 patient booked on this date", DoctorExtraRules.AffectedBookingsText(1));
        Assert.StartsWith("2 patients booked", DoctorExtraRules.AffectedBookingsText(2));
    }

    [Fact]
    public void Profile_validation_follows_web_then_backend_rules()
    {
        DoctorProfileEdit E(string? name = "Dr. Ana", string? spec = "Family Medicine", string? fee = "500", string? lic = null) => new(name, spec, null, fee, lic, null, null);
        Assert.True(DoctorExtraRules.ValidateProfile(E()).Ok);
        Assert.Equal(500m, DoctorExtraRules.ValidateProfile(E()).Fee);
        const string web = "Enter your full name and a valid non-negative consultation fee.";
        Assert.Equal(web, DoctorExtraRules.ValidateProfile(E(name: " ")).Error);
        Assert.Equal(web, DoctorExtraRules.ValidateProfile(E(fee: "-1")).Error);
        Assert.Equal(web, DoctorExtraRules.ValidateProfile(E(fee: "abc")).Error);
        Assert.Equal("Specialty is required.", DoctorExtraRules.ValidateProfile(E(spec: "")).Error);
        Assert.Equal("Consultation fee is too large (max 1,000,000).", DoctorExtraRules.ValidateProfile(E(fee: "2000000")).Error);
        Assert.Equal("License number is too long (max 50 characters).", DoctorExtraRules.ValidateProfile(E(lic: new string('x', 51))).Error);
        Assert.False(DoctorExtraRules.CanUpdatePassword("old", "short", "short"));
        Assert.True(DoctorExtraRules.CanUpdatePassword("old", "violet harbor lantern", "violet harbor lantern"));
    }
}
//JJQR - END
