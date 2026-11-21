//JJQR - AI (Nov 21, 2026) - TEMPORARY harness: runs every staff service/VM load + primary action against a COPY of the sandbox DB (never the original).
// Run: CLINIC_COPY_DIR=/tmp/clinic-copy dotnet test ClinicNode.Tests --filter "Category=RealDb"   (report: /tmp/staff-harness-report.txt)
using System.Text;
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.App.ViewModels.Staff;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using ClinicNode.Tests;
using ClinicNode.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.Staff;

[Collection(SandboxCopyCollection.Name)]
public class StaffRealDbHarness
{
    sealed class Rec
    {
        public readonly List<string> Lines = [];
        public int Pass, Fail;
        public async Task<T?> Step<T>(string page, string what, Func<Task<T>> body)
        {
            try { var r = await body(); Pass++; Lines.Add($"PASS | {page} | {what}"); return r; }
            catch (Exception ex) { Fail++; Lines.Add($"FAIL | {page} | {what} | {ex.GetType().Name}: {ex.Message}{Inner(ex)}\n{ex.StackTrace}\n"); return default; }
        }
        static string Inner(Exception ex) { var sb = new StringBuilder(); for (var i = ex.InnerException; i is not null; i = i.InnerException) sb.Append(" <= ").Append(i.GetType().Name).Append(": ").Append(i.Message); return sb.ToString(); }
        public Task Step(string page, string what, Func<Task> body) => Step<bool>(page, what, async () => { await body(); return true; });
        public void Check(string page, string what, bool ok, string detail = "")
        {
            if (ok) { Pass++; Lines.Add($"PASS | {page} | {what}"); } else { Fail++; Lines.Add($"FAIL | {page} | {what} | {detail}"); }
        }
        public void Note(string s) => Lines.Add("NOTE | " + s);
    }

    static void Need(StaffResult r, string ctx) { if (!r.Succeeded) throw new InvalidOperationException($"{ctx}: {r.Error}"); }

