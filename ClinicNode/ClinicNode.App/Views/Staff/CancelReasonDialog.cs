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
    private bool _working; //JJQR - AI (Nov 25, 2026) - the cancel is being sent //JJQR - END

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

        var back = StaffUi.Btn("Keep booking", () => _working ? Task.CompletedTask : ModalClose.ThenAnswerAsync(this, _done, false), "SecondaryButton"); //JJQR - AI (Nov 25, 2026) - close first, then answer; not while cancelling //JJQR - END
        var ok = StaffUi.Btn("Cancel booking", async () =>
        {
            StaffUi.Show(error, null);
            if (picked is null) { StaffUi.Show(error, "Choose a reason."); return; }
            var reason = picked == Others ? (other.Text ?? "").Trim() : picked;
            if (picked == Others && reason.Length < 5) { StaffUi.Show(error, "Type the reason (at least 5 characters)."); return; }
            //JJQR - AI (Nov 25, 2026) - while the cancel is being sent, "Keep booking" / X / Back do nothing (they would say "kept" for a booking that gets cancelled)
            string? problem;
            _working = true; back.IsEnabled = false;
            try { problem = await commit(reason); }
            finally { _working = false; back.IsEnabled = true; }
            //JJQR - END
            if (problem is not null) { StaffUi.Show(error, problem); return; }
            await ModalClose.ThenAnswerAsync(this, _done, true); //JJQR - AI (Nov 25, 2026) - close first, then answer //JJQR - END
        }, "DangerButton");
        var footer = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { back, ok } };
        Content = ClinicModal.Build("Cancel booking?", body, footer, () => _working ? Task.CompletedTask : ModalClose.ThenAnswerAsync(this, _done, false)); //JJQR - AI (Nov 25, 2026) - close first, then answer //JJQR - END
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
        if (_working) return true; //JJQR - AI (Nov 25, 2026) //JJQR - END
        _done.TrySetResult(false);
        return base.OnBackButtonPressed();
    }
}
//JJQR - END
