//JJQR - AI (Nov 22, 2026) - owner: every profile (doctor, staff, admin) can change its own sign-in email. The current password is required; the server checks the address and that nobody else uses it.
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Shared.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Shared;


public static class ProfileEmailCard
{
    public static View Build(IStaffProfileService svc, Func<string?> currentEmail, Action<string>? onChanged = null)
    {
        var current = ClinicTheme.Text("", "MutedBody");
        var email = new Entry { Placeholder = "New email address", Keyboard = Keyboard.Email };
        var password = new Entry { Placeholder = "Current password", IsPassword = true };
        var error = StaffUi.Error();
        Button? btn = null;
        void Refresh() => current.Text = "Signed in as " + (currentEmail() ?? "");
        Refresh();
        btn = StaffUi.Btn("Change email", async () =>
        {
            StaffUi.Show(error, null);
            var r = await svc.ChangeEmailAsync(password.Text, email.Text);
            if (!r.Succeeded) { StaffUi.Show(error, r.Error ?? "Could not change the email."); return; }
            var changed = (email.Text ?? "").Trim().ToLowerInvariant();
            password.Text = ""; email.Text = "";
            onChanged?.Invoke(changed);
            Refresh();
            ClinicToast.Show("Email changed. Use the new email the next time you sign in.", ToastKind.Success);
        }, "SecondaryButton");
        return StaffUi.Card(
            new Label { Text = "Change email", FontFamily = ClinicTheme.FontSemiBold, FontSize = 17 },
            current, error,
            StaffUi.Field("New email", email),
            StaffUi.Field("Current password", password, "Needed to confirm it is you."),
            btn);
    }
}
//JJQR - END
