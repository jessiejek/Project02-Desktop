//JJQR - AI (Nov 21, 2026) - per-node daily official-receipt sequence (OR-<nodePrefix>-<yyyymmdd>-<seq>).
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Staff;

public class StaffOrSequenceTests
{
    static async Task<(string Prefix, string Number)> RecordAsync(TestHost host, WalkInTicket t)
    {
        await using var db = await host.DbAsync();
        var nodeId = (await SyncOutbox.GetOrCreateIdentityAsync(db)).NodeId;
        var prefix = StaffRules.OrDayPrefix(StaffRules.NodePrefixFrom(nodeId), ClinicClock.Today);
        var pay = host.Make<StaffPaymentsService>();
        var r = await pay.RecordAsync(new RecordPaymentInput(t.BookingId, PaymentMethod.Cash, t.ProvisionalFee.ToString(System.Globalization.CultureInfo.InvariantCulture), null, null));
        Assert.True(r.Succeeded, r.Error);
        var receipt = await pay.GetReceiptAsync(t.BookingId);
        Assert.NotNull(receipt?.OrNumber);
        return (prefix, receipt!.OrNumber!);
    }

    [Fact]
    public async Task Record_assigns_or_format_from_node_identity_and_increments()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var (prefix, a) = await RecordAsync(host, await host.CompletedVisitAsync("Ora"));
        var (_, b) = await RecordAsync(host, await host.CompletedVisitAsync("Orb"));
        Assert.Equal(prefix + "0001", a);
        Assert.Equal(prefix + "0002", b);
        Assert.StartsWith("OR-", a);
        await using var db = await host.DbAsync();
        var nodeId = (await SyncOutbox.GetOrCreateIdentityAsync(db)).NodeId;
        Assert.Equal(StaffRules.FormatOrNumber(StaffRules.NodePrefixFrom(nodeId), ClinicClock.Today, 1), a);
        Assert.Equal(4, StaffRules.NodePrefixFrom(nodeId).Length);
    }

    [Fact]
    public async Task Sequence_resets_on_the_next_manila_day()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var t1 = await host.CompletedVisitAsync("DayOne");
        var t2 = await host.CompletedVisitAsync("DayTwo");
        await using var db = await host.DbAsync();
        var p1 = await db.Payments.SingleAsync(p => p.BookingId == t1.BookingId);
        var p2 = await db.Payments.SingleAsync(p => p.BookingId == t2.BookingId);
        var first = await StaffPaymentsService.AllocateOrNumberAsync(db, p1, new DateOnly(2026, 11, 21));
        await db.SaveChangesAsync();
        var second = await StaffPaymentsService.AllocateOrNumberAsync(db, p2, new DateOnly(2026, 11, 22));
        await db.SaveChangesAsync();
        Assert.Equal("OR-" + StaffRules.NodePrefixFrom((await SyncOutbox.GetOrCreateIdentityAsync(db)).NodeId) + "-20261121-0001", first);
        Assert.Equal("OR-" + StaffRules.NodePrefixFrom((await SyncOutbox.GetOrCreateIdentityAsync(db)).NodeId) + "-20261122-0001", second);
    }

    [Fact]
    public async Task Concurrent_records_do_not_duplicate_a_sequence()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var tickets = new WalkInTicket[8];
        for (var i = 0; i < tickets.Length; i++)
            tickets[i] = await host.CompletedVisitAsync("Conc" + i);

        var pay = host.Make<StaffPaymentsService>();
        var tasks = tickets.Select(t => pay.RecordAsync(new RecordPaymentInput(
            t.BookingId, PaymentMethod.Cash, t.ProvisionalFee.ToString(System.Globalization.CultureInfo.InvariantCulture), null, null)));
        var results = await Task.WhenAll(tasks);
        Assert.All(results, r => Assert.True(r.Succeeded, r.Error));

        var numbers = new List<string>();
        foreach (var t in tickets)
        {
            var receipt = await pay.GetReceiptAsync(t.BookingId);
            numbers.Add(receipt!.OrNumber!);
        }
        Assert.Equal(tickets.Length, numbers.Distinct().Count());
        await using var db = await host.DbAsync();
        var prefix = StaffRules.OrDayPrefix(StaffRules.NodePrefixFrom((await SyncOutbox.GetOrCreateIdentityAsync(db)).NodeId), ClinicClock.Today);
        var seqs = numbers.Select(n => StaffRules.SeqOf(n, prefix)).ToList();
        Assert.All(seqs, s => Assert.NotNull(s));
        Assert.Equal(Enumerable.Range(1, tickets.Length), seqs.Select(s => s!.Value).OrderBy(x => x));
    }

    [Fact]
    public async Task Legacy_fallback_is_used_only_when_the_row_has_no_stored_number()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var t = await host.CompletedVisitAsync("Legacy");
        await using (var db = await host.DbAsync())
        {
            var p = await db.Payments.SingleAsync(x => x.BookingId == t.BookingId);
            p.Status = PaymentStatus.Paid;
            p.OrNumber = null;
            p.ConfirmedAt = DateTimeOffset.UtcNow;
            p.AmountReceived = p.Amount;
            await db.SaveChangesAsync();
        }
        var pay = host.Make<StaffPaymentsService>();
        var receipt = await pay.GetReceiptAsync(t.BookingId);
        Assert.Equal(StaffRules.OrNumberFor(t.BookingId), receipt!.OrNumber);

        var t2 = await host.CompletedVisitAsync("Stored");
        var (_, assigned) = await RecordAsync(host, t2);
        Assert.Equal(assigned, (await pay.GetReceiptAsync(t2.BookingId))!.OrNumber);
        Assert.NotEqual(StaffRules.OrNumberFor(t2.BookingId), assigned);
    }

    [Fact]
    public async Task Allocate_is_idempotent_for_the_same_payment_on_the_same_day()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var t = await host.CompletedVisitAsync("Again");
        await using var db = await host.DbAsync();
        var p = await db.Payments.SingleAsync(x => x.BookingId == t.BookingId);
        var a = await StaffPaymentsService.AllocateOrNumberAsync(db, p);
        var b = await StaffPaymentsService.AllocateOrNumberAsync(db, p);
        Assert.Equal(a, b);
        Assert.EndsWith("-0001", a);
    }
}
//JJQR - END