    [SkippableFact, Trait("Category", "RealDb")]
    public async Task Staff_pages_against_copy_of_sandbox_db()
    {
        var src = Environment.GetEnvironmentVariable("CLINIC_COPY_DIR") ?? ""; // opt-in only: unset = skip, so the normal suite never needs the copy
        // JJQR - AI (Nov 21, 2026) - vstest treats SkipException.ForSkip as a failure; SkippableFact records a skip
        Skip.If(src.Length == 0, "CLINIC_COPY_DIR is not set; RealDb harness is env-gated.");
        Skip.If(!Directory.Exists(src), "CLINIC_COPY_DIR does not exist: " + src);
        // JJQR - END
        Assert.StartsWith("/tmp/", src); // safety: never point at the real sandbox container
        var run = Path.Combine("/tmp", "clinic-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        foreach (var f in Directory.GetFiles(src, "clinic-node.db*")) File.Copy(f, Path.Combine(run, Path.GetFileName(f)));
        if (File.Exists(Path.Combine(src, "node-secret.key"))) File.Copy(Path.Combine(src, "node-secret.key"), Path.Combine(run, "node-secret.key"));

        var rec = new Rec();
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddClinicNodeCore(new NodeOptions { DataDirectory = run, Demo = true });
        sc.AddClinicNodeSync();
        sc.AddClinicNodeStaff();
        sc.AddClinicNodeDoctorExtra();
        var sp = sc.BuildServiceProvider();
        T Get<T>() where T : notnull => sp.GetRequiredService<T>();

        var factory = Get<IDbContextFactory<ClinicAppDbContext>>();
        // 1) the app's own init path against the untouched copy, exactly as the app runs it
        var asIs = await rec.Step("startup", "LocalDatabase.InitializeAsync on the copy AS-IS (what the app does)", async () => { await Get<LocalDatabase>().InitializeAsync(); return true; });
        if (!asIs && Environment.GetEnvironmentVariable("HARNESS_APPLY_SYNC_FIX") != "0")
        {
            // 2) re-run everything else on a second copy with the sync_changes table created (suggested fix for Data/LocalDatabase.cs), to test the rest of the staff flows
            await sp.DisposeAsync();
            run = Path.Combine("/tmp", "clinic-run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run);
            foreach (var f in Directory.GetFiles(src, "clinic-node.db*")) File.Copy(f, Path.Combine(run, Path.GetFileName(f)));
            sc = new ServiceCollection();
            sc.AddLogging();
            sc.AddClinicNodeCore(new NodeOptions { DataDirectory = run, Demo = true });
            sc.AddClinicNodeSync(); sc.AddClinicNodeStaff(); sc.AddClinicNodeDoctorExtra();
            sp = sc.BuildServiceProvider();
            factory = Get<IDbContextFactory<ClinicAppDbContext>>();
            await using (var db0 = await factory.CreateDbContextAsync())
                ClinicApp.Infrastructure.Sync.SyncBootstrap.EnsureSqliteSyncSchema(db0);
            rec.Note("WORKAROUND APPLIED for the remaining steps: SyncBootstrap.EnsureSqliteSyncSchema(db) before init");
            await rec.Step("startup", "LocalDatabase.InitializeAsync with sync schema ensured first", () => Get<LocalDatabase>().InitializeAsync());
        }

        // resolve staff@clinic.test from the DB
        Guid staffUserId = default;
        await rec.Step("startup", "resolve staff@clinic.test user + staff record", async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            var s = await db.StaffAccounts.AsNoTracking().SingleAsync(x => x.Email == "staff@clinic.test");
            staffUserId = s.UserId;
            rec.Note($"staff user {s.UserId} staffId={s.StaffId} role={s.Role} status={s.Status} FullName='{s.FullName}'");
        });
        var login = await rec.Step("login", "staff@clinic.test", async () =>
        {
            var r = await Get<IAuthService>().LoginAsync("staff@clinic.test", DevDataSeeder.DevPassword, false);
            if (!r.Succeeded) throw new InvalidOperationException("login failed: " + r.Error);
            return r.Session!;
        });
        if (login is null) { Finish(rec, run); return; }

        var patients = Get<IPatientService>();
        var queue = Get<IQueueService>();

        // ── dashboard ──
        var dash = Get<IStaffDashboardService>();
        var board = await rec.Step("dashboard", "load", () => dash.GetAsync());
        if (board is not null) rec.Note($"dashboard: booked/checked-in={board.BookedOrCheckedIn} ready-for-payment={board.ReadyForPayment} patients-today={board.PatientsToday} arrivals={board.Arrivals.Count}");

        // fresh patient + walk-ins
        var stamp = DateTime.Now.ToString("HHmmss");
        Guid p1 = default, p2 = default, p3 = default;
        var wiz = new WalkInWizardViewModel(Get<IStaffPatientsService>(), queue);
        await rec.Step("walk-in wizard", "search (existing data, empty query)", () => wiz.SearchAsync(""));
        await rec.Step("walk-in wizard", "search by name 'a'", () => wiz.SearchAsync("a"));
        rec.Check("walk-in wizard", "search returns results object", wiz.Results is not null, wiz.Error ?? "");
        var spat = Get<IStaffPatientsService>();
        await rec.Step("patients", "search (blank)", async () => { var pg = await spat.SearchAsync(null); rec.Note($"patients total={pg.TotalCount}"); });
        await rec.Step("patients", "search (term)", () => spat.SearchAsync("reyes"));
        var bad = spat.ValidateQuickRegister(new QuickRegisterForm("", null, "", null, null, null, null));
        rec.Check("patients", "register validation rejects blank form", bad is not null, "no error returned");
        var reg = await rec.Step("patients", "quick register", async () =>
        {
            var (r, pt) = await spat.QuickRegisterAsync(new QuickRegisterForm("Harness" + stamp, null, "Tester", new DateOnly(1985, 3, 3), SexType.Male, "0917 555 0101", "Harness St"));
            Need(r, "quick register"); p1 = pt!.PatientId; return pt;
        });
        await rec.Step("patients", "detail (new)", async () => { var d = await spat.GetDetailAsync(p1); if (d is null) throw new InvalidOperationException("detail null"); });
        // detail of every real patient: catches data-shape crashes
        await rec.Step("patients", "detail for ALL existing patients", async () =>
        {
            var pg = await spat.SearchAsync(null, 1, 500);
            foreach (var row in pg.Items) { if (await spat.GetDetailAsync(row.PatientId) is null) throw new InvalidOperationException("detail null for " + row.PatientCode); }
            rec.Note($"patient details loaded: {pg.Items.Count}");
        });

        // walk-in wizard: select → confirm → check in
        await rec.Step("walk-in wizard", "search new patient, select, check in", async () =>
        {
            await wiz.ResetAsync();
            await wiz.SearchAsync("Harness" + stamp);
            var hit = wiz.Results!.Items.FirstOrDefault(x => x.PatientId == p1) ?? throw new InvalidOperationException("registered patient not found by search: " + wiz.Error);
            wiz.Select(hit);
            if (!await wiz.CheckInAsync()) throw new InvalidOperationException("wizard check-in failed: " + wiz.Error);
            if (wiz.Ticket is null) throw new InvalidOperationException("no ticket");
            rec.Note($"wizard ticket {wiz.Ticket.QueueNumber} booking {wiz.Ticket.BookingId} fee {wiz.Ticket.ProvisionalFee}");
        });
        var ticket1 = wiz.Ticket;

        // extra patients/walk-ins for cancel + payments
        async Task<(Guid pid, WalkInTicket t)> Walk(string first, DateOnly dob, string? disc = null)
        {
            var p = await patients.CreateAsync(new NewPatientRequest(first + stamp, "Tester", dob, SexType.Female, ContactNumber: "0917 555 0102"));
            var r = await queue.CheckInWalkInAsync(new WalkInRequest(p.PatientId, null, DiscountCategory: disc));
            if (!r.Succeeded) throw new InvalidOperationException("walk-in: " + r.Error);
            return (p.PatientId, r.Ticket!);
        }
        WalkInTicket? t2 = null, t3 = null;
        await rec.Step("walk-in wizard", "check in (senior discount) via service", async () => { var (a, t) = await Walk("Senior", new DateOnly(1950, 1, 1), "Senior"); p2 = a; t2 = t; });
        await rec.Step("walk-in wizard", "check in (regular) via service", async () => { var (a, t) = await Walk("Cancelme", new DateOnly(1995, 5, 5)); p3 = a; t3 = t; });

        // ── queue ──
        var qb = Get<IStaffQueueBoardService>();
        await rec.Step("queue", "load board", async () =>
        {
            var (s, rows) = await qb.GetAsync();
            rec.Note($"queue: waiting={s.Waiting} withDoctor={s.WithDoctor} booked={s.BookedNotArrived} completed={s.Completed} noShow={s.NoShow} total={s.Total} rows={rows.Count}");
            foreach (var r in rows) { _ = r.IsPriority; _ = r.Entry.QueueNumber; }
        });
        if (ticket1 is not null)
        {
            await rec.Step("queue", "call in (walk-in 1)", async () => Need(await qb.ActAsync(ticket1.BookingId, "call"), "call"));
            await rec.Step("queue", "complete (walk-in 1)", async () => Need(await qb.ActAsync(ticket1.BookingId, "complete"), "complete"));
        }
        if (t2 is not null) await rec.Step("queue", "hold then call (walk-in 2)", async () => { Need(await qb.ActAsync(t2.BookingId, "hold"), "hold"); Need(await qb.ActAsync(t2.BookingId, "call"), "call"); });
        if (t3 is not null)
        {
            await rec.Step("queue", "cancel needs reason", async () => { var r = await qb.CancelAsync(t3.BookingId, ""); if (r.Succeeded) throw new InvalidOperationException("blank reason accepted"); });
            await rec.Step("queue", "cancel with reason (walk-in 3)", async () => Need(await qb.CancelAsync(t3.BookingId, "Harness cancel"), "cancel"));
        }
        // real queue rows: act on an existing CheckedIn one
        await rec.Step("queue", "call in an EXISTING checked-in real row (if any)", async () =>
        {
            var (_, rows) = await qb.GetAsync();
            var real = rows.FirstOrDefault(r => r.Entry.Status == BookingStatus.CheckedIn && r.Entry.BookingId != t2?.BookingId);
            if (real is null) { rec.Note("no other checked-in rows to act on"); return; }
            Need(await qb.ActAsync(real.Entry.BookingId, "call"), "call real row");
        });

        // dashboard actions: toggle check-in on a booked row (use walk-in 3-style: new walk-in then undo)
        await rec.Step("dashboard", "toggle check-in + cancel with reason", async () =>
        {
            var (_, t) = await Walk("Toggle", new DateOnly(1992, 2, 2));
            Need(await dash.ToggleCheckInAsync(t.BookingId), "undo check-in");
            Need(await dash.ToggleCheckInAsync(t.BookingId), "check in again");
            Need(await dash.ToggleCheckInAsync(t.BookingId), "undo again");
            Need(await dash.CancelAsync(t.BookingId, "Harness cancel"), "cancel");
        });
        await rec.Step("dashboard", "reload after actions", () => dash.GetAsync());

        // ── booking detail ──
        var bk = Get<IStaffBookingService>();
        await rec.Step("booking detail", "load every booking from the dashboard + queue", async () =>
        {
            var d = await dash.GetAsync();
            foreach (var a in d.Arrivals) if (await bk.GetAsync(a.BookingId) is null) throw new InvalidOperationException("detail null " + a.BookingId);
            await using var db = await factory.CreateDbContextAsync();
            var ids = await db.Bookings.AsNoTracking().OrderByDescending(b => b.CreatedAt).Select(b => b.BookingId).Take(200).ToListAsync();
            foreach (var id in ids) if (await bk.GetAsync(id) is null) throw new InvalidOperationException("detail null " + id);
            rec.Note($"booking details loaded: {ids.Count}");
        });
        await rec.Step("booking detail", "undo check-in, check in, mark no-show, cancel rules", async () =>
        {
            var (_, t) = await Walk("Detail", new DateOnly(1993, 3, 3));
            Need(await bk.UndoCheckInAsync(t.BookingId), "undo");
            Need(await bk.CheckInAsync(t.BookingId), "check-in");
            Need(await bk.UndoCheckInAsync(t.BookingId), "undo2");
            var r = await bk.CancelAsync(t.BookingId, "no"); if (r.Succeeded) throw new InvalidOperationException("short reason accepted");
            Need(await bk.MarkNoShowAsync(t.BookingId, "Did not arrive"), "no-show");
        });
        await rec.Step("booking detail", "reschedule", async () =>
        {
            var (_, t) = await Walk("Resched", new DateOnly(1994, 4, 4));
            Need(await bk.UndoCheckInAsync(t.BookingId), "undo");
            StaffResult moved = StaffResult.Fail("none");
            for (var i = 1; i <= 7 && !moved.Succeeded; i++) moved = await bk.RescheduleAsync(t.BookingId, ClinicClock.Today.AddDays(i), "Harness");
            Need(moved, "reschedule");
        });

        // ── payments ──
        var pay = Get<IStaffPaymentsService>();
        var cb = await rec.Step("payments", "load collect board", () => pay.GetCollectBoardAsync());
        if (cb is not null) rec.Note($"payments: waiting={cb.WaitingToPay} due={cb.TotalDue} waivedToConfirm={cb.WaivedToConfirm} rows={cb.Rows.Count}");
        await rec.Step("payments", "list methods", () => pay.ListMethodsAsync());
        if (ticket1 is not null)
        {
            await rec.Step("payments", "record exact amount (walk-in 1, GCash + ref)", async () =>
            {
                var after = await pay.GetCollectBoardAsync();
                var row = after.Rows.FirstOrDefault(r => r.BookingId == ticket1.BookingId) ?? throw new InvalidOperationException("completed walk-in not on collect board");
                var amt = row.AmountDue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var wrong = await pay.RecordAsync(new RecordPaymentInput(ticket1.BookingId, PaymentMethod.Cash, (row.AmountDue + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), null, null));
                if (wrong.Succeeded) throw new InvalidOperationException("wrong amount accepted");
                Need(await pay.RecordAsync(new RecordPaymentInput(ticket1.BookingId, PaymentMethod.GCash, amt, "REF-H1", "harness")), "record");
                if (await pay.GetReceiptAsync(ticket1.BookingId) is null) throw new InvalidOperationException("receipt null");
            });
        }
        // waiver path: complete another visit, doctor waives via consultation row, staff confirms
        await rec.Step("payments", "waiver + confirm waiver", async () =>
        {
            var (_, t) = await Walk("Waiver", new DateOnly(1991, 1, 1));
            Need(await qb.ActAsync(t.BookingId, "call"), "call"); Need(await qb.ActAsync(t.BookingId, "complete"), "complete");
            await using (var db = await factory.CreateDbContextAsync())
            {
                var p = await db.Payments.SingleAsync(x => x.BookingId == t.BookingId);
                p.Status = PaymentStatus.Waived; p.WaivedReason = "Harness charity";
                (await db.Bookings.SingleAsync(x => x.BookingId == t.BookingId)).AmountDue = 0;
                await db.SaveChangesAsync();
            }
            var row = (await pay.GetCollectBoardAsync()).Rows.First(r => r.BookingId == t.BookingId);
            Need(await pay.ConfirmWaiverAsync(row.PaymentId), "confirm waiver");
        });
        // existing real payments
        await rec.Step("payments", "receipt for every paid booking in the DB", async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            var ids = await db.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Paid).Select(p => p.BookingId).Take(100).ToListAsync();
            foreach (var id in ids) _ = await pay.GetReceiptAsync(id);
            rec.Note($"receipts loaded: {ids.Count}");
        });

