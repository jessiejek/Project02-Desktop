//JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.3: the DESKTOP makes the ids, so a reply that is lost on the way back cannot produce a second booking or patient.
// The fake server "saves" (records) the request and then times out; the online-first proxy retries the SAME request locally. Both must carry the same ids.
using System.Net;
using System.Text.Json;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Core.Sync;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using ClinicNode.Api.Services;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Api.Tests;

public class ClientIdLostReplyTests
{
    private static Task<ApiRig> StartAsync() => ApiRig.StartAsync(s =>
    {
        s.AddOnlineFirst<IStaffWalkInService, ApiStaffWalkInService, StaffWalkInService>();
        s.AddOnlineFirst<IStaffPatientsService, ApiStaffPatientsService, StaffPatientsService>();
    });

    /// <summary>Offline numbers come only from a reserved block (V10), so a test that checks in locally reserves one first, as a sync would.</summary>
    private static async Task ReserveBlockAsync(ApiRig rig)
    {
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        foreach (var lane in new[] { "R", "S" })
            await QueueBlocks.SaveAsync(db, new QueueBlockReservation(ClinicApp.Domain.ClinicClock.Today, lane, 1, 20, 20, null, null));
    }

    private static string? Field(string? body, string name) =>
        body is null ? null : JsonDocument.Parse(body).RootElement.TryGetProperty(name, out var v) ? v.GetString() : null;

    [Fact]
    public async Task Walk_in_with_a_lost_reply_uses_the_same_ids_locally_and_stays_one_booking()
    {
        await using var rig = await StartAsync();
        var patient = await rig.Get<IPatientService>().CreateAsync(new NewPatientRequest("Lost", "Reply", new DateOnly(1990, 1, 1), SexType.Male));
        rig.Http.Route = r => r.RequestUri!.AbsolutePath is "/health" or "/health/ready" ? FakeHandler.Status(HttpStatusCode.OK)
            : r.RequestUri!.AbsolutePath == "/api/queue" ? throw new TaskCanceledException("the reply never came") : FakeHandler.Status(HttpStatusCode.NotFound);
        await ReserveBlockAsync(rig);
        var bookingId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var request = new WalkInRequest(patient.PatientId, rig.DoctorId, BookingId: bookingId, PaymentId: paymentId);
        var walkIn = rig.Get<IStaffWalkInService>();

        // step 1.2 (rule R1): Online mode no longer saves quietly after a lost reply. The save is refused and nothing is kept here...
        await Assert.ThrowsAsync<OnlineWriteFailedException>(() => walkIn.CheckInWalkInAsync(request));
        await using (var db0 = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync())
            Assert.Equal(0, await db0.Bookings.CountAsync(b => b.BookingId == bookingId));
        // ...the doctor switches to Offline mode and presses the same button again: the SAME ids go into the local copy
        await rig.GoOfflineAsync();
        var first = await walkIn.CheckInWalkInAsync(request);

        Assert.True(first.Succeeded, first.Error);
        var sent = rig.Http.Requests.Single(r => r.PathAndQuery == "/api/queue" && r.Method == HttpMethod.Post);
        Assert.Equal(bookingId.ToString(), Field(sent.Body, "booking_id"));      // what the server received (the lost reply)
        Assert.Equal(paymentId.ToString(), Field(sent.Body, "payment_id"));
        Assert.Equal(bookingId, first.Ticket!.BookingId);                        // what the local save kept: the SAME id
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await db.Bookings.CountAsync(b => b.BookingId == bookingId));
        Assert.True(await db.Payments.AnyAsync(p => p.PaymentId == paymentId && p.BookingId == bookingId));

        // the user presses the button again: same request, still no network → same ticket, still one booking, no second queue number
        var again = await walkIn.CheckInWalkInAsync(request);
        Assert.True(again.Succeeded, again.Error);
        Assert.Equal(first.Ticket.QueueNumber, again.Ticket!.QueueNumber);
        Assert.Equal(1, await db.Bookings.CountAsync(b => b.PatientId == patient.PatientId));
    }

    [Fact]
    public async Task Walk_in_booking_id_of_another_patient_is_refused_locally()
    {
        await using var rig = await StartAsync();
        var a = await rig.Get<IPatientService>().CreateAsync(new NewPatientRequest("Pat", "One", new DateOnly(1990, 1, 1), SexType.Male));
        var b = await rig.Get<IPatientService>().CreateAsync(new NewPatientRequest("Pat", "Two", new DateOnly(1991, 2, 2), SexType.Female));
        rig.Http.Route = _ => throw new HttpRequestException("down");
        await ReserveBlockAsync(rig);
        await rig.GoOfflineAsync(); //JJQR - AI (Nov 22, 2026) - step 1.2 //JJQR - END
        var id = Guid.NewGuid();
        var walkIn = rig.Get<IStaffWalkInService>();
        Assert.True((await walkIn.CheckInWalkInAsync(new WalkInRequest(a.PatientId, rig.DoctorId, BookingId: id))).Succeeded);
        var clash = await walkIn.CheckInWalkInAsync(new WalkInRequest(b.PatientId, rig.DoctorId, BookingId: id));
        Assert.False(clash.Succeeded);
    }

    [Fact]
    public async Task Patient_registration_with_a_lost_reply_uses_the_same_patient_id_locally()
    {
        await using var rig = await StartAsync();
        rig.Http.Route = r => r.RequestUri!.AbsolutePath is "/health" or "/health/ready" ? FakeHandler.Status(HttpStatusCode.OK)
            : r.RequestUri!.AbsolutePath == "/api/patients" ? throw new TaskCanceledException("the reply never came") : FakeHandler.Status(HttpStatusCode.NotFound);
        var patientId = Guid.NewGuid();
        var form = new QuickRegisterForm("Ana", null, "Lopez", new DateOnly(1992, 5, 5), SexType.Female, "09170000000", "Cebu", patientId);
        var svc = rig.Get<IStaffPatientsService>();

        await Assert.ThrowsAsync<OnlineWriteFailedException>(() => svc.QuickRegisterAsync(form));   // step 1.2 (R1): Online + lost reply = refused, nothing kept
        await rig.GoOfflineAsync();
        var (result, row) = await svc.QuickRegisterAsync(form);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(patientId, row!.PatientId);                                                         // local copy keeps the desktop's id
        var sent = rig.Http.Requests.Single(r => r.PathAndQuery == "/api/patients" && r.Method == HttpMethod.Post);
        Assert.Equal(patientId.ToString(), Field(sent.Body, "patient_id"));                              // and so did the request the server got
        var (_, again) = await svc.QuickRegisterAsync(form);                                              // button pressed again
        Assert.Equal(patientId, again!.PatientId);
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        Assert.Equal(1, await db.Patients.CountAsync(p => p.PatientId == patientId));
    }
}
//JJQR - END
