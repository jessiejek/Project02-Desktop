//JJQR - AI (Nov 22, 2026) - offline-mode plan step 2.4 (D6, Q3): the doctor marks a payment received and types the booklet receipt number; no automatic numbering on that path
using System.Net;
using System.Text.Json;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Api.Tests;

public class BookletReceiptTests
{
    private static Task<ApiRig> StartAsync() => ApiRig.StartAsync(s =>
    {
        s.AddOnlineFirst<IStaffWalkInService, ApiStaffWalkInService, StaffWalkInService>();
        s.AddOnlineFirst<IStaffPaymentsService, ApiStaffPaymentsService, StaffPaymentsService>();
    });

    /// <summary>A checked-in visit with an unpaid payment on this computer (made Offline, as the doctor would).</summary>
    private static async Task<Guid> UnpaidVisitAsync(ApiRig rig, string last)
    {
        await using (var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync())
            foreach (var lane in new[] { "R", "S" })
                await QueueBlocks.SaveAsync(db, new QueueBlockReservation(ClinicApp.Domain.ClinicClock.Today, lane, 1, 20, 20, null, null));
        var p = await rig.Get<IPatientService>().CreateAsync(new NewPatientRequest("Pay", last, new DateOnly(1990, 1, 1), SexType.Male));
        var id = Guid.NewGuid();
        var r = await rig.Get<IStaffWalkInService>().CheckInWalkInAsync(new WalkInRequest(p.PatientId, rig.DoctorId, BookingId: id));
        Assert.True(r.Succeeded, r.Error);
        return id;
    }

