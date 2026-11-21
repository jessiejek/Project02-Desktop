//JJQR - AI (Nov 21, 2026) - ReceiptView → ReceiptPrintModel mapping (pending sync and OR fallback).
namespace ClinicNode.Tests.Printing;

    public class ReceiptMappingTests
    {
        [Fact]
        public void FromView_keeps_pending_sync_and_falls_back_to_booking_or_number()
        {
            var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
            var view = new ClinicNode.Core.Staff.ReceiptView(id, "Juan", "Dr. Doe", new DateOnly(2026, 11, 21), 450m,
                ClinicApp.Domain.Enums.PaymentStatus.Paid, null, null, null, null, DateTimeOffset.UtcNow, PendingSync: true);
            var m = ClinicNode.Core.Printing.ReceiptDocument.FromView(view);
            Assert.True(m.IsPendingSync);
            Assert.Equal(ClinicNode.Core.Staff.StaffRules.OrNumberFor(id), m.OrNumber);
            Assert.Equal("Paid", m.PaymentStatus);
            Assert.Contains("PENDING SYNC", ClinicNode.Core.Printing.ReceiptDocument.Build(m).Html);
        }
    }
//JJQR - END
