//JJQR - AI (Nov 22, 2026) - offline-mode plan step 3.2: the sync results screen. It runs the sync (step 3.1) and then shows: what was sent / accepted / merged / in conflict / refused / received / still waiting,
// every item that needs review (with a button to open the review list), the time in Manila time, and the last 30 runs kept on this computer.
using ClinicNode.Api.Http;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Views.Shared;

public sealed class SyncResultPage : ContentPage
{
    private readonly IServiceProvider _sp;
    private readonly string _trigger;
    private readonly bool _skippable;
    private readonly TaskCompletionSource<SyncCycleResult?> _done = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Label _headline = new() { FontSize = 20, FontFamily = ClinicTheme.FontSemiBold };
    private readonly Label _detail = ClinicTheme.Text("", "MutedSm");
    //JJQR - AI (Nov 23, 2026) - owner: "if we are waiting for sync, show a loading like when the app updates". A bigger spinner, a moving progress bar and a plain sentence while the sync runs (all hidden when it finishes).
    private readonly ActivityIndicator _spinner = new() { IsRunning = true, HeightRequest = 32, WidthRequest = 32, HorizontalOptions = LayoutOptions.Start, Color = ClinicTheme.Color("Primary") };
    private readonly ProgressBar _bar = new() { Progress = 0, ProgressColor = ClinicTheme.Color("Primary"), HorizontalOptions = LayoutOptions.Fill };
    private readonly Label _note = ClinicTheme.Text("Please keep the app open. This usually takes a few seconds.", "MutedSm");
    private bool _running = true;
    //JJQR - AI (Nov 23, 2026) - owner: "be transparent whether we are sending or downloading data". A four-step strip and live numbers from the sync itself.
    private readonly HorizontalStackLayout _steps = new() { Spacing = 14 };
    private readonly Label[] _stepLabels = new Label[4];
    private static readonly string[] StepNames = ["Connect", "Send", "Receive", "Finish"];
    private bool _determinate;
    private ISyncService? _progressSource;
    //JJQR - END
    private readonly VerticalStackLayout _counts = new() { Spacing = 4 };
    private readonly VerticalStackLayout _review = new() { Spacing = 6 };
    private readonly VerticalStackLayout _history = new() { Spacing = 4, IsVisible = false };
    private Button _skip = null!;
    private Button _reviewBtn = null!;
    private Button _closeBtn = null!;
    private Button _historyBtn = null!;
    private bool _started;
    private int _openItems;

