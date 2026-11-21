//JJQR - AI (Nov 21, 2026) - D-PROFILE: doctor profile + change password (web doctor/profile/page.tsx).
//JJQR - AI (21 Nov 2026) - doctor self-edit is the Bio only (PUT /api/doctors/me { bio } via IDoctorBioService, online-first). Full name, specialization,
// fee, license, PTR and S2 are shown read-only: they are Admin-only on the backend.
//JJQR - END
//JJQR - AI (21 Nov 2026) - ALL-2 1:1 UI pass vs web doctor/profile/page.tsx (allen5): ONE card ("Professional details" + email, Full Name / Specialization / Bio,
// Consultation Fee / License # / PTR # / S2 # in a 2-column grid, "Save profile" → "Saving profile…", the web full-name/fee check), then "Clinic availability"
// + "Manage availability" and the Change Password section inside the same card (labels as placeholders, "Updating…"). Load failure = web ErrorState
// "Profile unavailable". Save behaviour kept on purpose until Jessie decides: Admin-only fields read-only, Bio via PUT /api/doctors/me (the web's
// PUT /api/doctors/{id} is Admin-only and fails for a doctor). Password: IDoctorProfileService → IStaffProfileService (online-first →
// POST /api/auth/change-password, the web's changePassword route; local fallback offline).
// (the page body below is the 21 Nov 2026 rewrite)
//JJQR - END
//JJQR - AI (21 Nov 2026) - ALL-2 page body
using System.Globalization;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.DoctorExtra;

namespace ClinicNode.App.Views.Doctor.Extra;

public sealed class DoctorProfilePage : StaffLivePage
{
    private readonly VerticalStackLayout _body = StaffUi.Column();
    private readonly Entry _name = new(), _spec = new(), _fee = new() { Keyboard = Keyboard.Numeric }, _license = new(), _ptr = new(), _s2 = new();
    private readonly Editor _bio = new() { HeightRequest = 96 };
    private readonly Entry _current = new() { IsPassword = true, Placeholder = "Current Password" },
        _new = new() { IsPassword = true, Placeholder = "New Password" },
        _confirm = new() { IsPassword = true, Placeholder = "Confirm Password" };
    private readonly Label _error = StaffUi.Error(), _saved = StaffUi.Success(), _pwError = StaffUi.Error(), _pwSaved = StaffUi.Success();
    private readonly Label _email = ClinicTheme.Text("", "MutedBody");
    private Button? _updatePw;

    public DoctorProfilePage(IServiceProvider sp) : base(sp, "Profile", live: false)
    {
        Content = StaffUi.Scroll(new VerticalStackLayout
        {
            Spacing = 16,
            Children = { StaffUi.Header("Your profile", "Manage your professional identity and the details used on prescriptions."), LoadError, ErrorStateView, _body },
        });
        foreach (var e in new[] { _current, _new, _confirm }) e.TextChanged += (_, _) => UpdatePwButton();
    }

    // web: <ErrorState title="Profile unavailable" message="Your profile could not be loaded. Please try again." onRetry/>
    protected override (string Title, string Message)? LoadErrorState => ("Profile unavailable", "Your profile could not be loaded. Please try again.");
    protected override void OnLoadStateChanged(bool failed) => _body.IsVisible = !failed;

    private bool CanUpdate => DoctorExtraRules.CanUpdatePassword(_current.Text, _new.Text, _confirm.Text);

    private void UpdatePwButton()
    {
        if (_updatePw is not null) _updatePw.IsEnabled = CanUpdate;
    }

    private static View Divider() => new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant") };

