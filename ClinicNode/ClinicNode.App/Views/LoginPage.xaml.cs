// JJQR - AI (Nov 21, 2026)
using ClinicNode.App.Controls;
using ClinicNode.Core.Auth;
using ClinicNode.Data;

namespace ClinicNode.App.Views;

public partial class LoginPage : ContentPage
{
    private readonly IAuthService _auth;
    private readonly IEmailMemory _emailMemory;
    private readonly NodeOptions _options;
    private readonly LocalDatabase _database;

    public LoginPage(IAuthService auth, IEmailMemory emailMemory, NodeOptions options, LocalDatabase database)
    {
        InitializeComponent();
        _auth = auth;
        _emailMemory = emailMemory;
        _options = options;
        _database = database;
        SignInButton.IsEnabled = false;
        SignInButton.Text = "Preparing…";
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ApplyPasswordGlyph();
        if (_options.Demo)
        {
            DemoBanner.IsVisible = true;
            DemoBanner.Text = "Demo mode: use doctor@clinic.test, staff@clinic.test or admin@clinic.test (development password). Never enable demo on a real clinic computer.";
        }
        var remembered = _auth.RememberedEmail ?? await _emailMemory.GetAsync();
        //JJQR - AI (21 Nov 2026) - web: the box starts ticked; prefill only the email (never a password)
        RememberEmailCheck.IsChecked = true;
        if (!string.IsNullOrWhiteSpace(remembered) && string.IsNullOrWhiteSpace(EmailEntry.Text))
            EmailEntry.Text = remembered;
        ErrorIcon.Source = ClinicIcons.FontImage("error", 16, ClinicTheme.Color("StatusDanger"));
        //JJQR - END

        SignInButton.IsEnabled = false;
        SignInButton.Text = "Preparing…";
        ErrorLabel.IsVisible = false;
        try
        {
            await _database.InitializeAsync();
            if (_database.InitializationError is { } err)
                throw new InvalidOperationException(err);
            SignInButton.Text = "Log in";
            SignInButton.IsEnabled = true;
            //JJQR - AI (21 Nov 2026) - item 1: resume with the saved refresh token (≤ 7 days, online only); otherwise the form stays
            await TryResumeAsync();
            //JJQR - END
        }
        catch (Exception ex)
        {
            ErrorLabel.Text = "Could not open the local clinic database: " + ex.Message;
            ErrorLabel.IsVisible = true;
            SignInButton.Text = "Log in";
            SignInButton.IsEnabled = false;
        }
    }


    //JJQR - AI (21 Nov 2026) - item 1: silent sign-in from the SecureStorage refresh token
    bool _resumeTried;
    async Task TryResumeAsync()
    {
        // owner rule 4: startup catch-up pull + hub/connectivity wiring (idempotent)
        try { IPlatformApplication.Current?.Services.GetService<ClinicNode.App.Shared.LiveSyncCoordinator>()?.Start(); } catch { }
        if (_resumeTried || _auth is not ClinicNode.Api.Auth.IOnlineAuthSession online) return;
        _resumeTried = true; // once per page: after a sign-out the user must type the password
        try
        {
            if (!await online.TryResumeAsync()) return;
            PasswordEntry.Text = "";
            if (Shell.Current is AppShell shell)
                await shell.EnterRoleAsync();
        }
        catch (Exception)
        {
            // resume is a convenience; the form is still there
        }
    }
    //JJQR - END

    void OnTogglePassword(object? sender, EventArgs e)
    {
        PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
        ApplyPasswordGlyph();
    }

    void ApplyPasswordGlyph()
    {
        var hidden = PasswordEntry.IsPassword;
        TogglePassword.Source = ClinicIcons.FontImage(hidden ? "eye" : "eye_slash", ClinicIcons.SizeSm, ClinicTheme.Color("OnSurfaceVariant"));
        SemanticProperties.SetDescription(TogglePassword, hidden ? "Show password" : "Hide password");
    }

    async void OnForgotPassword(object? sender, TappedEventArgs e)
    {
        //JJQR - AI (21 Nov 2026) - item 5: online-only reset page (web /forgot-password); offline → "needs internet"
        var services = IPlatformApplication.Current?.Services;
        var reset = services?.GetService<ClinicNode.Api.Auth.IPasswordResetClient>();
        var connectivity = services?.GetService<ClinicNode.Api.Http.IApiConnectivity>();
        var online = false;
        if (connectivity is not null)
        {
            try { online = await connectivity.EnsureCheckedAsync() || await connectivity.CheckNowAsync(); }
            catch { online = false; }
        }
        if (reset is null || connectivity is null || !online)
        {
            await DisplayAlertAsync("Forgot password?", ClinicNode.Api.Auth.PasswordResetClient.NeedsInternet, "OK");
            return;
        }
        await Navigation.PushModalAsync(new ClinicNode.App.Views.Shared.Auth.ForgotPasswordPage(reset, connectivity, EmailEntry.Text));
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - web parity helpers: label toggles the box, Enter in Email moves to Password, web error copy
    void OnRememberLabelTapped(object? sender, TappedEventArgs e) => RememberEmailCheck.IsChecked = !RememberEmailCheck.IsChecked;

    void OnEmailCompleted(object? sender, EventArgs e) => PasswordEntry.Focus();

    void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    static string WebError(string? error) => error switch
    {
        null or "" => "Incorrect email or password.",
        "Invalid email or password." => "Incorrect email or password.",
        _ => error,
    };
    //JJQR - END

    async void OnSignIn(object? sender, EventArgs e)
    {
        //JJQR - AI (21 Nov 2026) - web form is "required": an empty field just gets focus, no request
        if (!SignInButton.IsEnabled) return;
        if (string.IsNullOrWhiteSpace(EmailEntry.Text)) { EmailEntry.Focus(); return; }
        if (string.IsNullOrEmpty(PasswordEntry.Text)) { PasswordEntry.Focus(); return; }
        //JJQR - END
        ErrorLabel.IsVisible = false;
        Busy.IsVisible = Busy.IsRunning = true;
        SignInButton.Text = "Logging in…";
        SignInButton.IsEnabled = false;
        try
        {
            await _database.InitializeAsync();
            var result = await _auth.LoginAsync(EmailEntry.Text ?? "", PasswordEntry.Text ?? "", RememberEmailCheck.IsChecked);
            if (!result.Succeeded)
            {
                ShowError(WebError(result.Error)); //JJQR - AI (21 Nov 2026) - web copy //JJQR - END
                return;
            }
            PasswordEntry.Text = "";
            if (Shell.Current is AppShell shell)
                await shell.EnterRoleAsync();
        }
        catch (Exception ex)
        {
            //JJQR - AI (21 Nov 2026) - web copy for an unreachable server; never echo the password or a token
            ShowError(ex is ClinicNode.Api.Http.ApiOfflineException
                ? "We couldn't reach the clinic server. Check your connection and try again."
                : "Could not sign in: " + ex.Message);
            //JJQR - END
        }
        finally
        {
            Busy.IsVisible = Busy.IsRunning = false;
            SignInButton.Text = "Log in";
            SignInButton.IsEnabled = true;
        }
    }
}
// JJQR - END
