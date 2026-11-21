//JJQR - AI (Nov 21, 2026) - side data a native printer needs beyond the HTML (e.g. the queue ticket for raw ESC/POS), attached to a
// PrintDocument without changing that shared record. Keyed by reference, collected with the document.
using System.Runtime.CompilerServices;

namespace ClinicNode.Core.Printing;

public enum ClinicPrintKind { Other, Prescription, LabRequest, MedicalCertificate, QueueSlip, Receipt }

public sealed record PrintJobInfo(ClinicPrintKind Kind, QueueSlipData? QueueSlip = null);

public static class PrintJobInfoExtensions
{
    static readonly ConditionalWeakTable<PrintDocument, PrintJobInfo> Table = new();

    public static PrintDocument WithInfo(this PrintDocument document, PrintJobInfo info)
    {
        Table.AddOrUpdate(document, info);
        return document;
    }

    public static PrintJobInfo Info(this PrintDocument document) =>
        Table.TryGetValue(document, out var info) ? info : new PrintJobInfo(ClinicPrintKind.Other);
}
//JJQR - END
