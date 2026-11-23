//JJQR - AI (Nov 23, 2026) - owner wishlist: the admin sets a temporary password for a Staff or Doctor member (support without a developer).
// Used by the Staff list and the Doctor edit page. Online only (the server owns passwords). They must choose their own at next sign-in.
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.Core.Admin;

namespace ClinicNode.App.Views.Admin;

public static class AdminSetPasswordDialog
{
    /// <param name="staffId">The StaffId (for a doctor this is the DoctorId).</param>
    public static async Task ShowAsync(Page page, Guid staffId, string name)
    {
        var svc = page.Handler?.MauiContext?.Services.GetService<IAdminStaffService>();
        if (svc is null) return;

        var pw = new Entry { IsPassword = true, Placeholder = "New password (12 or more characters)" };
        var again = new Entry { IsPassword = true, Placeholder = "Type it again" };
        var body = new VerticalStackLayout
        {
            Spacing = 10,
            WidthRequest = 420,
            Children =
            {
                ClinicTheme.Text($"Set a temporary password for {name}. Tell them in person. They will be asked to choose their own the next time they sign in, and any computer they are signed in on is signed out.", "MutedBody"),
                ClinicUi.Field("New password", pw),
                ClinicUi.Field("Confirm password", again),
            },
        };

        var done = await AdminDialog.ShowSubmitAsync(page, "Set password", body, "Set password", async () =>
        {
            if (string.IsNullOrEmpty(pw.Text)) return "Type a new password.";
            if (pw.Text != again.Text) return "The two passwords do not match.";
            var r = await svc.SetPasswordAsync(staffId, pw.Text);
            return r.Succeeded ? null : (r.Error ?? "The password could not be set. Try again.");
        }, busyText: "Saving…");

        if (done) await page.DisplayAlertAsync("Password set", $"{name} can sign in with the new password and will then choose their own.", "OK");
    }
}
//JJQR - END