    SyncResultPage(IServiceProvider sp, string trigger, bool skippable)
    {
        _sp = sp; _trigger = trigger; _skippable = skippable;
        Title = "Sync";
        BackgroundColor = Colors.Transparent;
        ControlTemplate = null;
        _skip = StaffUi.Btn("Skip", async () => { _cts.Cancel(); await Task.CompletedTask; }, "SecondaryButton");
        _skip.IsVisible = skippable;
        _reviewBtn = StaffUi.Btn("Review items", async () => { await CloseAsync(); await Shell.Current.GoToAsync("sync-conflicts"); }, "SecondaryButton");
        _reviewBtn.IsVisible = false;
        _closeBtn = StaffUi.Btn("Close", CloseAsync);
        _closeBtn.IsVisible = false;
        _historyBtn = StaffUi.Btn("Past syncs", async () => { _history.IsVisible = !_history.IsVisible; await Task.CompletedTask; }, "GhostButton");
        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new HorizontalStackLayout { Spacing = 12, Children = { _spinner, _headline } },
                _bar,
                _steps,
                _detail, _note, _counts,
                _review,
                _historyBtn, _history,
            },
        };
        var footer = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.End, Children = { _skip, _reviewBtn, _closeBtn } };
        Content = ClinicModal.Build("Sync", body, footer, null, 560, 620);
        for (var i = 0; i < 4; i++) { _stepLabels[i] = new Label { FontSize = 12 }; _steps.Children.Add(_stepLabels[i]); }
        SetStep(0);
        _headline.Text = "Connecting to the clinic server";
        //JJQR - AI (Nov 23, 2026) - say what is about to be sent
        var waiting = _sp.GetService<ISyncStatus>()?.PendingCount ?? 0;
        _detail.Text = waiting > 0
            ? $"Sending {waiting} change{(waiting == 1 ? "" : "s")} saved on this computer, then receiving what is new from the server."
            : "Checking the clinic server for anything new.";
        //JJQR - END
    }

    /// <summary>Runs a sync and shows the result. Returns the result, or null when it was skipped.</summary>
    public static async Task<SyncCycleResult?> ShowAsync(IServiceProvider sp, string trigger, bool skippable = false)
    {
        var page = new SyncResultPage(sp, trigger, skippable);
        await Shell.Current.Navigation.PushModalAsync(page);
        return await page._done.Task;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started) return;
        _started = true;
        //JJQR - AI (Nov 23, 2026) - the bar keeps moving while the sync runs (the sync has no percentage, so it sweeps)
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(40), () =>
        {
            if (!_running) return false;
            if (!_determinate) _bar.Progress = (_bar.Progress + 0.02) % 1.0;
            return true;
        });
        //JJQR - AI (Nov 23, 2026) - listen to what the sync is doing right now
        _progressSource = _sp.GetService<ISyncService>();
        if (_progressSource is not null) _progressSource.ProgressChanged += OnProgress;
        //JJQR - END
        //JJQR - END
        SyncCycleResult? result = null;
        try { result = await _sp.GetRequiredService<ISyncService>().RunManualAsync(_trigger, _cts.Token); }
        catch (OperationCanceledException) { /* skipped */ }
        catch (Exception ex) { result = new SyncCycleResult(false, SyncFailureKind.None, ex.Message); }
        await ShowResultAsync(result);
        _done.TrySetResult(result);
    }

    //JJQR - AI (Nov 23, 2026) - paint the four steps: done = tick, current = bold teal, next = grey
    void SetStep(int current)
    {
        for (var i = 0; i < 4; i++)
        {
            var done = i < current; var now = i == current;
            _stepLabels[i].Text = (done ? "✓ " : now ? "● " : "○ ") + StepNames[i];
            _stepLabels[i].TextColor = ClinicTheme.Color(now ? "Primary" : done ? "StatusSuccess" : "OnSurfaceVariant");
            _stepLabels[i].FontFamily = now ? ClinicTheme.FontSemiBold : ClinicTheme.FontMedium;
        }
    }

    void OnProgress(SyncProgress p) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!_running) return;
        var (step, headline) = p.Stage switch
        {
            "connect" => (0, "Connecting to the clinic server"),
            "send" => (1, "Sending data to the clinic server"),
            "receive" => (2, "Downloading data from the clinic server"),
            "apply" => (2, "Saving the downloaded data on this computer"),
            "blocks" => (3, "Reserving queue numbers"),
            _ => (3, "Finishing up"),
        };
        SetStep(step);
        _headline.Text = headline;
        _detail.Text = p.Message;
        _determinate = p.Stage is "send" or "apply" && p.Total > 0;
        if (_determinate) _bar.Progress = Math.Clamp((double)p.Done / p.Total, 0, 1);
    });
    //JJQR - END

    async Task CloseAsync()
    {
        _cts.Cancel();
        try { await Navigation.PopModalAsync(); } catch { /* already closed */ }
        _done.TrySetResult(null);
    }

    async Task ShowResultAsync(SyncCycleResult? r)
    {
        _spinner.IsRunning = false; _spinner.IsVisible = false;
        _running = false; _bar.IsVisible = false; _note.IsVisible = false; _steps.IsVisible = false; //JJQR - AI (Nov 23, 2026) //JJQR - END
        if (_progressSource is not null) { _progressSource.ProgressChanged -= OnProgress; _progressSource = null; }
        _skip.IsVisible = false; _closeBtn.IsVisible = true;
        _counts.Children.Clear();
        if (r is null) { _headline.Text = "Skipped"; _headline.TextColor = ClinicTheme.Color("OnSurface"); _detail.Text = "Nothing was lost: what was not sent yet is still safe on this computer."; }
        else if (!r.Succeeded)
        {
            _headline.Text = "Sync did not finish"; _headline.TextColor = ClinicTheme.Color("StatusDanger");
            _detail.Text = (r.Error ?? "Something went wrong.") + (r.StillWaiting > 0 ? $"  {r.StillWaiting} change{(r.StillWaiting == 1 ? " is" : "s are")} still waiting on this computer." : "");
        }
        else
        {
            _headline.Text = "Sync finished"; _headline.TextColor = ClinicTheme.Color("StatusSuccess");
            _detail.Text = ConnectionColors.Manila(r.FinishedAt);
            void Row(string k, int v, bool warn = false)
            {
                var g = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(230), new ColumnDefinition(GridLength.Star) } };
                g.Add(ClinicTheme.Text(k, "MutedSm"), 0, 0);
                g.Add(new Label { Text = v.ToString(), FontSize = 13, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color(warn && v > 0 ? "StatusWarning" : "OnSurface") }, 1, 0);
                _counts.Children.Add(g);
            }
            Row("Sent to the clinic server", r.Pushed);
            Row("Accepted as sent", r.Accepted);
            Row("Accepted, with some server values kept", r.Merged, warn: true);
            Row("Clashed with something newer", r.Conflicts, warn: true);
            Row("Refused by the server", r.Rejected, warn: true);
            Row("Received from the server", r.Received);
            Row("Still waiting on this computer", r.StillWaiting, warn: true);
        }

        // items that need review (the review list is the existing one; this shows the newest)
        _review.Children.Clear();
        try
        {
            var open = await _sp.GetRequiredService<ISyncStatus>().GetOpenConflictsAsync();
            _openItems = open.Count;
            if (open.Count > 0)
            {
                _review.Children.Add(ClinicTheme.Text($"Needs review: {open.Count} item{(open.Count == 1 ? "" : "s")}", "LabelMd"));
                foreach (var c in open.Take(6))
                    _review.Children.Add(ClinicTheme.Text($"{c.Aggregate} · {c.Message ?? c.Kind}", "MutedSm"));
                if (open.Count > 6) _review.Children.Add(ClinicTheme.Text($"... and {open.Count - 6} more", "MutedSm"));
            }
            _reviewBtn.IsVisible = open.Count > 0;
        }
        catch { /* the list is optional here */ }

        // the last 30 runs
        _history.Children.Clear();
        try
        {
            var runs = await _sp.GetRequiredService<ISyncService>().GetRunHistoryAsync();
            if (runs.Count == 0) _history.Children.Add(ClinicTheme.Text("No earlier syncs on this computer.", "MutedSm"));
            foreach (var run in runs)
            {
                var when = run.FinishedAt.ToOffset(ClinicApp.Domain.ClinicClock.Offset).ToString("MMM d, h:mm tt");
                var line = run.Succeeded
                    ? $"{when} · {run.Trigger} · sent {run.Sent}, accepted {run.Accepted}, review {run.NewReviewItems}, received {run.Received}"
                    : $"{when} · {run.Trigger} · did not finish: {run.Error}";
                _history.Children.Add(ClinicTheme.Text(line, "MutedSm"));
            }
        }
        catch { /* optional */ }
    }
}
//JJQR - END
