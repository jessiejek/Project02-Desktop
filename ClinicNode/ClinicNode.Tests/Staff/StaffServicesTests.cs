//JJQR - AI (Nov 21, 2026) - front-desk services against a temp local SQLite node.
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Staff;
using ClinicNode.Core.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.Staff;

public class StaffDashboardServiceTests
{
    [Fact]
    public async Task Arrivals_toggle_check_in_and_cancel_with_reason()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var dash = host.Make<StaffDashboardService>();
        var p = await host.NewPatientAsync();
        var t = await host.WalkInAsync(p.PatientId);

        var board = await dash.GetAsync();
        var row = Assert.Single(board.Arrivals, a => a.BookingId == t.BookingId);
        Assert.Equal(BookingStatus.CheckedIn, row.Status);
        Assert.False(row.CanCancel);
        Assert.Equal("Undo check-in", row.CheckInLabel);

        Assert.True((await dash.ToggleCheckInAsync(t.BookingId)).Succeeded);
        row = Assert.Single((await dash.GetAsync()).Arrivals, a => a.BookingId == t.BookingId);
        Assert.Equal(BookingStatus.Confirmed, row.Status);
        Assert.True(row.CanCancel);

        var blank = await dash.CancelAsync(t.BookingId, "  ");
        Assert.Equal("Please enter a reason.", blank.Error);
        var before = (await dash.GetAsync()).PatientsToday;
        Assert.True((await dash.CancelAsync(t.BookingId, "Patient left")).Succeeded);
        var after = await dash.GetAsync();
        Assert.DoesNotContain(after.Arrivals, a => a.BookingId == t.BookingId);
        Assert.Equal(before - 1, after.PatientsToday);
    }

    [Fact]
    public async Task Ready_for_payment_counts_completed_visits_that_owe()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        await host.CompletedVisitAsync();
        var board = await host.Make<StaffDashboardService>().GetAsync();
        Assert.Equal(1, board.ReadyForPayment);
    }
}

public class StaffPaymentsServiceTests
{
    [Fact]
    public async Task Collect_list_records_exact_amount_with_or_number()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var t = await host.CompletedVisitAsync();
        var pay = host.Make<StaffPaymentsService>();

        var board = await pay.GetCollectBoardAsync();
        var row = Assert.Single(board.Rows);
        Assert.False(row.IsWaived);
        Assert.Equal(1, board.WaitingToPay);
        Assert.Equal(t.ProvisionalFee, board.TotalDue);

        var blank = await pay.RecordAsync(new RecordPaymentInput(t.BookingId, PaymentMethod.Cash, "", null, null));
        Assert.Equal("Enter the amount received.", blank.Error);
        var wrong = await pay.RecordAsync(new RecordPaymentInput(t.BookingId, PaymentMethod.Cash, (t.ProvisionalFee + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), null, null));
        Assert.False(wrong.Succeeded);

        var ok = await pay.RecordAsync(new RecordPaymentInput(t.BookingId, PaymentMethod.GCash, t.ProvisionalFee.ToString(System.Globalization.CultureInfo.InvariantCulture), "REF-9", "thanks"));
        Assert.True(ok.Succeeded, ok.Error);
        Assert.Empty((await pay.GetCollectBoardAsync()).Rows);

