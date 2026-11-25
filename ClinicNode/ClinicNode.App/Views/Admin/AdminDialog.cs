//JJQR - AI (21 Nov 2026) - admin-local copy of the shared ClinicDialog (Views/Shared/Controls/ClinicUi.cs, not edited) whose confirm button
// can be relabelled while it is open, e.g. the announcements modal: "Post announcement" ⇄ "Save as hidden" follows the "Show on dashboards now" box.
using ClinicNode.App.Controls;

namespace ClinicNode.App.Views.Admin;

public sealed class AdminDialog : ContentPage
{
    readonly TaskCompletionSource<bool> _result = new();

    /// <summary>The confirm button; change its Text while the dialog is open.</summary>
    public Button ConfirmButton { get; }

    // booking-detail modals (web Modal + submitModal): the confirm runs the write while the dialog stays open,
    // shows the confirm's busy text, and a failure appears as a FormAlert inside the dialog instead of closing it.
    Func<Task<string?>>? _submit;
    string? _busyText;
    Button? _cancelButton;
    Button? _closeButton;
    readonly Border _errorBox;
    readonly Label _errorLabel;

    AdminDialog(string title, View body, string confirm, string? cancel, bool danger)
    {
        ControlTemplate = null; //JJQR - AI (Nov 21, 2026) - dialogs keep the full-window backdrop, no centred column //JJQR - END
        BackgroundColor = Colors.Transparent;
        Shell.SetPresentationMode(this, PresentationMode.ModalNotAnimated);
        //JJQR - AI (Nov 21, 2026) - shared modal chrome
        ConfirmButton = new Button { Text = confirm, Style = ClinicTheme.Style(danger ? "DangerButton" : "PrimaryButton") };
        ConfirmButton.Clicked += async (_, _) => await ConfirmButton.RunBusyAsync(ConfirmAsync);
        _errorLabel = new Label { Style = ClinicTheme.Style("FormAlertErrorText") };
        _errorBox = new Border { Style = ClinicTheme.Style("FormAlertError"), Content = _errorLabel, IsVisible = false, Margin = new Thickness(0, 0, 0, 12) };
        var footer = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End };
        if (cancel is not null)
        {
            var no = new Button { Text = cancel, Style = ClinicTheme.Style("SecondaryButton") };
            no.Clicked += async (_, _) => await DoneAsync(false);
            _cancelButton = no;
            footer.Children.Add(no);
        }
        footer.Children.Add(ConfirmButton);
        Content = ClinicModal.Build(title, new VerticalStackLayout { Children = { _errorBox, body } }, footer, () => _submitting ? Task.CompletedTask : DoneAsync(false)); //JJQR - AI (Nov 21, 2026) - no closing while a save is running //JJQR - END
        //JJQR - END
    }

    protected override bool OnBackButtonPressed() { if (!_submitting) _ = DoneAsync(false); return true; }

    bool _submitting;

    async Task ConfirmAsync()
    {
        if (_submit is null) { await DoneAsync(true); return; }
        if (_submitting) return;
        _submitting = true;
        var label = ConfirmButton.Text;
        var wasEnabled = ConfirmButton.IsEnabled;
        ConfirmButton.IsEnabled = false;
        if (_cancelButton is not null) _cancelButton.IsEnabled = false;
        if (_closeButton is not null) _closeButton.IsEnabled = false;
        if (_busyText is not null) ConfirmButton.Text = _busyText;
        _errorBox.IsVisible = false;
        string? error;
        try { error = await _submit(); }
        catch (Exception ex) { error = string.IsNullOrWhiteSpace(ex.Message) ? "The change could not be saved. Try again." : ex.Message; }
        _submitting = false;
        if (error is null) { await DoneAsync(true); return; }
        ConfirmButton.Text = label;
        ConfirmButton.IsEnabled = wasEnabled;
        if (_cancelButton is not null) _cancelButton.IsEnabled = true;
        if (_closeButton is not null) _closeButton.IsEnabled = true;
        _errorLabel.Text = error;
        _errorBox.IsVisible = true;
    }

    private bool _closing; //JJQR - AI (Nov 25, 2026) - closing: a second click does nothing //JJQR - END

    async Task DoneAsync(bool value)
    {
        //JJQR - AI (Nov 25, 2026) - owner: close first, then answer (the caller used to redraw / navigate while this pop-up was still on screen)
        if (_result.Task.IsCompleted || _closing) return;
        _closing = true;
        try { if (Navigation.ModalStack.Contains(this)) await Navigation.PopModalAsync(false); } catch (Exception ex) { ClinicNode.Core.Diagnostics.ClinicErrorLog.Write("Dialog.Close", ex); }
        _result.TrySetResult(value);
        //JJQR - END
    }

    /// <summary>Web-style modal: <paramref name="submit"/> runs on confirm and returns null on success (dialog closes, result true)
    /// or an error message (shown inside the dialog, which stays open). <paramref name="busyText"/> replaces the confirm label while saving.</summary>
    public static async Task<bool> ShowSubmitAsync(Page page, string title, View body, string confirm, Func<Task<string?>> submit, string? cancel = "Cancel", bool danger = false, string? busyText = null, Action<AdminDialog>? onOpen = null)
    {
        var dialog = new AdminDialog(title, body, confirm, cancel, danger) { _submit = submit, _busyText = busyText };
        onOpen?.Invoke(dialog);
        await page.Navigation.PushModalAsync(dialog, false);
        return await dialog._result.Task;
    }

    /// <param name="onOpen">Called with the dialog before it is shown (wire live label changes to <see cref="ConfirmButton"/> here).</param>
    public static async Task<bool> ShowAsync(Page page, string title, View body, string confirm, string? cancel = "Cancel", bool danger = false, Action<AdminDialog>? onOpen = null)
    {
        var dialog = new AdminDialog(title, body, confirm, cancel, danger);
        onOpen?.Invoke(dialog);
        await page.Navigation.PushModalAsync(dialog, false);
        return await dialog._result.Task;
    }
}
//JJQR - END
