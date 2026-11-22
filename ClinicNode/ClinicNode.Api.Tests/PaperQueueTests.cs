//JJQR - AI (Nov 22, 2026) - offline-mode plan step 2.3 (owner Q1): with no reserved queue number left, the doctor still checks the patient in ("Paper queue"): no number, never blocked
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Printing;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Api.Tests;

public class PaperQueueTests
{
    private static Task<ApiRig> StartAsync() => ApiRig.StartAsync(s => s.AddOnlineFirst<IStaffWalkInService, ApiStaffWalkInService, StaffWalkInService>());

    [Fact]
    public async Task Offline_with_no_reserved_number_the_patient_is_added_without_a_number_and_queued_for_sync()
    {
        await using var rig = await StartAsync();
        var patient = await rig.Get<IPatientService>().CreateAsync(new NewPatientRequest("Paper", "Queue", new DateOnly(1990, 1, 1), SexType.Female));
        await rig.GoOfflineAsync();                                  // no block was reserved on this computer
        var bookingId = Guid.NewGuid();

        var r = await rig.Get<IStaffWalkInService>().CheckInWalkInAsync(new WalkInRequest(patient.PatientId, rig.DoctorId, BookingId: bookingId));

        Assert.True(r.Succeeded, r.Error);
        Assert.Equal("", r.Ticket!.QueueNumber);                      // no number yet
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        var b = await db.Bookings.SingleAsync(x => x.BookingId == bookingId);
        Assert.Null(b.QueueNumber);
        Assert.Equal(BookingStatus.CheckedIn, b.Status);              // the doctor can start the visit
        Assert.True(await db.Set<ClinicApp.Domain.Entities.Payment>().AnyAsync(p => p.BookingId == bookingId));
        var pending = await rig.Get<ClinicNode.Data.ISyncOutbox>().CountPendingAsync(db);
        Assert.True(pending > 0, "the booking must wait in the outbox for the next sync");
    }

    [Fact]
    public async Task Several_paper_queue_patients_can_be_added_and_pressing_the_button_again_does_not_duplicate()
    {
        await using var rig = await StartAsync();
        var svc = rig.Get<IStaffWalkInService>();
        await rig.GoOfflineAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            var p = await rig.Get<IPatientService>().CreateAsync(new NewPatientRequest("Paper", "No" + i, new DateOnly(1990, 1, 1 + i), SexType.Male));
            var id = Guid.NewGuid(); ids.Add(id);
            Assert.True((await svc.CheckInWalkInAsync(new WalkInRequest(p.PatientId, rig.DoctorId, BookingId: id))).Succeeded);
            Assert.True((await svc.CheckInWalkInAsync(new WalkInRequest(p.PatientId, rig.DoctorId, BookingId: id))).Succeeded);   // same button again
        }
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        Assert.Equal(3, await db.Bookings.CountAsync(b => ids.Contains(b.BookingId)));
    }

    [Fact]
    public async Task A_reserved_number_is_still_used_when_there_is_one()
    {
        await using var rig = await StartAsync();
        await using (var db0 = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync())
            foreach (var lane in new[] { "R", "S" })
                await QueueBlocks.SaveAsync(db0, new QueueBlockReservation(ClinicApp.Domain.ClinicClock.Today, lane, 1, 20, 20, null, null));
        var p = await rig.Get<IPatientService>().CreateAsync(new NewPatientRequest("Has", "Number", new DateOnly(1990, 1, 1), SexType.Male));
        await rig.GoOfflineAsync();
        var r = await rig.Get<IStaffWalkInService>().CheckInWalkInAsync(new WalkInRequest(p.PatientId, rig.DoctorId, BookingId: Guid.NewGuid()));
        Assert.True(r.Succeeded, r.Error);
        Assert.StartsWith("R-", r.Ticket!.QueueNumber);
    }

    [Fact]
    public void The_slip_says_PAPER_QUEUE_instead_of_a_blank()
    {
        var t = new QueueSlipData { ClinicName = "Grace", QueueNumber = "", PatientName = "Ana Cruz", PatientCode = "AC-1", DoctorName = "Dr G", IssuedAt = DateTimeOffset.UtcNow };
        Assert.Contains("PAPER QUEUE", QueueSlip.WebBodyHtml(t));
        Assert.Contains("PAPER QUEUE", QueueSlip.ThermalLines(t).Select(l => l.Text));
        Assert.Contains("Queue PAPER QUEUE", QueueSlip.Web(t).Title);
        var numbered = t with { QueueNumber = "R-007" };
        Assert.DoesNotContain("PAPER QUEUE", QueueSlip.WebBodyHtml(numbered));
    }
}
//JJQR - END
