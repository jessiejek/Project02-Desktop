//JJQR - AI (Nov 21, 2026) - /staff/profile "Your profile": Details (Full name, Contact number, Email read-only, Save changes) + Change password.
using ClinicNode.App.Controls;
using ClinicNode.Core.Staff;

namespace ClinicNode.App.Views.Staff;

public sealed class StaffProfilePage : StaffLivePage
{
    private readonly Entry _fullName = new();
    private readonly Entry _contact = new() { Keyboard = Keyboard.Telephone };
    private readonly Entry _email = new() { IsReadOnly = true };
    private readonly Label _detailsError = StaffUi.Error();
    private readonly Label _detailsSaved = StaffUi.Success();
    private readonly Entry _current = new() { IsPassword = true };
    private readonly Entry _new = new() { IsPassword = true };
    private readonly Entry _confirm = new() { IsPassword = true };
    private readonly Label _mismatch = StaffUi.Error();
    private readonly Label _pwError = StaffUi.Error();
    private readonly Label _pwSaved = StaffUi.Success();
    private readonly Button _update;
    //JJQR - AI (21 Nov 2026) - STAFF PARITY (web /staff/profile): ErrorState "Your profile didn’t load" while no profile is loaded (cards hidden); a failed
    // refresh after a load keeps the form silently. "Save changes" is enabled only when name or number changed; both buttons sit on the right.
    private readonly VerticalStackLayout _body = new() { Spacing = 16 };
    private readonly Button _save;
    private string _initialName = "", _initialContact = "";
    protected override (string Title, string Message)? LoadErrorState => ("Your profile didn’t load", "Check the connection to the clinic server, then try again.");
    protected override string? StaleLoadWarning => "";
    protected override void OnLoadStateChanged(bool failed) => _body.IsVisible = !failed;
    //JJQR - END

    public StaffProfilePage(IServiceProvider sp) : base(sp, "Profile", live: false)
    {
        var save = _save = StaffUi.Btn("Save changes", SaveAsync); //JJQR - AI (21 Nov 2026) - kept for the dirty check //JJQR - END
        //JJQR - AI (21 Nov 2026) - STAFF PARITY
        save.HorizontalOptions = LayoutOptions.End;
        save.IsEnabled = false;
        foreach (var e in new[] { _fullName, _contact }) e.TextChanged += (_, _) => RefreshDirty();
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - web: "Update password" is the secondary button
        _update = StaffUi.Btn("Update password", UpdatePasswordAsync, "SecondaryButton");
        //JJQR - END
        _update.IsEnabled = false;
        _update.HorizontalOptions = LayoutOptions.End; //JJQR - AI (21 Nov 2026) - STAFF PARITY //JJQR - END
        foreach (var e in new[] { _current, _new, _confirm }) e.TextChanged += (_, _) => RefreshPassword();
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                StaffUi.Header("Your profile", "Your name and number as the clinic sees them, and your sign-in password."),
                LoadError,
                ErrorStateView, //JJQR - AI (21 Nov 2026) - STAFF PARITY //JJQR - END
                BodyOf(_body, //JJQR - AI (21 Nov 2026) - STAFF PARITY: the cards the web ErrorState replaces //JJQR - END
                StaffUi.Card(
                    ClinicTheme.Text("Details", "HeadlineSm"),
                    _detailsSaved, _detailsError, //JJQR - AI (21 Nov 2026) - STAFF PARITY: web order: saved, then error //JJQR - END
                    StaffUi.Field("Full name", _fullName),
                    StaffUi.Field("Contact number", _contact),
                    StaffUi.Field("Email", _email, "Ask an admin to change the email you sign in with."),
                    save),
                StaffUi.Card(
                    ClinicTheme.Text("Change password", "HeadlineSm"),
                    _pwSaved, _pwError,
                    StaffUi.Field("Current password", _current),
                    StaffUi.Field("New password", _new, StaffProfileService.PasswordHint),
                    StaffUi.Field("Confirm new password", _confirm),
                    _mismatch,
                    _update)), //JJQR - AI (21 Nov 2026) - closes BodyOf //JJQR - END
            }
        });
    }

    protected override async Task LoadAsync()
    {
        var p = await Svc<IStaffProfileService>().GetMineAsync();
        //JJQR - AI (21 Nov 2026) - STAFF PARITY: no profile = the web ErrorState (thrown so the base shows it)
        if (p is null) throw new InvalidOperationException("profile not found");
        _initialName = p.FullName ?? "";
        _initialContact = p.ContactNumber ?? "";
        //JJQR - END
        _fullName.Text = p.FullName;
        _contact.Text = p.ContactNumber;
        _email.Text = p.Email;
        RefreshDirty(); //JJQR - AI (21 Nov 2026) //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - STAFF PARITY helpers
    void RefreshDirty() => _save.IsEnabled = (_fullName.Text ?? "") != _initialName || (_contact.Text ?? "") != _initialContact;

    static View BodyOf(VerticalStackLayout body, params View[] children)
    {
        foreach (var c in children) body.Children.Add(c);
        return body;
    }
    //JJQR - END

    void RefreshPassword()
    {
        var mismatch = !string.IsNullOrEmpty(_confirm.Text) && _confirm.Text != _new.Text;
        StaffUi.Show(_mismatch, mismatch ? "Passwords don’t match yet." : null);
        _update.IsEnabled = Svc<IStaffProfileService>().CanUpdatePassword(_current.Text, _new.Text, _confirm.Text);
    }

    async Task SaveAsync()
    {
        StaffUi.Show(_detailsError, null);
        StaffUi.Show(_detailsSaved, null);
        var r = await Svc<IStaffProfileService>().SaveAsync(_fullName.Text, _contact.Text);
        if (!r.Succeeded) { StaffUi.Show(_detailsError, r.Error ?? "Your profile wasn’t saved. Please try again."); return; }
        StaffUi.Show(_detailsSaved, $"Saved {StaffUi.DateTimeText(DateTimeOffset.Now)}.");
        //JJQR - AI (21 Nov 2026) - STAFF PARITY: the saved values are the new baseline for the dirty check
        _initialName = _fullName.Text ?? "";
        _initialContact = _contact.Text ?? "";
        Dispatcher.Dispatch(RefreshDirty); // after Btn re-enables itself
        //JJQR - END
    }

    async Task UpdatePasswordAsync()
    {
        StaffUi.Show(_pwError, null);
        StaffUi.Show(_pwSaved, null);
        var r = await Svc<IStaffProfileService>().ChangePasswordAsync(_current.Text, _new.Text, _confirm.Text);
        if (!r.Succeeded) { StaffUi.Show(_pwError, r.Error ?? "Could not update the password."); Dispatcher.Dispatch(RefreshPassword); return; }
        _current.Text = _new.Text = _confirm.Text = "";
        StaffUi.Show(_pwSaved, "Password updated. Use it the next time you sign in.");
        Dispatcher.Dispatch(RefreshPassword); // after Btn re-enables itself
    }
}
//JJQR - END
