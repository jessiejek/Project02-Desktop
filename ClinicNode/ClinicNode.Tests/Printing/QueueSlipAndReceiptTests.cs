//JJQR - AI (Nov 21, 2026) - queue slip (web, thermal 80/58, ESC/POS) and Official Receipt (incl. PENDING SYNC) tests.
using System.Text;
using ClinicNode.Core.Printing;
using static ClinicNode.Tests.Printing.PrintingTestData;

namespace ClinicNode.Tests.Printing;

public class QueueSlipAndReceiptTests
{
    [Fact]
    public void Web_slip_matches_walk_in_template_content()
    {
        var doc = QueueSlip.Web(Ticket);
        Assert.Equal("Queue S-004", doc.Title);
        Assert.Equal(PaperKind.PrinterDefault, PrintPaper.KindOf(doc.Page));
        Assert.Contains("<div class=\"brand\">Dr. Grace Gavino Clinic</div>", doc.Html);
        Assert.Contains("<div class=\"queue\">S-004</div>", doc.Html);
        Assert.Contains("<p><strong>PRIORITY LANE · Senior / PWD</strong></p>", doc.Html);
        Assert.Contains("<p><strong>Juan &lt;Dela&gt; Cruz</strong> (MF-000123)</p>", doc.Html);
        Assert.Contains("Follow-up · provisional ₱350</p>", doc.Html);
        Assert.Contains("11/21/2026, 2:15:00 PM", doc.Html);
    }

    [Fact]
    public void Regular_lane_slip_has_no_priority_line()
    {
        var html = QueueSlip.Web(Ticket with { IsPriority = false, QueueNumber = "R-010", VisitType = "New", ProvisionalFee = 450.5m }).Html;
        Assert.DoesNotContain("PRIORITY", html);
        Assert.Contains("New · provisional ₱450.5", html);
    }

    [Theory]
    [InlineData(80, 72, 48)]
    [InlineData(58, 48, 32)]
    public void Thermal_slip_uses_roll_width_and_printable_head(int paperMm, int printableMm, int columns)
    {
        var roll = paperMm == 80 ? PrintPaper.Thermal80 : PrintPaper.Thermal58;
        var doc = QueueSlip.Thermal(Ticket, roll);
        Assert.Equal(PaperKind.Roll, PrintPaper.KindOf(doc.Page));
        Assert.Equal(printableMm, PrintPaper.PrintableWidthMm(roll));
        Assert.Equal(columns, PrintPaper.EscPosColumns(roll));
        Assert.Contains($"width: {paperMm}mm", doc.Html);
        Assert.Contains("S-004", doc.Html);
    }

    [Fact]
    public void Thermal_slip_rejects_sheet_paper() =>
        Assert.Throws<ArgumentException>(() => QueueSlip.Thermal(Ticket, PrintPaper.A5));

    [Fact]
    public void EscPos_slip_initialises_centres_prints_big_number_and_cuts()
    {
        var bytes = QueueSlip.EscPos(Ticket, PrintPaper.Thermal80);
        Assert.Equal(new byte[] { 0x1B, 0x40, 0x1B, 0x61, 0x01 }, bytes[..5]);
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x42, 0x00 }, bytes[^4..]);
        var text = Encoding.ASCII.GetString(bytes);
        Assert.Contains("S-004", text);
        Assert.Contains("PRIORITY LANE - Senior / PWD", text);
        Assert.Contains("provisional PHP 350", text);
        Assert.Contains(new string('-', 48), text);
        Assert.Contains(Encoding.ASCII.GetString([0x1D, 0x21, 0x22]), text); // triple size
        var noCut = QueueSlip.EscPos(Ticket, PrintPaper.Thermal58, cut: false);
        Assert.NotEqual(new byte[] { 0x1D, 0x56, 0x42, 0x00 }, noCut[^4..]);
    }

    [Fact]
    public void EscPos_wrap_breaks_on_words_and_splits_long_words()
    {
        Assert.Equal(["Buaya, Lapu", "Lapu City"], EscPosBuilder.Wrap("Buaya, Lapu Lapu City", 11));
        Assert.Equal(["ABCDE", "FGH"], EscPosBuilder.Wrap("ABCDEFGH", 5));
    }

    [Fact]
    public void Receipt_matches_web_official_receipt_fields()
    {
        var doc = ReceiptDocument.Build(Receipt);
        Assert.Equal("Receipt OR-2026-000123", doc.Title);
        Assert.Equal(ClinicPrintKind.Receipt, doc.Info().Kind);
        Assert.Contains("<h1>Official Receipt</h1>", doc.Html);
        Assert.Contains("<p class=\"meta\">OR-2026-000123</p>", doc.Html);
        Assert.Contains("<p><strong>Patient:</strong> Juan Dela Cruz</p>", doc.Html);
        Assert.Contains("<p><strong>Doctor:</strong> Jane Doe</p>", doc.Html);
        Assert.Contains("<p><strong>Date:</strong> 2026-11-21</p>", doc.Html);
        Assert.Contains("<p><strong>Amount:</strong> ₱450</p>", doc.Html);
        Assert.Contains("<p><strong>Payment:</strong> Paid</p>", doc.Html);
        Assert.DoesNotContain(ReceiptDocument.PendingSyncText, doc.Html);
        Assert.DoesNotContain("pending-sync", doc.Html);
    }

    [Fact]
    public void Receipt_pending_sync_prints_a_visible_mark_and_title_suffix()
    {
        var doc = ReceiptDocument.Build(Receipt with { IsPendingSync = true });
        Assert.EndsWith("(pending sync)", doc.Title);
        Assert.Contains("<div class=\"pending-sync\" role=\"note\"><strong>PENDING SYNC</strong>", doc.Html);
        Assert.Contains(".pending-sync {", doc.Html);
        Assert.True(doc.Html.IndexOf("PENDING SYNC", StringComparison.Ordinal) < doc.Html.IndexOf("Official Receipt</h1>", StringComparison.Ordinal));
    }

    [Fact]
    public void Receipt_without_or_number_shows_dash_and_booking_id_title()
    {
        var doc = ReceiptDocument.Build(Receipt with { OrNumber = null, PatientName = "A & B" });
        Assert.Equal("Receipt 11111111-2222-3333-4444-555555555555", doc.Title);
        Assert.Contains("<p class=\"meta\">—</p>", doc.Html);
        Assert.Contains("A &amp; B", doc.Html);
    }
}
//JJQR - END
