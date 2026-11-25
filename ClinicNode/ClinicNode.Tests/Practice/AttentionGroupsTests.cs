//JJQR - AI (Nov 25, 2026) - GAV-2: "Needs attention" grouped by date (same cases as web attention-groups.test.mjs)
using ClinicNode.Core.Practice;

namespace ClinicNode.Tests.PracticeTests;

public class AttentionGroupsTests
{
    static readonly DateOnly Today = new(2026, 11, 25);
    static AttentionItem I(AttentionKind k, DateOnly? d, Guid? id = null) => new(k, id ?? Guid.NewGuid(), "P", "t", "a", "s", d);

    [Fact]
    public void Today_first_then_older_newest_first_with_headers_and_counts()
    {
        var g = AttentionGroups.Group([I(AttentionKind.FinishChart, new(2026, 11, 23)), I(AttentionKind.UnpaidCompleted, null),
            I(AttentionKind.UnpaidCompleted, new(2026, 11, 24)), I(AttentionKind.FinishChart, new(2026, 11, 24)), I(AttentionKind.FollowUpDue, Today)], Today);
        Assert.Equal(["Today · Nov 25", "Nov 24", "Nov 23"], g.Select(x => x.Header));
        Assert.Equal(["2 items", "2 items", "1 item"], g.Select(x => x.CountLabel));
        Assert.True(g[0].IsToday);
    }

    [Fact]
    public void Inside_a_date_unpaid_then_charts_then_follow_ups()
    {
        var g = AttentionGroups.Group([I(AttentionKind.FollowUpDue, Today), I(AttentionKind.FinishChart, Today), I(AttentionKind.UnpaidCompleted, Today)], Today);
        Assert.Equal([AttentionKind.UnpaidCompleted, AttentionKind.FinishChart, AttentionKind.FollowUpDue], g[0].Items.Select(i => i.Kind));
    }

    [Fact]
    public void Earlier_day_stays_under_its_date_and_future_too_old_and_repeats_are_dropped()
    {
        Assert.Equal(new DateOnly(2026, 10, 26), AttentionGroups.From(Today));
        var dup = Guid.NewGuid();
        var g = AttentionGroups.Group([I(AttentionKind.UnpaidCompleted, new(2026, 10, 26)), I(AttentionKind.UnpaidCompleted, new(2026, 10, 25)),
            I(AttentionKind.UnpaidCompleted, new(2026, 11, 26)), I(AttentionKind.FinishChart, Today, dup), I(AttentionKind.FinishChart, Today, dup),
            I(AttentionKind.UnpaidCompleted, Today, dup)], Today);
        Assert.Equal([(Today, 2), (new DateOnly(2026, 10, 26), 1)], g.Select(x => (x.Date, x.Items.Count)));
    }

    [Fact]
    public void Today_open_older_closed_and_the_doctors_toggle_wins()
    {
        var g = AttentionGroups.Group([I(AttentionKind.UnpaidCompleted, Today), I(AttentionKind.UnpaidCompleted, new(2026, 11, 24))], Today);
        var none = new Dictionary<DateOnly, bool>();
        Assert.True(AttentionGroups.IsOpen(g[0], none));
        Assert.False(AttentionGroups.IsOpen(g[1], none));
        Assert.True(AttentionGroups.IsOpen(g[1], new Dictionary<DateOnly, bool> { [new(2026, 11, 24)] = true }));
        Assert.False(AttentionGroups.IsOpen(g[0], new Dictionary<DateOnly, bool> { [Today] = false }));
    }

    [Fact]
    public void Another_year_shows_the_year_and_item_counts()
    {
        Assert.Equal("Dec 31, 2025", AttentionGroups.ShortDate(new(2025, 12, 31), new(2026, 1, 2)));
        Assert.Equal("Jan 2", AttentionGroups.ShortDate(new(2026, 1, 2), new(2026, 1, 2)));
        Assert.Equal("1 item", AttentionGroups.ItemsLabel(1));
        Assert.Equal("3 items", AttentionGroups.ItemsLabel(3));
    }