        var receipt = await pay.GetReceiptAsync(t.BookingId);
        Assert.NotNull(receipt);
        Assert.Matches(@"^OR-[A-F0-9]{4}-\d{8}-\d{4}$", receipt!.OrNumber);
        Assert.NotEqual(StaffRules.OrNumberFor(t.BookingId), receipt.OrNumber);
        Assert.Equal(PaymentMethod.GCash, receipt.Method);
        Assert.True(receipt.PendingSync);
    }

    [Fact]
    public async Task Desk_waiver_needs_the_doctors_decision_and_doctor_waivers_wait_for_confirmation()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var t = await host.CompletedVisitAsync();
        var pay = host.Make<StaffPaymentsService>();

        Assert.Equal("At least 5 characters.", (await pay.WaiveAsync(t.BookingId, "no")).Error);
        Assert.Equal("Only the doctor can waive the professional fee.", (await pay.WaiveAsync(t.BookingId, "Indigent patient")).Error);

        await using (var db = await host.DbAsync())
        {
            var b = await db.Bookings.SingleAsync(x => x.BookingId == t.BookingId);
            db.Consultations.Add(new Consultation
            {
                ConsultationId = Guid.NewGuid(), BookingId = b.BookingId, PatientId = b.PatientId, DoctorId = b.DoctorId,
                Status = ConsultationStatus.Completed, PfDecision = "Waive", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var waived = await pay.WaiveAsync(t.BookingId, "Indigent patient");
        Assert.True(waived.Succeeded, waived.Error);
        // The desk's own waiver is already confirmed, so nothing is left on the list.
        Assert.Empty((await pay.GetCollectBoardAsync()).Rows);
    }

    [Fact]
    public async Task Doctor_waived_visit_shows_at_zero_until_confirm_waiver()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var t = await host.CompletedVisitAsync();
        await using (var db = await host.DbAsync())
        {
            var p = await db.Payments.SingleAsync(x => x.BookingId == t.BookingId);
            p.Status = PaymentStatus.Waived;
            p.WaivedReason = "Charity";
            var b = await db.Bookings.SingleAsync(x => x.BookingId == t.BookingId);
            b.AmountDue = 0;
            await db.SaveChangesAsync();
        }
        var pay = host.Make<StaffPaymentsService>();
        var board = await pay.GetCollectBoardAsync();
        var row = Assert.Single(board.Rows);
        Assert.True(row.IsWaived);
        Assert.Equal(0m, row.AmountDue);
        Assert.Equal("Charity", row.WaivedReason);
        Assert.Equal(1, board.WaivedToConfirm);
        Assert.Equal(0, board.WaitingToPay);
        Assert.False((await pay.RecordAsync(new RecordPaymentInput(t.BookingId, PaymentMethod.Cash, "0", null, null))).Succeeded);

        Assert.True((await pay.ConfirmWaiverAsync(row.PaymentId)).Succeeded);
        Assert.Empty((await pay.GetCollectBoardAsync()).Rows);
    }
}

public class StaffPatientsServiceTests
{
    [Fact]
    public async Task Quick_register_validates_then_issues_an_mf_code_and_search_finds_it()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var svc = host.Make<StaffPatientsService>();

        var future = new QuickRegisterForm("Juan", null, "Dela Cruz", ClinicClock.Today.AddDays(1), SexType.Male, null, null);
        Assert.Equal("Date of birth cannot be in the future.", svc.ValidateQuickRegister(future));
        var badPhone = future with { DateOfBirth = new DateOnly(1980, 5, 5), ContactNumber = "abc" };
        Assert.Equal("Contact number must be a phone number (digits, spaces, + - ( ) only).", svc.ValidateQuickRegister(badPhone));
        var noSex = badPhone with { ContactNumber = "0917 555 0000", Sex = null };
        Assert.False(svc.CanQuickRegister(noSex));
        var longName = noSex with { Sex = SexType.Male, FirstName = new string('x', 101) };
        Assert.Equal("First name is too long (max 100 characters).", svc.ValidateQuickRegister(longName));

        var (result, row) = await svc.QuickRegisterAsync(longName with { FirstName = "Juan" });
        Assert.True(result.Succeeded, result.Error);
        Assert.StartsWith("MF-", row!.PatientCode);
        Assert.Equal("NoAccount", row.AccountStatus);

        var byPhone = await svc.SearchAsync("555 0000");
        Assert.Contains(byPhone.Items, p => p.PatientId == row.PatientId);
        var byName = await svc.SearchAsync("juan dela");
        Assert.Contains(byName.Items, p => p.PatientId == row.PatientId);
        var page = await svc.SearchAsync(null, 1, 1);
        Assert.Single(page.Items);
        Assert.Equal(1, page.RangeStart);
        Assert.Equal(page.TotalCount, page.PageCount);
    }

    [Fact]
    public async Task Detail_lists_bookings()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var p = await host.NewPatientAsync("Detail", "Case");
        var t = await host.WalkInAsync(p.PatientId);
        var detail = await host.Make<StaffPatientsService>().GetDetailAsync(p.PatientId);
        Assert.NotNull(detail);
        Assert.Equal("Detail Case", detail!.Patient.FullName);
        Assert.Contains(detail.Bookings, b => b.BookingId == t.BookingId);
        Assert.Null(await host.Make<StaffPatientsService>().GetDetailAsync(Guid.NewGuid()));
    }
}

public class StaffProfileServiceTests
{
    [Fact]
    public async Task Save_name_and_contact_with_api_rules()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var svc = host.Make<StaffProfileService>();
        var me = await svc.GetMineAsync();
        Assert.NotNull(me);
        Assert.Equal("staff@clinic.test", me!.Email);