    /// <summary>What the screen would send: the exact amount due, prefilled.</summary>
    private static async Task<RecordPaymentInput> InputAsync(ApiRig rig, Guid booking, string? or)
    {
        var due = await WithDb(rig, db => db.Payments.Where(p => p.BookingId == booking).Select(p => p.Amount).SingleAsync());
        return new(booking, PaymentMethod.Cash, due.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), null, null, or);
    }

    [Fact]
    public async Task Offline_the_typed_booklet_number_is_saved_as_typed_and_queued_for_sync()
    {
        await using var rig = await StartAsync();
        await rig.GoOfflineAsync();
        var booking = await UnpaidVisitAsync(rig, "One");
        rig.Http.Requests.Clear();

        var r = await rig.Get<IStaffPaymentsService>().RecordAsync(await InputAsync(rig, booking, "  BK-000123 "));

        Assert.True(r.Succeeded, r.Error);
        Assert.Empty(rig.Http.Requests.Where(x => x.Method != HttpMethod.Get || !x.PathAndQuery.StartsWith("/health")));   // Offline: the server was not used
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        var pay = await db.Payments.SingleAsync(p => p.BookingId == booking);
        Assert.Equal("BK-000123", pay.OrNumber);                      // trimmed, exactly what was typed, no OR-<node>-<date>-<seq>
        Assert.Equal(PaymentStatus.Paid, pay.Status);
        Assert.NotNull(pay.ConfirmedByUserId);                          // under the signed-in doctor's account
        Assert.True(await rig.Get<ClinicNode.Data.ISyncOutbox>().CountPendingAsync(db) > 0);
    }

    [Fact]
    public async Task A_number_already_used_on_this_computer_is_refused_and_nothing_is_saved()
    {
        await using var rig = await StartAsync();
        await rig.GoOfflineAsync();
        var first = await UnpaidVisitAsync(rig, "A");
        var second = await UnpaidVisitAsync(rig, "B");
        var svc = rig.Get<IStaffPaymentsService>();
        Assert.True((await svc.RecordAsync(await InputAsync(rig, first, "BK-77"))).Succeeded);

        var dup = await svc.RecordAsync(await InputAsync(rig, second, "bk-77"));       // same number, different capitals
        Assert.False(dup.Succeeded);
        Assert.Contains("already used", dup.Error);
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        Assert.Equal(PaymentStatus.Unpaid, (await db.Payments.SingleAsync(p => p.BookingId == second)).Status);
        Assert.True((await svc.RecordAsync(await InputAsync(rig, second, "BK-78"))).Succeeded);   // a different number goes through
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_number_is_refused_on_the_typed_path(string typed)
    {
        await using var rig = await StartAsync();
        await rig.GoOfflineAsync();
        var booking = await UnpaidVisitAsync(rig, "Blank");
        var r = await rig.Get<IStaffPaymentsService>().RecordAsync(await InputAsync(rig, booking, typed));
        Assert.False(r.Succeeded);
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        Assert.Equal(PaymentStatus.Unpaid, (await db.Payments.SingleAsync(p => p.BookingId == booking)).Status);
    }

    [Fact]
    public async Task Without_a_typed_number_the_desks_automatic_numbering_is_unchanged()
    {
        await using var rig = await StartAsync();
        await rig.GoOfflineAsync();
        var booking = await UnpaidVisitAsync(rig, "Auto");
        Assert.True((await rig.Get<IStaffPaymentsService>().RecordAsync(await InputAsync(rig, booking, null))).Succeeded);
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        Assert.StartsWith("OR-", (await db.Payments.SingleAsync(p => p.BookingId == booking)).OrNumber);
    }

    [Fact]
    public async Task Online_the_typed_number_goes_to_the_server_as_typed_and_a_duplicate_answer_saves_nothing_here()
    {
        await using var rig = await StartAsync();
        await rig.GoOfflineAsync();
        var booking = await UnpaidVisitAsync(rig, "Online");
        await rig.Get<ClinicNode.Core.Sync.IWorkModeService>().SetAsync(ClinicNode.Core.Sync.WorkMode.Online);
        var paymentId = await WithDb(rig, db => db.Payments.Where(p => p.BookingId == booking).Select(p => p.PaymentId).SingleAsync());
        var due = await WithDb(rig, db => db.Payments.Where(p => p.BookingId == booking).Select(p => p.Amount).SingleAsync());
        var duplicate = false;
        rig.Http.Route = r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path is "/health" or "/health/ready") return FakeHandler.Status(HttpStatusCode.OK);
            if (path == $"/api/payments/booking/{booking}") return FakeHandler.Json(new { payment_id = paymentId, booking_id = booking, amount = due, status = "Unpaid" });
            if (path == $"/api/payments/{paymentId}/confirm")
                return duplicate ? FakeHandler.Text("""{"error":"or_number_duplicate","message":"used","or_number":"BK-9"}""", HttpStatusCode.Conflict) : FakeHandler.Json(new { ok = true });
            return FakeHandler.Status(HttpStatusCode.NotFound);
        };
        var svc = rig.Get<IStaffPaymentsService>();

        duplicate = true;
        var refused = await svc.RecordAsync(await InputAsync(rig, booking, "BK-9"));
        Assert.False(refused.Succeeded);
        Assert.Equal("Unpaid", (await WithDb(rig, db => db.Payments.Where(p => p.BookingId == booking).Select(p => p.Status.ToString()).SingleAsync())));

        duplicate = false;
        Assert.True((await svc.RecordAsync(await InputAsync(rig, booking, " BK-10 "))).Succeeded);
        var post = rig.Http.Requests.Last(x => x.PathAndQuery.EndsWith("/confirm") && x.Method == HttpMethod.Post);
        Assert.Equal("BK-10", JsonDocument.Parse(post.Body!).RootElement.GetProperty("or_number").GetString());
        Assert.DoesNotContain(rig.Http.Requests, x => x.PathAndQuery.Contains("or-number") || x.PathAndQuery.Contains("allocate"));
    }

    //JJQR - AI (Nov 25, 2026) - owner ("just trap the 2nd transaction"): the doctor's Mark received got there first; the server answers 409 not_unpaid
    [Fact]
    public async Task Online_a_payment_someone_just_recorded_shows_the_servers_sentence_and_changes_nothing_here()
    {
        await using var rig = await StartAsync();
        await rig.GoOfflineAsync();
        var booking = await UnpaidVisitAsync(rig, "Second");
        await rig.Get<ClinicNode.Core.Sync.IWorkModeService>().SetAsync(ClinicNode.Core.Sync.WorkMode.Online);
        var paymentId = await WithDb(rig, db => db.Payments.Where(p => p.BookingId == booking).Select(p => p.PaymentId).SingleAsync());
        var due = await WithDb(rig, db => db.Payments.Where(p => p.BookingId == booking).Select(p => p.Amount).SingleAsync());
        rig.Http.Route = r =>
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path is "/health" or "/health/ready") return FakeHandler.Status(HttpStatusCode.OK);
            if (path == $"/api/payments/booking/{booking}") return FakeHandler.Json(new { payment_id = paymentId, booking_id = booking, amount = due, status = "Unpaid" });
            if (path == $"/api/payments/{paymentId}/confirm")
                return FakeHandler.Text("""{"error":"not_unpaid","message":"This visit is no longer unpaid (it was just recorded)."}""", HttpStatusCode.Conflict);
            return FakeHandler.Status(HttpStatusCode.NotFound);
        };
        var refused = await rig.Get<IStaffPaymentsService>().RecordAsync(await InputAsync(rig, booking, "BK-11"));
        Assert.False(refused.Succeeded);
        Assert.Equal(ApiStaffPaymentsService.NoLongerUnpaidMessage, refused.Error);
        Assert.False(PaymentOutcome.TryGetOrClash(refused, out _)); // not an OR clash: no OR field appears
        var local = await WithDb(rig, db => db.Payments.AsNoTracking().SingleAsync(p => p.BookingId == booking));
        Assert.Equal(PaymentStatus.Unpaid, local.Status);
        Assert.Single(rig.Http.Requests, x => x.PathAndQuery.EndsWith("/confirm") && x.Method == HttpMethod.Post); // no local fallback, no retry
        Assert.True(ApiStaffPaymentsService.IsNoLongerUnpaid("""{"error":"not_unpaid"}"""));
        Assert.False(ApiStaffPaymentsService.IsNoLongerUnpaid("""{"error":"or_number_duplicate"}"""));
        Assert.False(ApiStaffPaymentsService.IsNoLongerUnpaid("not json"));
        Assert.False(ApiStaffPaymentsService.IsNoLongerUnpaid(null));
    }
    //JJQR - END

    private static async Task<T> WithDb<T>(ApiRig rig, Func<ClinicAppDbContext, Task<T>> f)
    {
        await using var db = await rig.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        return await f(db);
    }
}
//JJQR - END
