//JJQR - AI (2026-11-25) - why: GAV-4 "Not finished on an earlier day" online-first: the list comes from GET /api/queue/leftovers, Move to today calls
// PUT /api/queue/{id}/move-to-today (409 → the server's sentence), no-show the existing PUT /api/queue/{id}/no-show; an older server (404) hides the list
// and says Move to today needs the updated server; Start consultation on a leftover calls it in (it is not on today's list).
using System.Net;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.Core.Doctor;
using ClinicNode.Core.Queue;

namespace ClinicNode.Api.Tests;

public class LeftoverQueueOnlineFirstTests
{
    private static Task<ApiRig> Rig() => ApiRig.StartAsync(s =>
    {
        s.AddOnlineFirst<ILeftoverQueueService, ApiLeftoverQueueService, LocalLeftoverQueueService>();
        s.AddOnlineFirstDoctorDashboard();
        s.AddOnlineFirst<IConsultationQueueService, ApiConsultationQueueService, LocalConsultationQueueService>();
    });

    static object Item(Guid id, Guid doctorId, string status) => new
    {
        booking_id = id, appointment_date = "2026-11-24", queue_number = "R-007", is_priority = false, patient_id = Guid.NewGuid(), patient_name = "Test Patient A",
        patient_code = "P-0001", doctor_id = doctorId, status, visit_type = "New", amount_due = 450, checked_in_at = DateTimeOffset.UtcNow.AddDays(-1),
        updated_at = DateTimeOffset.UtcNow.AddDays(-1), is_walk_in = true,
    };

    [Fact]
    public async Task Online_list_move_conflict_and_no_show_use_the_queue_endpoints()
    {
        await using var rig = await Rig();
        var id = Guid.NewGuid();
        var moved = false;
        var healthy = rig.Http.Route;
        rig.Http.Route = r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (p == "/api/queue/leftovers") return FakeHandler.Json(new { today = "2026-11-25", from = "2026-10-26", items = new[] { Item(id, rig.DoctorId, "CheckedIn"), Item(Guid.NewGuid(), Guid.NewGuid(), "OnHold") } });
            if (p == $"/api/queue/{id:D}/move-to-today")
            {
                if (moved) return FakeHandler.Json(new { message = "This visit was already moved to today." }, HttpStatusCode.Conflict);
                moved = true;
                return FakeHandler.Json(new { booking_id = id, appointment_date = "2026-11-25", queue_number = "R-012", status = "CheckedIn", previous_date = "2026-11-24", previous_queue_number = "R-007" });
            }
            if (p == $"/api/queue/{id:D}/no-show") return FakeHandler.Json(new { booking_id = id, status = "NoShow" });
            return healthy(r);
        };
        var svc = rig.Get<ILeftoverQueueService>();

        var mine = await svc.ListAsync(rig.DoctorId);
        var row = Assert.Single(mine);
        Assert.Equal(new DateOnly(2026, 11, 24), row.Date);
        Assert.Equal(BookingStatus.CheckedIn, row.Status);
        Assert.Equal("Test Patient A", row.PatientName);
        Assert.Equal(2, (await svc.ListAsync(null)).Count); // staff: the clinic's

        var first = await svc.MoveToTodayAsync(id);
        Assert.True(first.Succeeded, first.Error);
        var second = await svc.MoveToTodayAsync(id);
        Assert.False(second.Succeeded);
        Assert.Equal("This visit was already moved to today.", second.Error);

        Assert.True((await svc.MarkNoShowAsync(id)).Succeeded);
        Assert.True(rig.Http.Saw($"/api/queue/{id:D}/no-show"));
    }

    [Fact]
    public async Task An_older_server_hides_the_list_and_says_move_needs_the_update()
    {
        await using var rig = await Rig(); // every non-health path answers 404
        var svc = rig.Get<ILeftoverQueueService>();
        Assert.Empty(await svc.ListAsync(rig.DoctorId));
        var move = await svc.MoveToTodayAsync(Guid.NewGuid());
        Assert.False(move.Succeeded);
        Assert.Equal(LeftoverGroups.MoveOldServer, move.Error);
    }

    [Fact]
    public async Task Start_consultation_on_a_leftover_calls_it_in()
    {
        await using var rig = await Rig();
        var id = Guid.NewGuid();
        var healthy = rig.Http.Route;
        rig.Http.Route = r =>
        {
            var p = r.RequestUri!.AbsolutePath;
            if (p == "/api/bookings/doctor/today") return FakeHandler.Json(Array.Empty<object>());
            if (p == "/api/queue/leftovers") return FakeHandler.Json(new { items = new[] { Item(id, rig.DoctorId, "CheckedIn") } });
            if (p == $"/api/queue/{id:D}/call" && r.Method == HttpMethod.Put) return FakeHandler.Json(new { booking_id = id, status = "InProgress" });
            return healthy(r);
        };
        var res = await rig.Get<IConsultationQueueService>().StartConsultAsync(rig.DoctorId, id);
        Assert.True(res.Succeeded, res.Error);
        Assert.True(rig.Http.Saw($"/api/queue/{id:D}/call"));
        var other = await rig.Get<IConsultationQueueService>().StartConsultAsync(rig.DoctorId, Guid.NewGuid());
        Assert.False(other.Succeeded);
    }
}
//JJQR - END