        Assert.Equal("Enter your full name.", (await svc.SaveAsync(" ", null)).Error);
        Assert.Equal("Contact number must be a phone number (digits, spaces, + - ( ) only).", (await svc.SaveAsync("Front Desk", "xx")).Error);
        Assert.True((await svc.SaveAsync("Maria Front Desk", "0917 000 1111")).Succeeded);
        var saved = await svc.GetMineAsync();
        Assert.Equal("Maria Front Desk", saved!.FullName);
        Assert.Equal("0917 000 1111", saved.ContactNumber);
    }

    [Fact]
    public async Task Change_password_checks_current_and_policy_then_new_password_signs_in()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var svc = host.Make<StaffProfileService>();
        var current = ClinicApp.Infrastructure.DevDataSeeder.DevPassword;
        const string next = "violet harbor lantern 42";

        Assert.False(svc.CanUpdatePassword(current, "short", "short"));
        Assert.False(svc.CanUpdatePassword(current, next, next + "x"));
        Assert.True(svc.CanUpdatePassword(current, next, next));

        Assert.Equal("Current password is incorrect.", (await svc.ChangePasswordAsync("wrong-password-1", next, next)).Error);
        Assert.Contains("too common", (await svc.ChangePasswordAsync(current, "password1234", "password1234")).Error);
        Assert.True((await svc.ChangePasswordAsync(current, next, next)).Succeeded);

        var auth = host.Get<IAuthService>();
        await auth.LogoutAsync();
        Assert.False((await auth.LoginAsync("staff@clinic.test", current, false)).Succeeded);
        Assert.True((await auth.LoginAsync("staff@clinic.test", next, false)).Succeeded);
    }
}

public class DoctorStatusServiceTests
{
    [Fact]
    public async Task Defaults_to_available_and_upserts_one_or_many()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var svc = host.Make<DoctorStatusService>();
        var board = await svc.GetTodayAsync();
        Assert.NotEmpty(board.Doctors);
        Assert.All(board.Doctors, d => Assert.Equal(DoctorDayStatusEnum.Available, d.Status));
        var id = board.Doctors[0].DoctorId;

        Assert.True((await svc.SetAsync(id, DoctorDayStatusEnum.RunningLate)).Succeeded);
        Assert.True((await svc.SetAsync(id, DoctorDayStatusEnum.RunningLate)).Succeeded); // idempotent upsert
        board = await svc.GetTodayAsync();
        Assert.Equal(1, board.RunningLate);

        Assert.True((await svc.SetManyAsync(board.Doctors.Select(d => d.DoctorId), DoctorDayStatusEnum.UnavailableToday)).Succeeded);
        board = await svc.GetTodayAsync();
        Assert.Equal(board.Doctors.Count, board.OutToday);
        await using var db = await host.DbAsync();
        Assert.Equal(board.Doctors.Count, await db.DoctorDayStatuses.CountAsync(s => s.StatusDate == ClinicClock.Today));
        Assert.Equal("Out today", DoctorStatusService.Label(DoctorDayStatusEnum.UnavailableToday));
    }
}

public class StaffAnnouncementsServiceTests
{
    [Fact]
    public async Task Only_active_newest_first_with_poster_fallback()
    {
        await using var host = await TestHost.StartAsync();
        await using (var db = await host.DbAsync())
        {
            db.Announcements.RemoveRange(db.Announcements);
            var now = DateTimeOffset.UtcNow;
            db.Announcements.Add(new Announcement { Id = Guid.NewGuid(), Title = "Old", Body = "a", IsActive = true, CreatedAt = now.AddDays(-1), UpdatedAt = now });
            db.Announcements.Add(new Announcement { Id = Guid.NewGuid(), Title = "New", Body = "b", IsActive = true, CreatedAt = now, UpdatedAt = now, PostedByUserId = Guid.NewGuid() });
            db.Announcements.Add(new Announcement { Id = Guid.NewGuid(), Title = "Hidden", Body = "c", IsActive = false, CreatedAt = now, UpdatedAt = now });
            await db.SaveChangesAsync();
        }
        var rows = await host.Make<StaffAnnouncementsService>().ListActiveAsync();
        Assert.Equal(["New", "Old"], rows.Select(r => r.Title).ToArray());
        Assert.All(rows, r => Assert.Equal("Clinic admin", r.PostedBy));
    }
}

public class StaffBookingServiceTests
{
    [Fact]
    public async Task Detail_cancel_needs_five_characters_and_reschedule_clones_the_booking()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var p = await host.NewPatientAsync("Res", "Ched");
        var t = await host.WalkInAsync(p.PatientId);
        var svc = host.Make<StaffBookingService>();
        Assert.True((await svc.UndoCheckInAsync(t.BookingId)).Succeeded);

