//JJQR - AI (21 Nov 2026) - "Reset your password", 1:1 with Project02-fe app/forgot-password/page.tsx inside components/auth/AuthShell.tsx (light theme):
// brand link → card (rounded-xl, p-xl) with h1 + description (mb-lg) → form space-y-md: FormAlert (success, identical for every address) → Email → full-width button
// ("Send reset link" / "Sending link…" / "Send another link") → footer "Back to log in" (body-md medium primary, centred, mt-md).
// Desktop only: online required (offline shows the "needs internet" FormAlert and sends nothing). Nothing is logged; the email is never stored here.
using ClinicNode.Api.Auth;
using ClinicNode.Api.Http;
using ClinicNode.App.Controls;

namespace ClinicNode.App.Views.Shared.Auth;

public sealed class ForgotPasswordPage : ContentPage
{
    private readonly IPasswordResetClient _reset;
    private readonly IApiConnectivity _connectivity;
    private readonly Entry _email = new() { Keyboard = Keyboard.Email, IsSpellCheckEnabled = false, IsTextPredictionEnabled = false, ReturnType = ReturnType.Send };
    private readonly Button _send = new() { Text = "Send reset link", HorizontalOptions = LayoutOptions.Fill };
    private readonly ActivityIndicator _busy = new() { IsVisible = false, IsRunning = false, HeightRequest = 20 };
    private readonly Border _successBox;
    private readonly Label _successLabel = new() { FontFamily = "Inter", FontSize = 14, LineBreakMode = LineBreakMode.WordWrap };
    private readonly Border _errorBox;
    private readonly Label _errorLabel = new() { FontFamily = "Inter", FontSize = 14, LineBreakMode = LineBreakMode.WordWrap };

    public ForgotPasswordPage(IPasswordResetClient reset, IApiConnectivity connectivity, string? email = null)
    {
        _reset = reset;
        _connectivity = connectivity;
        Title = "Reset your password";
        Shell.SetFlyoutBehavior(this, FlyoutBehavior.Disabled);
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = ClinicTheme.Color("SurfaceContainerLow");
        _email.Text = email ?? "";
        SemanticProperties.SetDescription(_email, "Email");
        _send.Clicked += async (_, _) => await _send.RunBusyAsync(SendAsync);
        _email.Completed += async (_, _) => await SendAsync();
        _busy.Color = ClinicTheme.Color("Primary");

        _successBox = FormAlert("success", _successLabel);
        _errorBox = FormAlert("danger", _errorLabel);

        var brand = ClinicTheme.Text("Dr. Grace Gavino Medical Clinic", "BrandLink");
        brand.MinimumHeightRequest = 44;
        brand.VerticalTextAlignment = TextAlignment.Center;
        brand.HorizontalOptions = LayoutOptions.Start;

        var back = new Label
        {
            Text = "Back to log in",
            FontFamily = "InterMedium",
            FontSize = 14,
            TextColor = ClinicTheme.Color("Primary"),
            HorizontalOptions = LayoutOptions.Center,
            MinimumHeightRequest = 44,
            VerticalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 16, 0, 0),
        };
        back.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PopModalAsync()) });

        var title = ClinicTheme.Text("Reset your password", "HeadlineLgMobile");
        SemanticProperties.SetHeadingLevel(title, SemanticHeadingLevel.Level1);

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 32),
                WidthRequest = 416,
                MaximumWidthRequest = 416,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    brand,
                    new Border
                    {
                        Style = ClinicTheme.Style("AuthCard"),
                        Padding = new Thickness(32),
                        Content = new VerticalStackLayout
                        {
                            Children =
                            {
                                new VerticalStackLayout { Spacing = 4, Margin = new Thickness(0, 0, 0, 24), Children =
                                {
                                    title,
                                    ClinicTheme.Text("Enter the email on your account and we'll send a link to choose a new password.", "MutedBody"),
                                } },
                                new VerticalStackLayout { Spacing = 16, Children =
                                {
                                    _successBox,
                                    _errorBox,
                                    new VerticalStackLayout { Spacing = 4, Children =
                                    {
                                        ClinicTheme.Text("Email", "FieldLabel"),
                                        new Border { Style = ClinicTheme.Style("InputFrame"), Content = _email },
                                    } },
                                    _send,
                                    _busy,
                                } },
                            },
                        },
                    },
                    back,
                },
            },
        };
    }

    // components/ui/FormAlert.tsx: rounded-lg border px-md py-sm, icon + body-md, status tokens
    private static Border FormAlert(string tone, Label text)
    {
        var danger = tone == "danger";
        var key = danger ? "StatusDanger" : "StatusSuccess";
        text.TextColor = ClinicTheme.Color(key);
        var icon = new Image
        {
            Source = ClinicIcons.FontImage(danger ? "error" : "check_circle", 16, ClinicTheme.Color(key)),
            WidthRequest = 16,
            HeightRequest = 16,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 2, 0, 0),
        };
        var grid = new Grid { ColumnSpacing = 8, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        grid.Add(icon, 0);
        grid.Add(text, 1);
        var box = new Border
        {
            IsVisible = false,
            StrokeThickness = 1,
            Stroke = ClinicTheme.Color(key + "Border"),
            BackgroundColor = ClinicTheme.Color(key + "Surface"),
            Padding = new Thickness(16, 8),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Content = grid,
        };
        SemanticProperties.SetDescription(box, danger ? "Error" : "Status");
        return box;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await IsOnlineAsync()) ShowError(PasswordResetClient.NeedsInternet);
    }

    private async Task<bool> IsOnlineAsync()
    {
        try { return await _connectivity.EnsureCheckedAsync() || await _connectivity.CheckNowAsync(); }
        catch { return false; }
    }

    private async Task SendAsync()
    {
        if (!_send.IsEnabled) return;
        _errorBox.IsVisible = false;
        _successBox.IsVisible = false;
        var target = (_email.Text ?? "").Trim();
        if (target.Length == 0) { _email.Focus(); return; } // web: required field, no request
        if (!target.Contains('@')) { ShowError("Enter the email on your account."); return; }
        _send.IsEnabled = false;
        _send.Text = "Sending link…";
        _busy.IsVisible = _busy.IsRunning = true;
        try
        {
            await _reset.RequestResetAsync(target);
            ShowSent(target);
        }
        catch (ApiOfflineException) { ShowError(PasswordResetClient.NeedsInternet); _send.Text = "Send reset link"; }
        catch (ApiException) { ShowSent(target); } // same message for every address: never reveals whether an account exists
        finally
        {
            _busy.IsVisible = _busy.IsRunning = false;
            _send.IsEnabled = true;
        }
    }

    private void ShowSent(string target)
    {
        _successLabel.FormattedText = new FormattedString
        {
            Spans =
            {
                new Span { Text = "If an account exists for " },
                new Span { Text = target, FontFamily = "InterMedium" },
                new Span { Text = ", a reset link has been sent. Check your inbox and spam folder." },
            },
        };
        _successBox.IsVisible = true;
        _send.Text = "Send another link";
    }

    private void ShowError(string message) { _errorLabel.Text = message; _errorBox.IsVisible = true; }
}
//JJQR - END