    //JJQR - AI (Nov 25, 2026) - doctor "Mark received" wording (same as web)
    [Fact]
    public void Mark_received_question_names_the_exact_peso_amount()
    {
        Assert.Equal("Mark ₱450 received in cash?", AttentionGroups.MarkReceivedQuestion(450m));
        Assert.Equal("₱1,500", AttentionGroups.Peso(1500m));
        Assert.Equal("₱450.50", AttentionGroups.Peso(450.5m));
    }
    //JJQR - END
}
//JJQR - END

//JJQR - AI (Nov 25, 2026) - GAV-2: the Local dashboard keeps an earlier day's unpaid completed visit under its own date (inside the 30-day window only)
public class DoctorDashboardOlderDaysTests
{
    [Fact]
    public async Task Unpaid_completed_visit_from_an_earlier_day_stays_listed_under_its_date()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var doctorId = session.DoctorId!.Value;
        var patients = host.Get<ClinicNode.Core.Patients.IPatientService>();
        var queue = host.Get<ClinicNode.Core.Queue.IQueueService>();
        var today = ClinicApp.Domain.ClinicClock.Today;

        async Task<Guid> PastVisit(string first, int daysAgo)
        {
            var p = await patients.CreateAsync(new ClinicNode.Core.Patients.NewPatientRequest(first, "Older", new DateOnly(1990, 1, 1), ClinicApp.Domain.Enums.SexType.Female));
            var w = await queue.CheckInWalkInAsync(new ClinicNode.Core.Queue.WalkInRequest(p.PatientId, doctorId));
            Assert.True(w.Succeeded, w.Error);
            var factory = host.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>();
            await using var db = await factory.CreateDbContextAsync();
            var b = await db.Bookings.FindAsync(w.BookingId!.Value);
            b!.AppointmentDate = today.AddDays(-daysAgo);
            b.Status = ClinicApp.Domain.Enums.BookingStatus.Completed;
            b.AmountDue = 450m;
            await db.SaveChangesAsync();
            return b.BookingId;
        }
        var yesterday = await PastVisit("Yesterday", 1);
        var tooOld = await PastVisit("TooOld", AttentionGroups.DaysBack + 5);