        // ── doctor status ──
        var ds = Get<IDoctorStatusService>();
        var dsb = await rec.Step("doctor status", "load", () => ds.GetTodayAsync());
        if (dsb is { Doctors.Count: > 0 })
        {
            await rec.Step("doctor status", "set one (RunningLate) + many (Available)", async () =>
            {
                Need(await ds.SetAsync(dsb.Doctors[0].DoctorId, DoctorDayStatusEnum.RunningLate), "set");
                Need(await ds.SetManyAsync(dsb.Doctors.Select(d => d.DoctorId), DoctorDayStatusEnum.Available), "set many");
            });
        }
        else rec.Check("doctor status", "has doctors", false, "no doctors in the board");

        // ── announcements ──
        await rec.Step("announcements", "load", async () => { var a = await Get<IStaffAnnouncementsService>().ListActiveAsync(); rec.Note($"announcements: {a.Count}"); });

        // ── profile ──
        var prof = Get<IStaffProfileService>();
        await rec.Step("profile", "load", async () => { var me = await prof.GetMineAsync() ?? throw new InvalidOperationException("profile null"); rec.Note($"profile: {me.FullName} / {me.Email} / '{me.ContactNumber}'"); });
        await rec.Step("profile", "save name + contact", async () => { var me = (await prof.GetMineAsync())!; Need(await prof.SaveAsync(me.FullName, string.IsNullOrWhiteSpace(me.ContactNumber) ? "0917 000 1111" : me.ContactNumber), "save"); });
        await rec.Step("profile", "change password and sign in with the new one", async () =>
        {
            const string next = "violet harbor lantern 42";
            Need(await prof.ChangePasswordAsync(DevDataSeeder.DevPassword, next, next), "change password");
            var auth = Get<IAuthService>(); await auth.LogoutAsync();
            var r = await auth.LoginAsync("staff@clinic.test", next, false);
            if (!r.Succeeded) throw new InvalidOperationException("login with new password failed: " + r.Error);
        });

