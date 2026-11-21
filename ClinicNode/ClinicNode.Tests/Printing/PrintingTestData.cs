//JJQR - AI (Nov 21, 2026) - shared sample data for the printing tests.
using ClinicNode.Core.Printing;

namespace ClinicNode.Tests.Printing;

static class PrintingTestData
{
    public static readonly PrintClinic Clinic = new("Grace Medical Clinic", "Buaya, Lapu Lapu City", "0928");
    public static readonly PrintDoctor Doctor = new("Jane Doe", "LIC-99", "PTR-88", "S2-77");
    public static readonly PrintPatient Patient = new("Juan Dela Cruz", "MF-000123", "1990-05-14", "Male", "Pusok, Lapu-Lapu City");

    public static readonly RxLine Paracetamol = new()
    {
        GenericName = "Paracetamol", Dosage = "500 mg", Quantity = "20 tablets", Instruction = "as needed", Timing = "Breakfast,Dinner",
        MealRelation = "After", DurationKind = "Days", DurationValue = 5, Indication = "for fever",
    };

    public static readonly QueueSlipData Ticket = new()
    {
        ClinicName = "Grace Medical Clinic", ClinicAddress = "Buaya, Lapu Lapu City", QueueNumber = "S-004", IsPriority = true,
        PatientName = "Juan <Dela> Cruz", PatientCode = "MF-000123", DoctorName = "Dr. Jane Doe", VisitType = "FollowUp",
        ProvisionalFee = 350.00m, IssuedAt = new DateTimeOffset(2026, 11, 21, 6, 15, 0, TimeSpan.Zero),
    };

    public static readonly ReceiptPrintModel Receipt = new()
    {
        OrNumber = "OR-2026-000123", BookingId = Guid.Parse("11111111-2222-3333-4444-555555555555"), PatientName = "Juan Dela Cruz",
        DoctorName = "Jane Doe", AppointmentDate = new DateOnly(2026, 11, 21), Amount = 450.00m, PaymentStatus = "Paid",
    };

    public sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
//JJQR - END
