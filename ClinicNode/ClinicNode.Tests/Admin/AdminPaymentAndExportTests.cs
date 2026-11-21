// JJQR - AI (Nov 21, 2026) - admin payment actions and clinic-reports CSV parity with the web portal.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Admin;
using ClinicNode.Core.Patients;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Admin;

public class AdminPaymentAndExportTests
{
    [Fact]
    public async Task Confirm_payment_marks_proof_submitted_booking_paid_with_or_number()
    {
        await using var host = await AdminTestHost.StartAsync();
        var (bookings, bookingId, _) = await OpenBookingAsync(host, "Nia", "Perez");

        var pending = await bookings.ConfirmPaymentAsync(bookingId);
        Assert.False(pending.Succeeded);
        Assert.Contains("proof submitted", pending.Error, StringComparison.OrdinalIgnoreCase);

        var proof = await bookings.UpdateStatusAsync(bookingId, BookingStatus.ProofSubmitted, null);
        Assert.True(proof.Succeeded, proof.Error);

        var confirmed = await bookings.ConfirmPaymentAsync(bookingId);
        Assert.True(confirmed.Succeeded, confirmed.Error);

        var detail = await bookings.GetAsync(bookingId);
        Assert.True(detail.Succeeded, detail.Error);
        Assert.Equal(BookingStatus.Confirmed, detail.Value!.Booking.Status);
        Assert.Equal(PaymentStatus.Paid, detail.Value.Booking.PaymentStatus);
        Assert.Equal(0m, detail.Value.AmountDue);
        // JJQR - AI (Nov 21, 2026) - sequential OR, not a booking-id prefix
        Assert.False(string.IsNullOrWhiteSpace(detail.Value.OrNumber));
        Assert.StartsWith("OR-", detail.Value.OrNumber);
        Assert.DoesNotContain(bookingId.ToString("D")[..8].ToUpperInvariant(), detail.Value.OrNumber, StringComparison.Ordinal);
        Assert.NotNull(detail.Value.PaymentId);
    }

    [Fact]
    public async Task Confirm_payment_allocates_unique_sequential_or_numbers()
    {
        await using var host = await AdminTestHost.StartAsync();
        var (bookings, firstId, _) = await OpenBookingAsync(host, "Nia", "Perez");
        var (_, secondId, _) = await OpenBookingAsync(host, "Lia", "Go");

        Assert.True((await bookings.UpdateStatusAsync(firstId, BookingStatus.ProofSubmitted, null)).Succeeded);
        Assert.True((await bookings.UpdateStatusAsync(secondId, BookingStatus.ProofSubmitted, null)).Succeeded);
        Assert.True((await bookings.ConfirmPaymentAsync(firstId)).Succeeded);
        Assert.True((await bookings.ConfirmPaymentAsync(secondId)).Succeeded);

        var a = (await bookings.GetAsync(firstId)).Value!.OrNumber!;
        var b = (await bookings.GetAsync(secondId)).Value!.OrNumber!;
        Assert.NotEqual(a, b);
        Assert.StartsWith("OR-", a);
        Assert.StartsWith("OR-", b);
        static int Seq(string or)
        {
            var i = or.LastIndexOf('-');
            Assert.True(i >= 0, "OR should end in a sequence: " + or);
            Assert.True(int.TryParse(or[(i + 1)..], out var n), "OR should end in a sequence: " + or);
            return n;
        }
        Assert.Equal(Seq(a) + 1, Seq(b));
    }

    [Fact]
    public async Task Waive_requires_reason_and_zeroes_amount_due()
    {
        await using var host = await AdminTestHost.StartAsync();
        var (bookings, bookingId, _) = await OpenBookingAsync(host, "Ivy", "Santos");

        var missing = await bookings.WaivePaymentAsync(bookingId, "  ");
        Assert.False(missing.Succeeded);
        Assert.Contains("reason is required", missing.Error, StringComparison.OrdinalIgnoreCase);

        var waived = await bookings.WaivePaymentAsync(bookingId, "Senior courtesy waiver");
        Assert.True(waived.Succeeded, waived.Error);

        var detail = await bookings.GetAsync(bookingId);
        Assert.Equal(PaymentStatus.Waived, detail.Value!.Booking.PaymentStatus);
        Assert.Equal(0m, detail.Value.AmountDue);
        // JJQR - AI (Nov 21, 2026) - waivers do not issue an official receipt (web / staff parity)
        Assert.True(string.IsNullOrWhiteSpace(detail.Value.OrNumber));

        var again = await bookings.WaivePaymentAsync(bookingId, "already waived");
        Assert.False(again.Succeeded);
    }