        var d = await host.Get<IDoctorDashboardService>().GetAsync(doctorId);
        var item = Assert.Single(d.Unpaid, a => a.BookingId == yesterday);
        Assert.Equal(today.AddDays(-1), item.Date);
        Assert.DoesNotContain(d.Unpaid, a => a.BookingId == tooOld);
        var groups = AttentionGroups.Group(d.Unpaid.Concat(d.Charts).Concat(d.FollowUps), today);
        Assert.Contains(groups, g => g.Date == today.AddDays(-1) && !g.IsToday && g.Items.Any(i => i.BookingId == yesterday));
    }

    //JJQR - AI (Nov 25, 2026) - doctor "Mark received" (Local / Offline copy)
    [Fact]
    public async Task Mark_received_records_cash_for_the_exact_amount_only_for_the_own_unpaid_visit()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var doctorId = session.DoctorId!.Value;
        var patients = host.Get<ClinicNode.Core.Patients.IPatientService>();
        var queue = host.Get<ClinicNode.Core.Queue.IQueueService>();
        var dash = host.Get<IDoctorDashboardService>();
        var factory = host.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>();
        var today = ClinicApp.Domain.ClinicClock.Today;

        var p = await patients.CreateAsync(new ClinicNode.Core.Patients.NewPatientRequest("Cash", "Visit", new DateOnly(1990, 1, 1), ClinicApp.Domain.Enums.SexType.Female));
        var w = await queue.CheckInWalkInAsync(new ClinicNode.Core.Queue.WalkInRequest(p.PatientId, doctorId));
        Assert.True(w.Succeeded, w.Error);
        var bookingId = w.BookingId!.Value;
        await using (var db = await factory.CreateDbContextAsync())
        {
            var b = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
                Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.Include(db.Bookings, x => x.Payment), x => x.BookingId == bookingId);
            b.AppointmentDate = today.AddDays(-1);
            b.Status = ClinicApp.Domain.Enums.BookingStatus.Completed;
            b.AmountDue = 450m;
            if (b.Payment is null)
                db.Payments.Add(new ClinicApp.Domain.Entities.Payment { PaymentId = Guid.NewGuid(), BookingId = bookingId, Amount = 450m, Status = ClinicApp.Domain.Enums.PaymentStatus.Unpaid, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            else { b.Payment.Amount = 450m; b.Payment.Status = ClinicApp.Domain.Enums.PaymentStatus.Unpaid; }
            await db.SaveChangesAsync();
        }

        Assert.False((await dash.MarkReceivedAsync(Guid.NewGuid(), bookingId, 450m)).Succeeded);   // another doctor
        var wrong = await dash.MarkReceivedAsync(doctorId, bookingId, 500m);                         // amount changed
        Assert.False(wrong.Succeeded);
        Assert.Contains("₱450", wrong.Error);
        Assert.Contains((await dash.GetAsync(doctorId)).Unpaid, a => a.BookingId == bookingId && a.Amount == 450m);

        var ok = await dash.MarkReceivedAsync(doctorId, bookingId, 450m);
        Assert.True(ok.Succeeded, ok.Error);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var pay = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Payments, x => x.BookingId == bookingId);
            Assert.Equal(ClinicApp.Domain.Enums.PaymentStatus.Paid, pay.Status);
            Assert.Equal(ClinicApp.Domain.Enums.PaymentMethod.Cash, pay.PaymentMethod);
            Assert.Equal(450m, pay.AmountReceived);
            Assert.Null(pay.OrNumber);
            Assert.Equal(session.UserId, pay.ConfirmedByUserId);
        }
        Assert.DoesNotContain((await dash.GetAsync(doctorId)).Unpaid, a => a.BookingId == bookingId);
        Assert.False((await dash.MarkReceivedAsync(doctorId, bookingId, 450m)).Succeeded);           // second tap: refused, nothing changes

        //JJQR - AI (Nov 25, 2026) - owner (no local audit row is fine "as long as it is synced"): the Mark received is queued for sync as ONE payment "confirm",
        // and the snapshot that is pushed (built by the backend's own SyncDocuments) carries what the cloud needs to write its audit row on sync:
        // Paid, Cash, the doctor's user, and the Mark received note (the cloud's SyncPushEngine.DoctorMarkedReceivedNote has the same text).
        await using (var db = await factory.CreateDbContextAsync())
        {
            var pending = await host.Get<ClinicNode.Data.ISyncOutbox>().ListPendingAsync(db, 500);
            var payId = (await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Payments, x => x.BookingId == bookingId)).PaymentId;
            var op = Assert.Single(pending, e => e.Aggregate == "payment" && e.EntityId == payId && e.OperationType == "confirm");
            var data = await ClinicNode.Sync.Local.AggregateDocuments.BuildSnapshotAsync(db, op);
            Assert.NotNull(data);
            Assert.Equal("Paid", data!["status"]?.GetValue<string>());
            Assert.Equal("Cash", data["payment_method"]?.GetValue<string>());
            Assert.Equal(450m, data["amount_received"]!.GetValue<decimal>());
            Assert.Equal(session.UserId.ToString(), data["confirmed_by_user_id"]?.GetValue<string>(), ignoreCase: true);
            Assert.Equal("Cash received by the doctor (marked on the doctor dashboard).", data["confirm_notes"]?.GetValue<string>());
            Assert.Equal(DoctorDashboardService.MarkReceivedNote, data["confirm_notes"]?.GetValue<string>());
        }
        //JJQR - END
    }
    //JJQR - END
}
//JJQR - END
