//JJQR - AI (Nov 21, 2026) - doctor self-edit write path (SYNC-API-CONTRACT §5.7): local write + outbox op shape + actor + validation.
using System.Text.Json.Nodes;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Sync;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.DoctorExtra;

sealed class SelfEditSyncProbe : ISyncBridge
{
    public int Calls;
    public void RequestSync() => Calls++;
    public Task<bool> IsRevokedAsync(CancellationToken ct = default) => Task.FromResult(false);
}

public class DoctorSelfEditServiceTests
{
    private sealed record Ctx(TestHost Host, DoctorSelfEditService Svc, SelfEditSyncProbe Sync, NodeSession Session, Guid DoctorId, Guid OtherDoctorId) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Host.DisposeAsync();
        public async Task<ClinicAppDbContext> Db() => await Host.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();

        /// <summary>Pending outbox ops of one aggregate, oldest first, with the payload parsed.</summary>
        public async Task<List<(SyncOutboxEntry Entry, JsonObject Data)>> Ops(string aggregate)
        {
            await using var db = await Db();
            var all = await Host.Get<ISyncOutbox>().ListPendingAsync(db, 1000);
            return all.Where(e => e.Aggregate == aggregate).OrderBy(e => e.CreatedAt).Select(e => (e, (JsonObject)JsonNode.Parse(e.PayloadJson)!)).ToList();
        }
    }

    private static async Task<Ctx> StartAsync(Func<TestHost, Task<NodeSession>>? login = null)
    {
        var host = await TestHost.StartAsync();
        var session = login is null ? await host.LoginDoctorAsync() : await login(host);
        var probe = new SelfEditSyncProbe();
        var svc = ActivatorUtilities.CreateInstance<DoctorSelfEditService>(host.Services, probe);
        await using var db = await host.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
        var mine = (await db.Doctors.Include(d => d.StaffAccount).AsNoTracking().FirstAsync(d => d.StaffAccount!.Email == "doctor@clinic.test")).DoctorId;
        var other = (await db.Doctors.Include(d => d.StaffAccount).AsNoTracking().FirstAsync(d => d.StaffAccount!.Email == "phase5doctor@clinic.test")).DoctorId;
        return new Ctx(host, svc, probe, session, mine, other);
    }

    private static List<string> Keys(JsonNode? n) => ((JsonObject)n!).Select(k => k.Key).OrderBy(k => k).ToList();

    // ── profile ──────────────────────────────────────────────────────────
    [Fact]
    public async Task Profile_save_writes_locally_and_queues_one_doctor_update_with_only_the_changed_fields()
    {
        await using var c = await StartAsync();
        var before = await (await c.Db()).Doctors.Include(d => d.StaffAccount).AsNoTracking().SingleAsync(d => d.DoctorId == c.DoctorId);
        var r = await c.Svc.SaveProfileAsync(c.DoctorId, new DoctorSelfProfileEdit(before.StaffAccount!.FullName, "Pediatrics", "New bio", "0917 000 1111"));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(1, c.Sync.Calls);

        var after = await (await c.Db()).Doctors.Include(d => d.StaffAccount).AsNoTracking().SingleAsync(d => d.DoctorId == c.DoctorId);
        Assert.Equal("Pediatrics", after.Specialization);
        Assert.Equal("New bio", after.Bio);
        Assert.Equal("0917 000 1111", after.StaffAccount!.ContactNumber);
        Assert.Equal(before.StaffAccount.FullName, after.StaffAccount.FullName);
        Assert.Equal(before.ConsultationFee, after.ConsultationFee); // the fee is never touched here

        var op = Assert.Single(await c.Ops("doctor"));
        Assert.Equal("update", op.Entry.OperationType);
        Assert.Equal(c.DoctorId, op.Entry.EntityId);
        var d = op.Data;
        Assert.Equal(c.DoctorId.ToString(), (string?)d["doctor_id"]);
        Assert.Equal(c.Session.UserId.ToString(), (string?)d["actor_user_id"]);
        Assert.Equal("Pediatrics", (string?)d["specialization"]);
        Assert.Equal("New bio", (string?)d["bio"]);
        Assert.Equal("0917 000 1111", (string?)d["staff_account"]!["contact_number"]);
        Assert.Null(d["staff_account"]!["full_name"]);          // unchanged → not sent
        Assert.Null(d["consultation_fee"]); Assert.Null(d["license_number"]); // cloud-owned, never sent
        Assert.Equal(["bio", "specialization", "staff.contact_number"], Keys(d["field_updated_at"]));
        Assert.Equal(["bio", "specialization", "staff.contact_number"], d["changed_fields"]!.AsArray().Select(x => (string)x!).OrderBy(x => x).ToList());
        Assert.All(((JsonObject)d["field_updated_at"]!).Select(k => (string)k.Value!), s => Assert.True(DateTimeOffset.TryParse(s, out var t) && Math.Abs((DateTimeOffset.UtcNow - t).TotalMinutes) < 2));
        Assert.True(DateTimeOffset.TryParse((string?)d["updated_at"], out _));
    }

    [Fact]
    public async Task Profile_save_with_nothing_changed_queues_nothing_and_does_not_request_a_sync()
    {
        await using var c = await StartAsync();
        var cur = await (await c.Db()).Doctors.Include(d => d.StaffAccount).AsNoTracking().SingleAsync(d => d.DoctorId == c.DoctorId);
        var r = await c.Svc.SaveProfileAsync(c.DoctorId, new DoctorSelfProfileEdit(cur.StaffAccount!.FullName, cur.Specialization, cur.Bio, cur.StaffAccount.ContactNumber));
        Assert.True(r.Ok);
        Assert.Empty(await c.Ops("doctor"));
        Assert.Equal(0, c.Sync.Calls);
    }

    [Fact]
    public async Task Profile_bio_can_be_cleared_and_that_is_sent_as_null()
    {
        await using var c = await StartAsync();
        Assert.True((await c.Svc.SaveProfileAsync(c.DoctorId, Edit(await Current(c), bio: "something"))).Ok);
        Assert.True((await c.Svc.SaveProfileAsync(c.DoctorId, Edit(await Current(c), bio: "  "))).Ok);
        var ops = await c.Ops("doctor");
        Assert.Equal(2, ops.Count);
        Assert.True(ops[1].Data.ContainsKey("bio"));
        Assert.Null(ops[1].Data["bio"]);
        Assert.Null((await (await c.Db()).Doctors.AsNoTracking().SingleAsync(d => d.DoctorId == c.DoctorId)).Bio);
    }

    private static async Task<Doctor> Current(Ctx c) => await (await c.Db()).Doctors.Include(d => d.StaffAccount).AsNoTracking().SingleAsync(d => d.DoctorId == c.DoctorId);
    private static DoctorSelfProfileEdit Edit(Doctor d, string? name = null, string? spec = null, string? bio = "keep", string? contact = "keep") =>
        new(name ?? d.StaffAccount!.FullName, spec ?? d.Specialization, bio == "keep" ? d.Bio : bio, contact == "keep" ? d.StaffAccount!.ContactNumber : contact);

    [Fact]
    public async Task Profile_validation_rejects_bad_input_without_writing_or_queueing()
    {
        await using var c = await StartAsync();
        var cur = await Current(c);
        Assert.False((await c.Svc.SaveProfileAsync(c.DoctorId, Edit(cur, spec: " "))).Ok);
        Assert.False((await c.Svc.SaveProfileAsync(c.DoctorId, Edit(cur, spec: new string('s', 101)))).Ok);
        Assert.False((await c.Svc.SaveProfileAsync(c.DoctorId, Edit(cur, name: " "))).Ok);
        Assert.False((await c.Svc.SaveProfileAsync(c.DoctorId, Edit(cur, name: new string('n', 201)))).Ok);
        Assert.False((await c.Svc.SaveProfileAsync(c.DoctorId, Edit(cur, bio: new string('b', 2001)))).Ok);
        Assert.False((await c.Svc.SaveProfileAsync(c.DoctorId, Edit(cur, contact: new string('9', 51)))).Ok);
        var spec = await c.Svc.SaveProfileAsync(c.DoctorId, Edit(cur, spec: ""));
        Assert.Equal("Specialization is required.", spec.Error);
        Assert.Empty(await c.Ops("doctor"));
        Assert.Equal(0, c.Sync.Calls);
        Assert.Equal(cur.Specialization, (await Current(c)).Specialization);
        // exactly at the limit is fine
        Assert.True((await c.Svc.SaveProfileAsync(c.DoctorId, Edit(cur, spec: new string('s', 100)))).Ok);
    }

    [Fact]
    public async Task Profile_actor_rules_doctor_for_self_only_admin_for_anyone_staff_never()
    {
        await using (var c = await StartAsync())
        {
            var cur = await Current(c);
            var other = await c.Svc.SaveProfileAsync(c.OtherDoctorId, new DoctorSelfProfileEdit("Hacked", "Hacked", null, null));
            Assert.False(other.Ok); Assert.Equal(DoctorSelfEditService.NotYours, other.Error);
            Assert.Empty(await c.Ops("doctor"));
        }
        await using (var c = await StartAsync(h => h.LoginStaffAsync()))
        {
            var r = await c.Svc.SaveProfileAsync(c.DoctorId, new DoctorSelfProfileEdit("X", "X", null, null));
            Assert.False(r.Ok); Assert.Equal(DoctorSelfEditService.NotYours, r.Error);
            Assert.Empty(await c.Ops("doctor"));
        }
        await using (var c = await StartAsync(h => h.LoginAdminAsync()))
        {
            var cur = await Current(c);
            var r = await c.Svc.SaveProfileAsync(c.DoctorId, Edit(cur, spec: "By admin"));
            Assert.True(r.Ok, r.Error);
            var op = Assert.Single(await c.Ops("doctor"));
            Assert.Equal(c.Session.UserId.ToString(), (string?)op.Data["actor_user_id"]); // the admin, not the doctor
        }
        await using (var c = await StartAsync())
        {
            await c.Host.Get<IAuthService>().LogoutAsync();
            var r = await c.Svc.SaveProfileAsync(c.DoctorId, new DoctorSelfProfileEdit("X", "X", null, null));
            Assert.False(r.Ok); Assert.Equal(DoctorSelfEditService.SessionEnded, r.Error);
        }
    }

    // ── weekly schedule ──────────────────────────────────────────────────
    private static DoctorScheduleDayEdit Day(int dow, bool active, string start, string end) => new((short)dow, active, TimeOnly.Parse(start), TimeOnly.Parse(end));

    [Fact]
    public async Task Weekly_schedule_creates_rows_for_new_weekdays_and_updates_only_what_changed()
    {
        await using var c = await StartAsync();
        // wipe the demo rows so Monday / Tuesday are new weekdays
        await using (var db = await c.Db()) { db.DoctorSchedules.RemoveRange(db.DoctorSchedules.Where(s => s.DoctorId == c.DoctorId)); await db.SaveChangesAsync(); }

        var first = await c.Svc.SaveWeeklyScheduleAsync(c.DoctorId, [Day(1, true, "09:00", "12:00"), Day(2, false, "00:00", "00:00")]);
        Assert.True(first.Ok, first.Error);
        var created = await c.Ops("doctor_schedule");
        Assert.Equal(2, created.Count);
        Assert.All(created, o => Assert.Equal("create", o.Entry.OperationType));
        var monday = created.Single(o => (int)o.Data["day_of_week"]! == 1);
        Assert.Equal(c.DoctorId.ToString(), (string?)monday.Data["doctor_id"]);
        Assert.Equal(monday.Entry.EntityId.ToString(), (string?)monday.Data["id"]);
        Assert.Equal("09:00:00", (string?)monday.Data["start_time"]);
        Assert.Equal("12:00:00", (string?)monday.Data["end_time"]);
        Assert.True((bool)monday.Data["is_active"]!);
        Assert.Equal(c.Session.UserId.ToString(), (string?)monday.Data["actor_user_id"]);
        Assert.Equal(["end_time", "is_active", "start_time"], Keys(monday.Data["field_updated_at"]));
        Assert.Equal(1, c.Sync.Calls);

        // second save: Monday end time changes, Tuesday identical → exactly one new op, update, only end_time
        var second = await c.Svc.SaveWeeklyScheduleAsync(c.DoctorId, [Day(1, true, "09:00", "13:00"), Day(2, false, "00:00", "00:00")]);
        Assert.True(second.Ok, second.Error);
        var all = await c.Ops("doctor_schedule");
        Assert.Equal(3, all.Count);
        var upd = all.Last();
        Assert.Equal("update", upd.Entry.OperationType);
        Assert.Equal(monday.Entry.EntityId, upd.Entry.EntityId); // same row id, one row per weekday
        Assert.Equal("13:00:00", (string?)upd.Data["end_time"]);
        Assert.Null(upd.Data["start_time"]);
        Assert.Null(upd.Data["is_active"]);
        Assert.Equal(["end_time"], Keys(upd.Data["field_updated_at"]));
        Assert.Equal(1, upd.Data["day_of_week"]!.GetValue<int>());
        Assert.Equal(2, c.Sync.Calls);

        await using var db2 = await c.Db();
        var rows = await db2.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == c.DoctorId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new TimeOnly(13, 0), rows.Single(r => r.DayOfWeek == 1).EndTime);

        // identical third save: nothing queued, no extra sync request
        Assert.True((await c.Svc.SaveWeeklyScheduleAsync(c.DoctorId, [Day(1, true, "09:00", "13:00")])).Ok);
        Assert.Equal(3, (await c.Ops("doctor_schedule")).Count);
        Assert.Equal(2, c.Sync.Calls);
    }

    [Fact]
    public async Task Weekly_schedule_validation_and_actor_rules()
    {
        await using var c = await StartAsync();
        var before = (await c.Ops("doctor_schedule")).Count;
        var bad = await c.Svc.SaveWeeklyScheduleAsync(c.DoctorId, [Day(1, true, "17:00", "08:00")]);
        Assert.False(bad.Ok); Assert.Contains("Monday", bad.Error);
        Assert.False((await c.Svc.SaveWeeklyScheduleAsync(c.DoctorId, [Day(1, true, "09:00", "09:00")])).Ok);   // start must be < end on an active day
        Assert.True((await c.Svc.SaveWeeklyScheduleAsync(c.DoctorId, [Day(0, false, "17:00", "08:00")])).Ok);    // an inactive day may have any times
        Assert.False((await c.Svc.SaveWeeklyScheduleAsync(c.DoctorId, [])).Ok);
        Assert.False((await c.Svc.SaveWeeklyScheduleAsync(c.DoctorId, [Day(7, true, "09:00", "12:00")])).Ok);
        Assert.False((await c.Svc.SaveWeeklyScheduleAsync(c.DoctorId, [Day(1, true, "09:00", "12:00"), Day(1, true, "10:00", "12:00")])).Ok);
        var other = await c.Svc.SaveWeeklyScheduleAsync(c.OtherDoctorId, [Day(1, true, "09:00", "12:00")]);
        Assert.False(other.Ok); Assert.Equal(DoctorSelfEditService.NotYours, other.Error);
        // none of the rejected saves queued anything for the other doctor; the only accepted save (Sunday, inactive) is the doctor's own
        var queued = await c.Ops("doctor_schedule");
        Assert.All(queued, o => Assert.Equal(c.DoctorId.ToString(), (string?)o.Data["doctor_id"]));
        Assert.True(queued.Count <= before + 1);
    }

    // ── blocked dates ────────────────────────────────────────────────────
    [Fact]
    public async Task Blocked_date_add_update_reason_and_remove_queue_create_update_delete()
    {
        await using var c = await StartAsync();
        var day = new DateOnly(2027, 3, 14);
        var add = await c.Svc.AddBlockedDateAsync(c.DoctorId, day, "  Conference ");
        Assert.True(add.Ok, add.Error);
        var id = add.Id!.Value;
        var row = await (await c.Db()).DoctorBlockedDates.AsNoTracking().SingleAsync(b => b.Id == id);
        Assert.Equal("Conference", row.Reason); Assert.Equal(day, row.BlockedDate); Assert.Equal(c.DoctorId, row.DoctorId);

        var ops = await c.Ops("doctor_blocked_date");
        var create = Assert.Single(ops);
        Assert.Equal("create", create.Entry.OperationType); Assert.Equal(id, create.Entry.EntityId);
        Assert.Equal(id.ToString(), (string?)create.Data["id"]);
        Assert.Equal(c.DoctorId.ToString(), (string?)create.Data["doctor_id"]);
        Assert.Equal("2027-03-14", (string?)create.Data["blocked_date"]);
        Assert.Equal("Conference", (string?)create.Data["reason"]);
        Assert.Equal(c.Session.UserId.ToString(), (string?)create.Data["actor_user_id"]);
        Assert.Equal(["reason"], Keys(create.Data["field_updated_at"]));

        var upd = await c.Svc.UpdateBlockedDateReasonAsync(id, "Conference (moved)");
        Assert.True(upd.Ok, upd.Error);
        Assert.Equal("Conference (moved)", (await (await c.Db()).DoctorBlockedDates.AsNoTracking().SingleAsync(b => b.Id == id)).Reason);
        var u = (await c.Ops("doctor_blocked_date")).Last();
        Assert.Equal("update", u.Entry.OperationType);
        Assert.Equal("Conference (moved)", (string?)u.Data["reason"]);
        Assert.Equal(["reason"], Keys(u.Data["field_updated_at"]));

        // same reason again: no new op
        Assert.True((await c.Svc.UpdateBlockedDateReasonAsync(id, "Conference (moved)")).Ok);
        Assert.Equal(2, (await c.Ops("doctor_blocked_date")).Count);

        var del = await c.Svc.RemoveBlockedDateAsync(id);
        Assert.True(del.Ok, del.Error);
        Assert.False(await (await c.Db()).DoctorBlockedDates.AnyAsync(b => b.Id == id));
        var d = (await c.Ops("doctor_blocked_date")).Last();
        Assert.Equal("delete", d.Entry.OperationType); Assert.Equal(id, d.Entry.EntityId);
        Assert.Equal(c.Session.UserId.ToString(), (string?)d.Data["actor_user_id"]);
        Assert.Equal(3, c.Sync.Calls);
    }

    [Fact]
    public async Task Blocked_date_validation_duplicates_and_other_doctors_rows()
    {
        await using var c = await StartAsync();
        var day = new DateOnly(2027, 4, 1);
        Assert.False((await c.Svc.AddBlockedDateAsync(c.DoctorId, default, null)).Ok);
        Assert.False((await c.Svc.AddBlockedDateAsync(c.DoctorId, day, new string('r', 501))).Ok);
        var first = await c.Svc.AddBlockedDateAsync(c.DoctorId, day, null);
        Assert.True(first.Ok, first.Error);
        var dup = await c.Svc.AddBlockedDateAsync(c.DoctorId, day, "again");
        Assert.False(dup.Ok); Assert.Equal("That date is already blocked.", dup.Error);
        Assert.False((await c.Svc.AddBlockedDateAsync(c.OtherDoctorId, day, null)).Ok);
        Assert.False((await c.Svc.UpdateBlockedDateReasonAsync(first.Id!.Value, new string('r', 501))).Ok);
        Assert.False((await c.Svc.UpdateBlockedDateReasonAsync(Guid.NewGuid(), "x")).Ok);
        Assert.False((await c.Svc.RemoveBlockedDateAsync(Guid.NewGuid())).Ok);
        Assert.Single(await c.Ops("doctor_blocked_date")); // only the first create was queued

        // another doctor's blocked date can be neither edited nor removed
        var foreign = Guid.NewGuid();
        await using (var db = await c.Db())
        {
            db.DoctorBlockedDates.Add(new DoctorBlockedDate { Id = foreign, DoctorId = c.OtherDoctorId, BlockedDate = new DateOnly(2027, 5, 5), Reason = "theirs", CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var u = await c.Svc.UpdateBlockedDateReasonAsync(foreign, "mine now");
        Assert.False(u.Ok); Assert.Equal(DoctorSelfEditService.NotYours, u.Error);
        var r = await c.Svc.RemoveBlockedDateAsync(foreign);
        Assert.False(r.Ok); Assert.Equal(DoctorSelfEditService.NotYours, r.Error);
        Assert.True(await (await c.Db()).DoctorBlockedDates.AnyAsync(b => b.Id == foreign && b.Reason == "theirs"));
    }

    [Fact]
    public async Task Admin_session_may_edit_a_doctors_blocked_dates_with_the_admin_as_actor()
    {
        await using var c = await StartAsync(h => h.LoginAdminAsync());
        var r = await c.Svc.AddBlockedDateAsync(c.DoctorId, new DateOnly(2027, 6, 1), "Clinic closed");
        Assert.True(r.Ok, r.Error);
        var op = Assert.Single(await c.Ops("doctor_blocked_date"));
        Assert.Equal(c.Session.UserId.ToString(), (string?)op.Data["actor_user_id"]);
    }

    [Fact]
    public void Di_registers_the_service()
    {
        var sc = new ServiceCollection();
        sc.AddClinicNodeDoctorSelfEdit();
        Assert.Contains(sc, d => d.ServiceType == typeof(IDoctorSelfEditService) && d.ImplementationType == typeof(DoctorSelfEditService));
    }
}
//JJQR - END
