//JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Staff;

namespace ClinicNode.Tests.Staff;

public class StaffRulesTests
{
    [Theory]
    [InlineData("", "Enter the amount received.")]
    [InlineData("abc", "Amount received must be a number, like 450 or 450.50.")]
    [InlineData("450.555", "Amount received must be a number, like 450 or 450.50.")]
    [InlineData("0", "Amount received must be more than ₱0.")]
    [InlineData("-5", "Amount received must be more than ₱0.")]
    [InlineData("449.99", "Amount must be exactly ₱450 — the amount the doctor set.")]
    [InlineData("500", "Amount must be exactly ₱450 — the amount the doctor set.")]
    public void Amount_received_must_be_exactly_the_fee(string raw, string error)
    {
        var r = StaffRules.CheckAmountReceived(raw, 450m);
        Assert.False(r.Ok);
        Assert.Equal(error, r.Error);
    }

    [Theory]
    [InlineData("450")]
    [InlineData("₱450.00")]
    [InlineData(" 450 ")]
    public void Exact_amount_passes(string raw)
    {
        var r = StaffRules.CheckAmountReceived(raw, 450m);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(450m, r.Value);
    }

    [Fact]
    public void Amount_with_thousands_separator_is_accepted() =>
        Assert.True(StaffRules.CheckAmountReceived("₱1,200", 1200m).Ok);

    [Fact]
    public void Or_number_legacy_fallback_is_derived_from_booking_id() =>
        Assert.Equal("OR-1A2B3C4D", StaffRules.OrNumberFor(Guid.Parse("1a2b3c4d-0000-0000-0000-000000000000")));

    [Fact]
    public void Or_sequence_format_uses_node_prefix_manila_date_and_padded_seq()
    {
        var nodeId = Guid.Parse("aa4f8351-410c-4b5d-842f-e684de014276");
        Assert.Equal("AA4F", StaffRules.NodePrefixFrom(nodeId));
        var date = new DateOnly(2026, 11, 21);
        Assert.Equal("20261121", StaffRules.OrDateStamp(date));
        Assert.Equal("OR-AA4F-20261121-", StaffRules.OrDayPrefix("AA4F", date));
        Assert.Equal("OR-AA4F-20261121-0001", StaffRules.FormatOrNumber("AA4F", date, 1));
        Assert.Equal("OR-AA4F-20261121-0012", StaffRules.FormatOrNumber("AA4F", date, 12));
        Assert.Equal("OR-AA4F-20261121-10000", StaffRules.FormatOrNumber("AA4F", date, 10_000)); // width grows past 4 digits
        Assert.Equal(1, StaffRules.SeqOf("OR-AA4F-20261121-0001", "OR-AA4F-20261121-"));
        Assert.Null(StaffRules.SeqOf("OR-AA4F-20261122-0001", "OR-AA4F-20261121-")); // other day
        Assert.Null(StaffRules.SeqOf("OR-1A2B3C4D", "OR-AA4F-20261121-")); // legacy
        Assert.Null(StaffRules.SeqOf("OR-AA4F-20261121-0000", "OR-AA4F-20261121-"));
    }

    [Fact]
    public void Display_or_number_uses_stored_then_legacy_only_when_settled()
    {
        var id = Guid.Parse("1a2b3c4d-0000-0000-0000-000000000000");
        Assert.Equal("OR-AA4F-20261121-0001", StaffRules.DisplayOrNumber("OR-AA4F-20261121-0001", id, settled: true));
        Assert.Equal("OR-1A2B3C4D", StaffRules.DisplayOrNumber(null, id, settled: true));
        Assert.Equal("OR-1A2B3C4D", StaffRules.DisplayOrNumber("  ", id, settled: true));
        Assert.Null(StaffRules.DisplayOrNumber(null, id, settled: false));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("0917 123 4567", null)]
    [InlineData("+63 (2) 8123-4567", null)]
    [InlineData("12345", "Contact number must be a phone number (digits, spaces, + - ( ) only).")]
    [InlineData("call me", "Contact number must be a phone number (digits, spaces, + - ( ) only).")]
    public void Phone_rule_matches_the_api(string value, string? error) => Assert.Equal(error, StaffRules.Phone(value));

