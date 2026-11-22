//JJQR - AI (Nov 22, 2026) - owner request: viewer for the developer network log (see DevNetworkLog). Opened from the Connection panel; only offered when the log is on.
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.Diagnostics;

namespace ClinicNode.App.Views.Shared;

public sealed class DevLogPage : ContentPage
{
    private readonly VerticalStackLayout _rows = new() { Spacing = 2 };
    private readonly Label _info = ClinicTheme.Text("", "MutedSm");

    public DevLogPage()
    {
        Title = "Developer log";
        BackgroundColor = Colors.Transparent;
        ControlTemplate = null;
        var copy = StaffUi.Btn("Copy all", async () =>
        {
            await Clipboard.Default.SetTextAsync(DevNetworkLog.AsText(ClinicApp.Domain.ClinicClock.Offset));
            _info.Text = "Copied. Paste it into the chat.";
        }, "PrimaryButton");
        var clear = StaffUi.Btn("Clear", () => { DevNetworkLog.Clear(); return Task.CompletedTask; }, "GhostButton");
        var body = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                ClinicTheme.Text("Every call this app makes to the clinic server, newest first. Passwords, tokens and request contents are never recorded. Kept in memory only.", "MutedSm"),
                new HorizontalStackLayout { Spacing = 10, Children = { copy, clear } },
                _info,
                new ScrollView { HeightRequest = 440, Content = _rows },
            },
        };
        Content = ClinicModal.Build("Developer log", body, null, () => Navigation.PopModalAsync(), 900, 620);
        Render();
    }

    protected override void OnAppearing() { base.OnAppearing(); DevNetworkLog.Changed += OnChanged; Render(); }
    protected override void OnDisappearing() { base.OnDisappearing(); DevNetworkLog.Changed -= OnChanged; }
    void OnChanged() => MainThread.BeginInvokeOnMainThread(Render);

    void Render()
    {
        _rows.Children.Clear();
        var list = DevNetworkLog.Snapshot();
        if (list.Count == 0) { _rows.Children.Add(ClinicTheme.Text("Nothing yet. Use the app and calls will appear here.", "MutedSm")); return; }
        foreach (var e in list.Take(200))
        {
            var key = e.Status == 0 || e.Status >= 500 ? "StatusDanger" : e.Status >= 400 ? "StatusWarning" : "OnSurface";
            _rows.Children.Add(new Label
            {
                Text = DevNetworkLog.Line(e, ClinicApp.Domain.ClinicClock.Offset),
                FontFamily = "Menlo", FontSize = 11, TextColor = ClinicTheme.Color(key), LineBreakMode = LineBreakMode.CharacterWrap,
            });
        }
    }
}
//JJQR - END
