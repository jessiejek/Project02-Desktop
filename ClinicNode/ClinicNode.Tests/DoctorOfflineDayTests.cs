//JJQR - AI (Nov 22, 2026) - offline-mode plan steps 2.2, 2.4, 2.5: one clinic day under the doctor's account alone, in Offline mode, on the app's real service graph with NO clinic server at all.
// Register -> find -> check in (paper queue allowed) -> typed booklet receipt -> print slip and receipt from local data -> Staff refused.
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Api.Auth;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Print;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests;

public class DoctorOfflineDayTests
{
    [Fact]
    public async Task The_doctor_runs_a_whole_offline_day_alone_and_everything_waits_for_sync()
    {
        await using var host = await AppTestHost.StartAsync();
        var auth = host.Get<IAuthService>();
        var mode = host.Get<IWorkModeService>();
        await mode.InitializeAsync();

        // the doctor signs in and switches the computer to Offline mode
        var doctor = await auth.LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, rememberEmail: false);
        Assert.True(doctor.Succeeded, doctor.Error);
        var doctors = await host.Get<IQueueService>().ListDoctorsAsync();
        var doctorId = doctors[0].DoctorId;
        // D7: a computer that is not registered with the clinic server cannot go Offline; once it is (a stored node credential), it can
        Assert.False((await mode.SetAsync(WorkMode.Offline)).Succeeded);
        await host.Get<ClinicNode.Sync.Auth.INodeCredentialStore>().SaveAsync(new ClinicNode.Sync.Auth.NodeCredential(Guid.NewGuid(), "test-secret"));
        Assert.True((await mode.SetAsync(WorkMode.Offline)).Succeeded);

        // 1. register a new patient (the desktop makes the id), then find them again from the local list
        var patientId = Guid.NewGuid();
        var (registered, row) = await host.Get<IStaffPatientsService>().QuickRegisterAsync(
            new QuickRegisterForm("Mia", null, "Offlinetest", new DateOnly(1992, 4, 4), SexType.Female, "09171234567", "Cebu", patientId));
        Assert.True(registered.Succeeded, registered.Error);
        Assert.Equal(patientId, row!.PatientId);
        var found = await host.Get<IStaffPatientsService>().SearchAsync("Offlinetest");
        Assert.Contains(found.Items, p => p.PatientId == patientId);

        // 2. check in as today's visit: with no reserved number it is a paper-queue patient, but the doctor is never blocked
        var bookingId = Guid.NewGuid();
        var walkIn = await host.Get<IQueueService>().CheckInWalkInAsync(new WalkInRequest(patientId, doctorId, BookingId: bookingId, PaymentId: Guid.NewGuid()));
        Assert.True(walkIn.Succeeded, walkIn.Error);
        Assert.Equal(bookingId, walkIn.Ticket!.BookingId);

        // 3. the slip prints from local data
        var slip = await host.Get<IPrintDocumentFactory>().QueueSlipAsync(bookingId);
        Assert.True(slip.Html.Contains("PAPER QUEUE") || (walkIn.Ticket.QueueNumber.Length > 0 && slip.Html.Contains(walkIn.Ticket.QueueNumber)), "the slip must show the number or PAPER QUEUE");

        // 4. (starting the visit and the consultation itself are covered by ConsultationServiceTests; this demo database gives the signed-in doctor a different id than its doctor row)

        // 5. the doctor marks the payment received with the booklet receipt number; no automatic numbering
        var paid = await host.Get<IStaffPaymentsService>().RecordAsync(new RecordPaymentInput(bookingId, PaymentMethod.Cash, "450", null, null, "BK-000501"));
        Assert.True(paid.Succeeded, paid.Error);

        // 6. the receipt prints from local data and shows the typed number
        var receipt = await host.Get<IPrintDocumentFactory>().PaymentReceiptAsync(bookingId);
        Assert.Contains("BK-000501", receipt.Html);

        // 7. all of it waits in the outbox for the next sync
        await using var db = await host.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        var pending = await host.Get<ISyncOutbox>().ListPendingAsync(db, 500);
        foreach (var aggregate in new[] { "patient", "booking", "payment" })
            Assert.Contains(pending, e => e.Aggregate == aggregate);

        // 8. Staff and Admin cannot use the computer while it is Offline
        await auth.LogoutAsync();
        foreach (var who in new[] { "staff@clinic.test", "admin@clinic.test" })
        {
            var r = await auth.LoginAsync(who, DevDataSeeder.DevPassword, rememberEmail: false);
            Assert.False(r.Succeeded);
            Assert.Equal(OnlineFirstAuthService.OfflineDoctorOnly, r.Error);
        }
    }
}
//JJQR - END