    [Fact]
    public void Senior_is_sixty_or_older_on_the_day()
    {
        var today = new DateOnly(2026, 11, 21);
        Assert.True(StaffRules.IsSeniorCitizen(new DateOnly(1966, 11, 21), today));
        Assert.False(StaffRules.IsSeniorCitizen(new DateOnly(1966, 11, 22), today));
        Assert.Equal("Senior", StaffRules.DefaultDiscountFor(new DateOnly(1950, 1, 1), today));
        Assert.Null(StaffRules.DefaultDiscountFor(new DateOnly(2000, 1, 1), today));
    }

    [Fact]
    public void Dashboard_cancel_only_for_confirmed_and_unpaid()
    {
        Assert.True(StaffRules.DashboardCanCancel(BookingStatus.Confirmed, PaymentStatus.Unpaid));
        Assert.False(StaffRules.DashboardCanCancel(BookingStatus.Confirmed, PaymentStatus.Paid));
        Assert.False(StaffRules.DashboardCanCancel(BookingStatus.CheckedIn, PaymentStatus.Unpaid));
        Assert.Equal("Check in", StaffRules.CheckInLabel(BookingStatus.Confirmed));
        Assert.Equal("Undo check-in", StaffRules.CheckInLabel(BookingStatus.CheckedIn));
    }

    [Fact]
    public void Queue_actions_follow_the_web_board()
    {
        var pending = StaffRules.QueueActionsFor(BookingStatus.Pending);
        Assert.True(pending.CheckIn && pending.NoShow);
        Assert.False(pending.CallIn || pending.Cancel || pending.Hold || pending.Complete);
        //JJQR - AI (Nov 21, 2026) - booked-not-arrived may surface as Confirmed on the node board.
        var booked = StaffRules.QueueActionsFor(BookingStatus.Confirmed);
        Assert.True(booked.CheckIn && booked.NoShow);
        Assert.False(booked.Complete || booked.OpenConsult);
        Assert.False(StaffRules.QueueActionsFor(BookingStatus.OnHold).Complete);
        //JJQR - END

        var waiting = StaffRules.QueueActionsFor(BookingStatus.CheckedIn);
        Assert.True(waiting.CallIn && waiting.Hold && waiting.NoShow && waiting.Cancel);
        Assert.False(waiting.Complete);

        var inRoom = StaffRules.QueueActionsFor(BookingStatus.InProgress);
        Assert.True(inRoom.Complete && inRoom.OpenConsult && inRoom.Hold && inRoom.NoShow);
        Assert.False(inRoom.Cancel || inRoom.CallIn);

        var done = StaffRules.QueueActionsFor(BookingStatus.Completed);
        Assert.False(done.CheckIn || done.NoShow || done.CallIn || done.Complete || done.Hold || done.Cancel);
    }

    [Fact]
    public void Booking_detail_actions_follow_the_web()
    {
        var confirmed = StaffRules.BookingActionsFor(BookingStatus.Confirmed, PaymentStatus.Unpaid);
        Assert.True(confirmed.CheckIn && confirmed.Reschedule && confirmed.MarkNoShow && confirmed.Cancel);
        var owing = StaffRules.BookingActionsFor(BookingStatus.Completed, PaymentStatus.Unpaid);
        Assert.True(owing.RecordPayment && owing.Waive);
        Assert.False(owing.ViewReceipt || owing.Cancel);
        var paid = StaffRules.BookingActionsFor(BookingStatus.Completed, PaymentStatus.Paid);
        Assert.True(paid.ViewReceipt && paid.Settled);
        Assert.False(paid.RecordPayment);
        Assert.False(StaffRules.BookingActionsFor(BookingStatus.Cancelled, PaymentStatus.Unpaid).HasAny);
    }

    [Fact]
    public void Peso_matches_web_format()
    {
        Assert.Equal("₱450", StaffRules.Peso(450m));
        Assert.Equal("₱1,200.5", StaffRules.Peso(1200.5m));
    }
}
//JJQR - END