    [Fact]
    public async Task Refund_rejects_bad_amounts_then_marks_paid_booking_refunded()
    {
        await using var host = await AdminTestHost.StartAsync();
        var (bookings, bookingId, _) = await OpenBookingAsync(host, "Rico", "Tan");

        var unpaid = await bookings.RefundPaymentAsync(bookingId, 10m, "oops");
        Assert.False(unpaid.Succeeded);

        var proof = await bookings.UpdateStatusAsync(bookingId, BookingStatus.ProofSubmitted, null);
        Assert.True(proof.Succeeded, proof.Error);
        var paid = await bookings.ConfirmPaymentAsync(bookingId);
        Assert.True(paid.Succeeded, paid.Error);

        var detail = await bookings.GetAsync(bookingId);
        var amount = detail.Value!.TotalFee;
        Assert.True(amount > 0);

        var zero = await bookings.RefundPaymentAsync(bookingId, 0m, "zero");
        Assert.False(zero.Succeeded);
        Assert.Contains("greater than zero", zero.Error, StringComparison.OrdinalIgnoreCase);

        var tooMuch = await bookings.RefundPaymentAsync(bookingId, amount + 1m, "too much");
        Assert.False(tooMuch.Succeeded);
        Assert.Contains("cannot be more than", tooMuch.Error, StringComparison.OrdinalIgnoreCase);

        var missingReason = await bookings.RefundPaymentAsync(bookingId, amount, " ");
        Assert.False(missingReason.Succeeded);

        var refunded = await bookings.RefundPaymentAsync(bookingId, amount, "Patient cancelled after paying");
        Assert.True(refunded.Succeeded, refunded.Error);

        var after = await bookings.GetAsync(bookingId);
        Assert.Equal(PaymentStatus.Refunded, after.Value!.Booking.PaymentStatus);
    }

    [Fact]
    public void Report_csv_uses_web_filename_sections_bom_and_escaping()
    {
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 11, 21);
        Assert.Equal("The start date must be on or before the end date.", AdminReportService.InvalidDateRange(to, from));
        Assert.Null(AdminReportService.InvalidDateRange(from, to));
        Assert.Equal("clinic-reports_2026-10-01_to_2026-11-21.csv", AdminReportService.CsvFileName(from, to));

        var unpaid = new UnpaidCompletedRow(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            Guid.NewGuid(), "P-1", "Cruz, \"Lina\"", Guid.NewGuid(), "Dr. Reyes",
            new DateOnly(2026, 11, 10), 1500.5m, PaymentStatus.Unpaid);
        var daily = new DailySummaryRow(new DateOnly(2026, 11, 10), 3, 1, 1, 1, 0, 800m);
        var follow = new PendingFollowUpRow(Guid.Parse("11111111-2222-3333-4444-555555555555"), Guid.NewGuid(), "Lina Cruz", Guid.NewGuid(), "Dr. Reyes", new DateOnly(2026, 11, 20), "Recheck labs", FollowUpStatus.Pending);

        var csv = AdminReportService.BuildCsv(from, to, [unpaid], [daily], [follow]);
        Assert.StartsWith("\uFEFF", csv);
        Assert.Contains("Unpaid Completed Appointments", csv);
        Assert.Contains("Daily Booking Summary", csv);
        Assert.Contains("Pending Follow-Ups", csv);
        Assert.Contains("\"Cruz, \"\"Lina\"\"\"", csv);
        Assert.Contains("1500.5", csv);
        Assert.Contains("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", csv);
        Assert.Contains("Recheck labs", csv);
        Assert.Contains("\r\n", csv);
    }

    static async Task<(IAdminBookingService Bookings, Guid BookingId, Guid DoctorId)> OpenBookingAsync(AdminTestHost host, string first, string last)
    {
        await host.LoginAdminAsync();
        var patients = host.Get<IPatientService>();
        var bookings = host.Get<IAdminBookingService>();
        var doctorId = (await host.LoginDoctorAsync()).DoctorId!.Value;
        await host.LoginAdminAsync();
        var patient = await patients.CreateAsync(new NewPatientRequest(first, last, new DateOnly(1991, 3, 3), SexType.Female));
        var date = ClinicClock.Today.AddDays(3);
        for (var i = 0; i < 14; i++)
        {
            if (await bookings.ClosedReasonAsync(date, doctorId) is null) break;
            date = date.AddDays(1);
        }
        var created = await bookings.CreateAsync(new CreateAdminBookingRequest(
            patient.PatientId, doctorId, date, new TimeOnly(11, 0), new TimeOnly(11, 30),
            [], PaymentMode.PayAtClinic, IsWalkIn: false, Notes: null));
        Assert.True(created.Succeeded, created.Error);
        return (bookings, created.Value!.BookingId, doctorId);
    }
}
// JJQR - END
