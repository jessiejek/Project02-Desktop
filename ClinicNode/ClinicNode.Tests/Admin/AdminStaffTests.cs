// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Admin;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Admin;

public class AdminStaffTests
{
    [Fact]
    public async Task Invite_rejects_duplicate_email()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var svc = host.Get<IAdminStaffService>();
        var dup = await svc.InviteAsync(new StaffInviteRequest("admin@clinic.test", "Ada Admin", "Admin"));
        Assert.False(dup.Succeeded);
        Assert.Contains("already exists", dup.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Invite_staff_and_revoke_invite()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var svc = host.Get<IAdminStaffService>();
        var invited = await svc.InviteAsync(new StaffInviteRequest("front.desk@clinic.test", "Front Desk", "Staff", InitialPassword: "Password1!"));
        Assert.True(invited.Succeeded, invited.Error);
        Assert.True(invited.Value!.PasswordSet);

        var list = await svc.SearchAsync("Front Desk", "Staff");
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(StaffStatus.Invited, list.Items[0].Status);

        var revoked = await svc.RevokeInviteAsync(invited.Value.UserId);
        Assert.True(revoked.Succeeded, revoked.Error);
        var after = await svc.SearchAsync("front.desk@clinic.test", "Staff");
        Assert.Equal(0, after.TotalCount);
    }

    [Fact]
    public async Task Deactivating_staff_revokes_refresh_tokens()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var staff = host.Get<IAdminStaffService>();
        var invited = await staff.InviteAsync(new StaffInviteRequest("temp.staff@clinic.test", "Temp Staff", "Staff", InitialPassword: "Password1!"));
        Assert.True(invited.Succeeded, invited.Error);

        // Activate first so we can deactivate (invite status is Invited).
        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.StaffAccounts.SingleAsync(s => s.StaffId == invited.Value!.StaffId);
            row.Status = StaffStatus.Active;
            db.RefreshTokens.Add(new ClinicApp.Auth.RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = invited.Value.UserId,
                TokenHash = "test-hash",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var updated = await staff.UpdateAsync(invited.Value!.StaffId, "Temp Staff", null, StaffStatus.Inactive);
        Assert.True(updated.Succeeded, updated.Error);
        Assert.Equal(StaffStatus.Inactive, updated.Value!.Status);

        await using (var db = await factory.CreateDbContextAsync())
        {
            var tokens = await db.RefreshTokens.Where(t => t.UserId == invited.Value.UserId).ToListAsync();
            Assert.All(tokens, t => Assert.NotNull(t.RevokedAt));
        }
    }

    [Fact]
    public async Task Invite_doctor_with_negative_fee_is_rejected()
    {
        await using var host = await AdminTestHost.StartAsync();
        await host.LoginAdminAsync();
        var svc = host.Get<IAdminStaffService>();
        var result = await svc.InviteAsync(new StaffInviteRequest(
            "bad.doc@clinic.test", "Bad Doc", "Doctor",
            InitialPassword: "Password1!",
            Doctor: new DoctorInviteProfile("IM", ConsultationFee: -10, SlotDurationMinutes: 30, SlotCapacity: 1)));
        Assert.False(result.Succeeded);
        Assert.Contains("cannot be negative", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
// JJQR - END
