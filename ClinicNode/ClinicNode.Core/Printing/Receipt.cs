//JJQR - AI (Nov 21, 2026) - Official Receipt print: the web template (Project02-fe app/admin/bookings/[id]/page.tsx "Print receipt")
// plus a visible PENDING SYNC mark when the payment is still only in the local outbox (MAUI-ARCHITECTURE §6 "Receipt / OR").
using System.Globalization;

namespace ClinicNode.Core.Printing;

/// <summary>What Record payment hands to the printer. Keep this shape stable (allen3 calls it).</summary>
public sealed record ReceiptPrintModel
{
    /// <summary>OR number; null/empty prints "—" like the web.</summary>
    public string? OrNumber { get; init; }
    public Guid BookingId { get; init; }
    public string PatientName { get; init; } = "";
    public string DoctorName { get; init; } = "";
    public DateOnly AppointmentDate { get; init; }
    public decimal Amount { get; init; }
    /// <summary>Payment status text as the web shows it, e.g. "Paid", "Waived".</summary>
    public string PaymentStatus { get; init; } = "Paid";
    /// <summary>True while the payment has not been acknowledged by the cloud (unacked outbox row). Prints a PENDING SYNC mark.</summary>
    public bool IsPendingSync { get; init; }
}

/// <summary>Printer capability for receipts. MauiPrinter implements it on Mac Catalyst and Windows.</summary>
public interface IReceiptPrinter
{
    Task PrintReceiptAsync(ReceiptPrintModel model, CancellationToken ct = default);
}

public static partial class ReceiptDocument
{
    public const string PendingSyncText = "PENDING SYNC";
    public const string PendingSyncNote = "Recorded offline on this clinic computer. Not yet synced to the online system.";

    const string PendingCss =
        ".pending-sync { border: 2px solid #b3261e; color: #b3261e; border-radius: 8px; padding: 8px 12px; margin: 0 0 12px; text-align: center; }\n" +
        "    .pending-sync strong { display: block; font-size: 16px; letter-spacing: 0.12em; }\n" +
        "    .pending-sync span { font-size: 11px; }";

    static string E(string? s) => ClinicFormsHtml.EscapeHtml(s ?? "");

    public static string Title(ReceiptPrintModel m) =>
        "Receipt " + (string.IsNullOrEmpty(m.OrNumber) ? m.BookingId.ToString() : m.OrNumber) + (m.IsPendingSync ? " (pending sync)" : "");

    public static string BodyHtml(ReceiptPrintModel m)
    {
        var pending = m.IsPendingSync
            ? $"""<div class="pending-sync" role="note"><strong>{PendingSyncText}</strong><span>{E(PendingSyncNote)}</span></div>"""
            : "";
        return $"""
            {pending}<h1>Official Receipt</h1>
                                 <p class="meta">{E(string.IsNullOrEmpty(m.OrNumber) ? "—" : m.OrNumber)}</p>
                                 <div class="card">
                                   <p><strong>Patient:</strong> {E(m.PatientName)}</p>
                                   <p><strong>Doctor:</strong> {E(m.DoctorName)}</p>
                                   <p><strong>Date:</strong> {E(m.AppointmentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}</p>
                                   <p><strong>Amount:</strong> ₱{E(QueueSlip.JsAmount(m.Amount))}</p>
                                   <p><strong>Payment:</strong> {E(m.PaymentStatus)}</p>
                                 </div>
            """;
    }

    /// <summary>Full printable document (web printHtml default chrome + brand line, printer's default paper).</summary>
    public static PrintDocument Build(ReceiptPrintModel m) =>
        new PrintDocument(Title(m), PrintPaper.PrinterDefault,
                PrintHtmlDocument.Wrap(Title(m), BodyHtml(m), bare: false, extraCss: m.IsPendingSync ? PendingCss : null))
            .WithInfo(new PrintJobInfo(ClinicPrintKind.Receipt));
}
//JJQR - END
