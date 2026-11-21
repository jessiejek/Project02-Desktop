// JJQR - AI (Nov 21, 2026) - admin 'Register this node' calls ISyncNodeRegistration.RegisterAsync.
using ClinicNode.App.Controls;
using ClinicNode.Sync.Wire;

namespace ClinicNode.App.Views.Admin;

public sealed class AdminRegisterNodePage : ContentPage
{
    private readonly ISyncNodeRegistration _registration;
    private readonly Entry _jwt = new() { IsPassword = true, Placeholder = "Admin access token" };
    private readonly Entry _name = new() { Placeholder = "Name for this computer" };
    private readonly Entry _platform = new();
    private readonly Entry _version = new();
    private readonly Label _status = new() { Style = ClinicTheme.Style("MutedBody") };
    private readonly Label _error = new() { Style = ClinicTheme.Style("ErrorText"), IsVisible = false };
    private readonly Button _submit = new() { Text = "Register this node" };

    //JJQR - AI (21 Nov 2026) - when the admin is signed in online, the session token is used and the token box can stay empty (never shown)
    private readonly ClinicNode.Api.Http.IApiTokenStore? _session;

    public AdminRegisterNodePage(ISyncNodeRegistration registration, ClinicNode.Api.Http.IApiTokenStore? session = null)
    {
        _session = session;
        _jwt.Placeholder = "Admin access token (leave empty to use your online sign-in)";
    //JJQR - END
        _registration = registration;
        Title = "Register this node";
        BackgroundColor = ClinicTheme.Color("Background");
        _name.Text = string.IsNullOrWhiteSpace(DeviceInfo.Current.Name) ? "Clinic Node" : DeviceInfo.Current.Name;
        _platform.Text = $"{DeviceInfo.Current.Platform} {DeviceInfo.Current.VersionString}".Trim();
        _version.Text = AppInfo.Current.VersionString;
        _submit.Clicked += async (_, _) => await RegisterAsync();

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 12,
                MaximumWidthRequest = 640,
                Children =
                {
                    ClinicTheme.Text("Register this node", "HeadlineMd"),
                    ClinicTheme.Text("Use a cloud admin access token to register this computer for sync. The node secret is stored in the OS credential vault.", "MutedBody"),
                    _status,
                    ClinicTheme.Text("Admin access token", "FieldLabel"),
                    _jwt,
                    ClinicTheme.Text("Computer name", "FieldLabel"),
                    _name,
                    ClinicTheme.Text("Platform", "FieldLabel"),
                    _platform,
                    ClinicTheme.Text("App version", "FieldLabel"),
                    _version,
                    _error,
                    _submit,
                }
            }
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadStatusAsync();
    }

    async Task LoadStatusAsync()
    {
        try
        {
            _status.Text = await _registration.IsRegisteredAsync()
                ? "This computer already has a node secret. Registering again will replace it."
                : "This computer is not registered for cloud sync yet.";
        }
        catch (Exception ex)
        {
            _status.Text = "Could not read registration status: " + ex.Message;
        }
    }

    async Task RegisterAsync()
    {
        _error.IsVisible = false;
        var token = _jwt.Text?.Trim() ?? "";
        //JJQR - AI (21 Nov 2026)
        if (string.IsNullOrWhiteSpace(token)) token = _session?.AccessToken ?? "";
        //JJQR - END
        var name = _name.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(name))
        {
            //JJQR - AI (21 Nov 2026)
            _error.Text = "Sign in online as an admin (or paste an admin access token) and enter a computer name.";
            //JJQR - END
            _error.IsVisible = true;
            return;
        }

        _submit.IsEnabled = false;
        _submit.Text = "Registering…";
        try
        {
            var result = await _registration.RegisterAsync(token, name, NullIfBlank(_platform.Text), NullIfBlank(_version.Text));
            if (result.Succeeded)
            {
                _status.Text = result.NodeId is { } id
                    ? $"Registered. Node id: {id}"
                    : "Registered.";
                _jwt.Text = "";
            }
            else
            {
                _error.Text = result.Error ?? "Registration failed.";
                _error.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            _error.Text = ex.Message;
            _error.IsVisible = true;
        }
        finally
        {
            _submit.IsEnabled = true;
            _submit.Text = "Register this node";
        }
    }

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
// JJQR - END
