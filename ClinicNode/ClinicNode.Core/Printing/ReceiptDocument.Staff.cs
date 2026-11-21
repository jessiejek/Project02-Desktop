//JJQR - AI (Nov 21, 2026) - staff-payments mapping, kept apart so the print-smoke tool (no Staff types) can skip it.
namespace ClinicNode.Core.Printing;

public static partial class ReceiptDocument
{
    /// <summary>Maps the staff payments receipt (IStaffPaymentsService.GetReceiptAsync) to the print model.</summary>
    public static ReceiptPrintModel FromView(ClinicNode.Core.Staff.ReceiptView v) => new()
    {
        OrNumber = string.IsNullOrWhiteSpace(v.OrNumber) ? ClinicNode.Core.Staff.StaffRules.OrNumberFor(v.BookingId) : v.OrNumber,
        BookingId = v.BookingId,
        PatientName = v.PatientName,
        DoctorName = v.DoctorName,
        AppointmentDate = v.VisitDate,
        Amount = v.Amount,
        PaymentStatus = v.Status.ToString(),
        IsPendingSync = v.PendingSync,
    };
}
//JJQR - END