        var detail = await svc.GetAsync(t.BookingId);
        Assert.NotNull(detail);
        Assert.True(detail!.Actions.Reschedule && detail.Actions.CheckIn && detail.Actions.Cancel);
        Assert.Equal("At least 5 characters.", (await svc.CancelAsync(t.BookingId, "no")).Error);
        Assert.Equal("Cannot reschedule to a past date.", (await svc.RescheduleAsync(t.BookingId, ClinicClock.Today.AddDays(-1), null)).Error);

        // Find a future day the clinic is open.
        StaffResult moved = StaffResult.Fail("none");
        for (var i = 1; i <= 7 && !moved.Succeeded; i++)
            moved = await svc.RescheduleAsync(t.BookingId, ClinicClock.Today.AddDays(i), "Patient asked");
        Assert.True(moved.Succeeded, moved.Error);
        var old = await svc.GetAsync(t.BookingId);
        Assert.Equal(BookingStatus.Rescheduled, old!.Status);
        var fresh = await svc.GetAsync(moved.Id!.Value);
        Assert.Equal(BookingStatus.Confirmed, fresh!.Status);
        Assert.False(string.IsNullOrEmpty(fresh.QueueNumber));
        Assert.Equal(PaymentStatus.Unpaid, fresh.PaymentStatus);
    }

    [Fact]
    public async Task Queue_board_service_runs_actions_and_cancel_needs_reason()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var p = await host.NewPatientAsync("Senior", "Lane", new DateOnly(1950, 1, 1));
        var t = await host.WalkInAsync(p.PatientId, "Senior");
        var svc = host.Make<StaffQueueBoardService>();
        var (summary, rows) = await svc.GetAsync();
        var row = Assert.Single(rows, r => r.Entry.BookingId == t.BookingId);
        Assert.True(row.IsPriority);
        Assert.StartsWith("S-", row.Entry.QueueNumber);
        Assert.Equal(rows[0].Entry.BookingId, rows.First(r => r.IsPriority).Entry.BookingId); // S- lane first
        var waitingBefore = summary.Waiting;
        var withDoctorBefore = summary.WithDoctor;
        Assert.Equal("Please enter a reason.", (await svc.CancelAsync(t.BookingId, "")).Error);
        Assert.True((await svc.ActAsync(t.BookingId, "call")).Succeeded);
        (summary, _) = await svc.GetAsync();
        Assert.Equal(withDoctorBefore + 1, summary.WithDoctor);
        Assert.Equal(waitingBefore - 1, summary.Waiting);
        Assert.False((await svc.ActAsync(t.BookingId, "bogus")).Succeeded);
    }
}

//JJQR - AI (Nov 21, 2026) - ISyncBridge.RequestSync is called after a successful save, and not after a failed one.
sealed class RecordingSyncBridge : ISyncBridge
{
    public int Calls;
    public void RequestSync() => Calls++;
    public Task<bool> IsRevokedAsync(CancellationToken ct = default) => Task.FromResult(false);
}

public class StaffSyncHookTests
{
    [Fact]
    public async Task RequestSync_runs_after_a_successful_save_not_after_a_failed_one()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var sync = new RecordingSyncBridge();

        var profile = ActivatorUtilities.CreateInstance<StaffProfileService>(host.Services, sync);
        Assert.Equal("Enter your full name.", (await profile.SaveAsync(" ", null)).Error);
        Assert.Equal(0, sync.Calls);
        Assert.True((await profile.SaveAsync("Maria Front Desk", "0917 000 1111")).Succeeded);
        Assert.Equal(1, sync.Calls);

        var doctors = ActivatorUtilities.CreateInstance<DoctorStatusService>(host.Services, sync);
        var board = await doctors.GetTodayAsync();
        var id = Assert.Single(board.Doctors.Take(1)).DoctorId;
        Assert.True((await doctors.SetAsync(id, DoctorDayStatusEnum.RunningLate)).Succeeded);
        Assert.Equal(2, sync.Calls);

        var t = await host.CompletedVisitAsync("Rosa");
        var pay = ActivatorUtilities.CreateInstance<StaffPaymentsService>(host.Services, sync);
        var blank = await pay.RecordAsync(new RecordPaymentInput(t.BookingId, PaymentMethod.Cash, "", null, null));
        Assert.False(blank.Succeeded);
        Assert.Equal(2, sync.Calls);
        var fee = t.ProvisionalFee.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.True((await pay.RecordAsync(new RecordPaymentInput(t.BookingId, PaymentMethod.Cash, fee, null, null))).Succeeded);
        Assert.Equal(3, sync.Calls);
    }
}
//JJQR - END
