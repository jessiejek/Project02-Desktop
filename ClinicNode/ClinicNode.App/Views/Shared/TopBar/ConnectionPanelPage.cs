//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.5: the connection panel. Opened from the mode pill on the top bar.
// Status word and reason, a chart of the last 60 checks, last / median / worst time, last contact, database health, changes waiting, last sync (Manila time),
// the mode switch and Sync now. While it is open the monitor checks every 5 seconds.
using ClinicNode.Api.Http;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.Sync;
using ClinicNode.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.App.Views.Shared;

public static class ConnectionColors
{
    public static string KeyOf(ConnectionQuality q) => q switch
    {
        ConnectionQuality.Good => "StatusSuccess",
        ConnectionQuality.Slow or ConnectionQuality.Unstable => "StatusWarning",
        ConnectionQuality.Down => "StatusDanger",
        _ => "OnSurfaceVariant",
    };

    public static string Word(ConnectionQuality q) => q switch
    {
        ConnectionQuality.Good => "Good",
        ConnectionQuality.Slow => "Slow",
        ConnectionQuality.Unstable => "Unstable",
        ConnectionQuality.Down => "Down",
        _ => "Checking...",
    };

    public static string Manila(DateTimeOffset? at) => at is null ? "never" : at.Value.ToOffset(ClinicApp.Domain.ClinicClock.Offset).ToString("MMM d, h:mm:ss tt") + " (Manila)";
}

/// <summary>One thin bar per check: height = time taken (capped at 1.5 s), red full-height bar = failed.</summary>
public sealed class SampleChartDrawable : IDrawable
{
    public IReadOnlyList<HealthSample> Samples { get; set; } = [];

    public void Draw(ICanvas canvas, RectF area)
    {
        canvas.FillColor = ClinicTheme.Color("SurfaceContainerLow");
        canvas.FillRectangle(area);
        var n = ConnectionMonitor.MaxSamples;
        var slot = area.Width / n;
        var barW = Math.Max(2f, slot - 2f);
        var good = ClinicTheme.Color("StatusSuccess"); var warn = ClinicTheme.Color("StatusWarning"); var bad = ClinicTheme.Color("StatusDanger");
        var list = Samples;
        for (var i = 0; i < list.Count; i++)
        {
            var s = list[i];
            var x = (n - list.Count + i) * slot + 1f;   // newest on the right
            float h; Color c;
            if (!s.Ok) { h = area.Height; c = bad; }
            else { h = Math.Max(3f, (float)Math.Min(s.Ms, 1500) / 1500f * area.Height); c = s.Ms >= ConnectionRules.SlowMs ? warn : good; }
            canvas.FillColor = c;
            canvas.FillRectangle(x, area.Height - h, barW, h);
        }
    }
}

public sealed class ConnectionPanelPage : ContentPage
{
    private readonly IServiceProvider _sp;
    private readonly IConnectionMonitor _monitor;
    private readonly IWorkModeService _mode;
    private readonly ISyncStatus _sync;
    private readonly Label _word = new() { FontSize = 22, FontFamily = ClinicTheme.FontSemiBold };
    private readonly Label _reason = ClinicTheme.Text("", "MutedSm");
    private readonly SampleChartDrawable _drawable = new();
    private readonly GraphicsView _chart = new() { HeightRequest = 90 };
    private readonly VerticalStackLayout _facts = new() { Spacing = 6 };
    private readonly Label _modeNote = ClinicTheme.Text("", "MutedSm");
    private Button _modeBtn = null!;
    private Button _syncBtn = null!;
    private readonly Label _syncMsg = ClinicTheme.Text("", "MutedSm");