        // ── doctor Today page services (not my lane; note only) ──
        await rec.Step("doctor Today (note)", "login doctor@clinic.test", async () =>
        {
            var auth = Get<IAuthService>(); await auth.LogoutAsync();
            var r = await auth.LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, false);
            if (!r.Succeeded) throw new InvalidOperationException("doctor login failed: " + r.Error);
            var did = r.Session!.DoctorId ?? throw new InvalidOperationException("session has no DoctorId");
            var dd = Get<IDoctorDashboardService>();
            await rec.Step("doctor Today (note)", "GetAsync (queue + day status)", () => dd.GetAsync(did));
            var today = ClinicClock.Today;
            await rec.Step("doctor Today (note)", "GetAnalyticsAsync month", () => dd.GetAnalyticsAsync(did, new DateOnly(today.Year, today.Month, 1), today, true));
            await rec.Step("doctor Today (note)", "GetAnalyticsAsync year", () => dd.GetAnalyticsAsync(did, new DateOnly(today.Year, 1, 1), today, false));
            await rec.Step("doctor Today (note)", "SetDayStatusAsync", async () => Need(await dd.SetDayStatusAsync(did, DoctorDayStatusEnum.Available), "set status"));
            // doctor-extra services as that doctor
            await rec.Step("doctor extra", "appointments + patients + schedule + profile + templates load", async () =>
            {
                await Get<IDoctorAppointmentsService>().ListAsync(did, AppointmentScope.Today);
                await Get<IDoctorAppointmentsService>().ListAsync(did, AppointmentScope.All);
                var pl = await Get<IDoctorPatientsService>().ListAsync(did);
                foreach (var r2 in pl) await Get<IDoctorPatientsService>().GetChartAsync(did, r2.PatientId);
                await Get<IDoctorScheduleService>().GetAsync(did);
                await Get<IDoctorProfileService>().GetMineAsync();
                await Get<IDoctorTemplatesService>().GetAsync(did);
            });
        });

        await sp.DisposeAsync();
        Finish(rec, run);
    }

    /// <summary>Repro of "doctor Today fails to load": open the PRISTINE copy with NO init/repair (an app instance whose init did not repair the file) and run the reads Today does.</summary>
    [SkippableFact, Trait("Category", "RealDb")]
    public async Task Pristine_copy_without_init_doctor_today_reads()
    {
        var src = Environment.GetEnvironmentVariable("CLINIC_COPY_DIR") ?? ""; // opt-in only: unset = skip, so the normal suite never needs the copy
        // JJQR - AI (Nov 21, 2026) - vstest treats SkipException.ForSkip as a failure; SkippableFact records a skip
        Skip.If(src.Length == 0, "CLINIC_COPY_DIR is not set; RealDb harness is env-gated.");
        Skip.If(!Directory.Exists(src), "CLINIC_COPY_DIR does not exist: " + src);
        // JJQR - END
        var run = Path.Combine("/tmp", "clinic-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        foreach (var f in Directory.GetFiles(src, "clinic-node.db*")) File.Copy(f, Path.Combine(run, Path.GetFileName(f)));
        var rec = new Rec();
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddClinicNodeCore(new NodeOptions { DataDirectory = run, Demo = true });
        sc.AddClinicNodeSync(); sc.AddClinicNodeStaff(); sc.AddClinicNodeDoctorExtra();
        await using var sp = sc.BuildServiceProvider();
        var factory = sp.GetRequiredService<IDbContextFactory<ClinicAppDbContext>>();
        var doctorId = Guid.Empty;
        await rec.Step("pristine/no-init", "resolve doctor@clinic.test (raw)", async () =>
        {
            await using var db = await factory.CreateDbContextAsync();
            var d = await db.Doctors.AsNoTracking().Include(x => x.StaffAccount).FirstAsync(x => x.StaffAccount!.Email == "doctor@clinic.test"); doctorId = d.DoctorId;
        });
        var dd = sp.GetRequiredService<IDoctorDashboardService>();
        await rec.Step("pristine/no-init", "doctor dashboard GetAsync (queue + day status)", () => dd.GetAsync(doctorId));
        var today = ClinicClock.Today;
        await rec.Step("pristine/no-init", "doctor analytics month", () => dd.GetAnalyticsAsync(doctorId, new DateOnly(today.Year, today.Month, 1), today, true));
        await rec.Step("pristine/no-init", "staff dashboard GetAsync", () => sp.GetRequiredService<IStaffDashboardService>().GetAsync());
        await rec.Step("pristine/no-init", "staff queue board GetAsync", () => sp.GetRequiredService<IStaffQueueBoardService>().GetAsync());
        await rec.Step("pristine/no-init", "login staff", async () => { var r = await sp.GetRequiredService<IAuthService>().LoginAsync("staff@clinic.test", DevDataSeeder.DevPassword, false); if (!r.Succeeded) throw new InvalidOperationException(r.Error); });
        var sb = new StringBuilder($"PRISTINE no-init run: {rec.Pass} pass / {rec.Fail} fail\n");
        foreach (var l in rec.Lines) sb.AppendLine(l);
        File.WriteAllText("/tmp/pristine-harness-report.txt", sb.ToString());
    }

    static void Finish(Rec rec, string run)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Harness run dir (copy): {run}");
        sb.AppendLine($"TOTAL: {rec.Pass} pass / {rec.Fail} fail");
        foreach (var l in rec.Lines) sb.AppendLine(l);
        File.WriteAllText("/tmp/staff-harness-report.txt", sb.ToString());
        Assert.True(rec.Fail == 0, $"{rec.Fail} harness step(s) failed; see /tmp/staff-harness-report.txt");
    }
}
//JJQR - END
