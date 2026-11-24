//JJQR - AI (Nov 24, 2026) - owner: "make this radio, then others enable textarea". The doctor's Cancel booking dialog (Today, Appointments, appointment page):
// the reason is picked from radio choices; "Others" turns on the text box (5+ characters). Same look and behaviour as StaffDialog: the commit callback
// returns an error to keep the dialog open, or null to close. Picking a choice only enables / disables the box (nothing is redrawn inside the radio's own event: Windows).
using ClinicNode.App.Controls;

namespace ClinicNode.App.Views.Staff;

public sealed class CancelReasonDialog : ContentPage
{
    public static readonly string[] Choices = ["Patient left", "Patient changed their mind", "Duplicate entry"];
    public const string Others = "Others";

    private readonly TaskCompletionSource<bool> _done = new();

    CancelReasonDialog(string message, Func<string, Task<string?>> commit)
    {
        Title = "Cancel booking?";
        BackgroundColor = Colors.Transparent;
        ControlTemplate = null;
        Shell.SetPresentationMode(this, PresentationMode.ModalNotAnimated);

        var error = StaffUi.Error();
        var other = new Editor { Placeholder = "Type the reason (at least 5 characters)", HeightRequest = 80, IsEnabled = false, Opacity = 0.5 };
        string? picked = null;

        var radios = new VerticalStackLayout { Spacing = 2 };
        foreach (var choice in Choices.Append(Others))
        {
            var rb = new RadioButton { Content = choice, GroupName = "cancel-reason", Value = choice };
            rb.CheckedChanged += (_, e) =>
            {
                if (!e.Value) return;
                picked = choice;
                var isOther = choice == Others;
                other.IsEnabled = isOther;
                other.Opacity = isOther ? 1 : 0.5;
                StaffUi.Show(error, null);
                if (isOther) Dispatcher.Dispatch(() => other.Focus()); // after the click finished (Windows)
            };
            radios.Children.Add(rb);
        }

        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Children = { error, ClinicTheme.Text(message, "BodyMd"), ClinicTheme.Text("Reason", "FieldLabel"), radios, other },
        };

        var back = StaffUi.Btn("Keep booking", async () => { _done.TrySetResult(false); await Navigation.PopModalAsync(); }, "SecondaryButton");
        var ok = StaffUi.Btn("Cancel booking", async () =>
        {
            StaffUi.Show(error, null);
            if (picked is null) { StaffUi.Show(error, "Choose a reason."); return; }
            var reason = picked == Others ? (other.Text ?? "").Trim() : picked;
            if (picked == Others && reason.Length < 5) { StaffUi.Show(error, "Type the reason (at least 5 characters)."); return; }
            var problem = await commit(reason);
            if (problem is not null) { StaffUi.Show(error, problem); return; }
            _done.TrySetResult(true);
            await Navigation.PopModalAsync();
        }, "DangerButton");
        var footer = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { back, ok } };
        Content = ClinicModal.Build("Cancel booking?", body, footer, async () => { _done.TrySetResult(false); await Navigation.PopModalAsync(); });
    }

    /// <summary>true when the booking was cancelled.</summary>
    public static async Task<bool> ShowAsync(Page from, string message, Func<string, Task<string?>> commit)
    {
        var d = new CancelReasonDialog(message, commit);
        await from.Navigation.PushModalAsync(d);
        return await d._done.Task;
    }

    protected override bool OnBackButtonPressed()
    {
        _done.TrySetResult(false);
        return base.OnBackButtonPressed();
    }
}
//JJQR - END