    protected override async Task LoadAsync()
    {
        _body.Children.Clear();
        var svc = Svc<IDoctorProfileService>();
        if (svc.CurrentDoctorId is null) { _body.Children.Add(DoctorExtraUi.NotLinked()); return; }
        // web: no row → throw → ErrorState
        var p = await svc.GetMineAsync() ?? throw new InvalidOperationException("Profile unavailable");
        _email.Text = p.Email;
        _name.Text = p.FullName; _spec.Text = p.Specialization; _bio.Text = p.Bio;
        _fee.Text = p.ConsultationFee.ToString("0.##", CultureInfo.InvariantCulture);
        _license.Text = p.LicenseNumber; _ptr.Text = p.PtrNumber; _s2.Text = p.S2Number;
        // Admin-only fields: read-only here (deliberate web diff until Jessie decides)
        foreach (var e in new[] { _name, _spec, _fee, _license, _ptr, _s2 }) e.IsReadOnly = true;
        //JJQR - AI (21 Nov 2026) - Jessie (6:30 PM): the 6 Admin-only fields LOOK read-only (muted background + text); Bio stays editable
        foreach (var e in new[] { _name, _spec, _fee, _license, _ptr, _s2 })
        {
            e.BackgroundColor = ClinicTheme.Color("SurfaceContainerLow");
            e.TextColor = ClinicTheme.Color("OnSurfaceVariant");
        }
        //JJQR - END

        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 16, RowSpacing = 16,
        };
        grid.Add(StaffUi.Field("Consultation Fee", _fee), 0, 0);
        grid.Add(StaffUi.Field("License #", _license), 1, 0);
        grid.Add(StaffUi.Field("PTR #", _ptr), 0, 1);
        grid.Add(StaffUi.Field("S2 #", _s2), 1, 1);

        Button? save = null;
        save = StaffUi.Btn("Save profile", async () =>
        {
            StaffUi.Show(_error, null); StaffUi.Show(_saved, null);
            // web handleSave check, same wording
            if (string.IsNullOrWhiteSpace(_name.Text)
                || !decimal.TryParse(string.IsNullOrWhiteSpace(_fee.Text) ? "0" : _fee.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var fee) || fee < 0)
            {
                StaffUi.Show(_error, "Enter your full name and a valid non-negative consultation fee.");
                return;
            }
            if (DoctorExtraUi.DoctorId(Sp) is not { } doctorId) { StaffUi.Show(_error, "Sign in with a doctor account to use this screen."); return; }
            save!.Text = "Saving profile…";
            try
            {
                var r = await Svc<IDoctorBioService>().SaveBioAsync(doctorId, _bio.Text);
                if (!r.Succeeded) { StaffUi.Show(_error, r.Error); return; }
                StaffUi.Show(_saved, $"Profile saved at {DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("h:mm:ss tt", CultureInfo.GetCultureInfo("en-PH"))}.");
            }
            finally { save!.Text = "Save profile"; }
        });

        _updatePw = StaffUi.Btn("Update Password", async () =>
        {
            if (!CanUpdate) return;
            StaffUi.Show(_pwError, null); StaffUi.Show(_pwSaved, null);
            _updatePw!.Text = "Updating…";
            try
            {
                var r = await svc.ChangePasswordAsync(_current.Text, _new.Text, _confirm.Text);
                if (!r.Succeeded) { StaffUi.Show(_pwError, r.Error ?? "Could not update the password."); return; }
                _current.Text = _new.Text = _confirm.Text = "";
                StaffUi.Show(_pwSaved, "Password updated.");
            }
            finally { _updatePw!.Text = "Update Password"; }
        });

        var header = new VerticalStackLayout { Spacing = 8, Children = { ClinicTheme.Text("Professional details", "HeadlineMd"), _email } };
        var availability = new VerticalStackLayout
        {
            Spacing = 8,
            Children = { ClinicTheme.Text("Clinic availability", "MutedBody"), StaffUi.Link("Manage availability", () => StaffUi.PushAsync<DoctorSchedulePage>(this)) },
        };
        var password = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                ClinicTheme.Text("Change Password", "HeadlineSm"), _pwSaved, _pwError,
                StaffUi.Field("Current Password", _current), StaffUi.Field("New Password", _new), StaffUi.Field("Confirm Password", _confirm),
                _updatePw,
            },
        };

        //JJQR - AI (21 Nov 2026) - one muted note under Full Name / Specialization and one under the fee / license / PTR / S2 grid
        static Label AdminNote() => ClinicTheme.Text("Contact the admin to change this.", "MutedSm");
        _body.Children.Add(StaffUi.Card(
            _saved, header, Divider(), _error,
            StaffUi.Field("Full Name", _name), StaffUi.Field("Specialization", _spec), AdminNote(), StaffUi.Field("Bio", _bio),
            grid, AdminNote(), save,
        //JJQR - END
            Divider(), availability,
            Divider(), password));
        UpdatePwButton();
    }
}
//JJQR - END
//JJQR - END