    public ConnectionPanelPage(IServiceProvider sp)
    {
        _sp = sp;
        _monitor = sp.GetRequiredService<IConnectionMonitor>();
        _mode = sp.GetRequiredService<IWorkModeService>();
        _sync = sp.GetRequiredService<ISyncStatus>();
        Title = "Connection";
        BackgroundColor = Colors.Transparent;
        ControlTemplate = null;
        _chart.Drawable = _drawable;

        _modeBtn = StaffUi.Btn("", async () => await SwitchModeAsync(), "GhostButton");
        _syncBtn = StaffUi.Btn("Sync now", async () => await SyncNowAsync(), "PrimaryButton");
        var buttons = new HorizontalStackLayout { Spacing = 10, Children = { _modeBtn, _syncBtn } };
        var body = new VerticalStackLayout
        {
            Spacing = 12,
            Children = { _word, _reason, _chart, ClinicTheme.Text("Last 60 checks, newest on the right. Red = no answer.", "MutedSm"), _facts, _modeNote, buttons, _syncMsg },
        };
        Content = ClinicModal.Build("Connection", body, null, () => Navigation.PopModalAsync(), 560, 640);
        Render();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _monitor.PanelOpen = true;                    // check every 5 seconds while this is open
        _monitor.Changed += OnChanged; _mode.Changed += OnMode; _sync.PropertyChanged += OnSync;
        Render();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _monitor.PanelOpen = false;
        _monitor.Changed -= OnChanged; _mode.Changed -= OnMode; _sync.PropertyChanged -= OnSync;
    }

    void OnChanged() => MainThread.BeginInvokeOnMainThread(Render);
    void OnMode(object? s, WorkModeInfo i) => MainThread.BeginInvokeOnMainThread(Render);
    void OnSync(object? s, System.ComponentModel.PropertyChangedEventArgs e) => MainThread.BeginInvokeOnMainThread(Render);

    void Render()
    {
        var st = _monitor.Status;
        var samples = _monitor.Samples;
        _word.Text = ConnectionColors.Word(st.Quality);
        _word.TextColor = ClinicTheme.Color(ConnectionColors.KeyOf(st.Quality));
        _reason.Text = st.Reason;
        _drawable.Samples = samples;
        _chart.Invalidate();

        var n = ConnectionPanelModel.Compute(samples);
        _facts.Children.Clear();
        void Row(string k, string v)
        {
            var g = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(190), new ColumnDefinition(GridLength.Star) } };
            g.Add(ClinicTheme.Text(k, "MutedSm"), 0, 0);
            g.Add(new Label { Text = v, FontSize = 13, TextColor = ClinicTheme.Color("OnSurface"), LineBreakMode = LineBreakMode.WordWrap }, 1, 0);
            _facts.Children.Add(g);
        }
        string ms(int? v) => v is null ? "-" : v + " ms";
        Row("Work mode", _mode.Mode == WorkMode.Offline ? "Offline mode" : "Online");
        Row("Last check / typical / worst", $"{ms(n.LastMs)} / {ms(n.MedianMs)} / {ms(n.WorstMs)}");
        Row("Checks failed", $"{n.Failed} of {n.Checks}");
        Row("Last contact with the server", ConnectionColors.Manila(n.LastContactAt));
        Row("Database on the server", n.Database);
        Row("Changes waiting to sync", _sync.PendingCount.ToString());
        Row("Last successful sync", ConnectionColors.Manila(_sync.LastSyncedAt));

        var offline = _mode.Mode == WorkMode.Offline;
        _modeBtn.Text = offline ? "Switch to Online" : "Switch to Offline mode";
        _modeBtn.IsEnabled = _mode.CanSwitch;
        _modeNote.Text = _mode.CanSwitch ? "" : "Only the doctor can switch the work mode.";
    }

    async Task SwitchModeAsync()
    {
        var target = _mode.Mode == WorkMode.Offline ? WorkMode.Online : WorkMode.Offline;
        //JJQR - AI (Nov 22, 2026) - step 3.5: going back Online syncs what is waiting first, and asks if some is still waiting
        if (target == WorkMode.Online && !await SyncGuard.ConfirmLeavingAsync(_sp, "switch", "Switch anyway, items stay queued")) return;
        //JJQR - END
        var r = await _mode.SetAsync(target);
        if (!r.Succeeded) await DisplayAlertAsync("Can't switch", r.Error ?? "", "OK");
        Render();
    }

    async Task SyncNowAsync()
    {
        _syncBtn.IsEnabled = false; _syncMsg.Text = "";
        try { await SyncResultPage.ShowAsync(_sp, "manual"); }   // step 3.2: the run and its result are shown on the results screen
        finally { _syncBtn.IsEnabled = true; Render(); }
    }
}
//JJQR - END
