//JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.5: the connection panel. Opened from the mode pill on the top bar.
// Status word and reason, a chart of the last 60 checks, last / median / worst time, last contact, database health, changes waiting, last sync (Manila time),
// the mode switch and Sync now. While it is open the monitor checks every 5 seconds.
using ClinicNode.Api.Http;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.Diagnostics;
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
    private readonly VerticalStackLayout _facts = new() { Spacing = 8 };
    //JJQR - AI (Nov 23, 2026) - owner: "the UI I don't like". The panel was a long list of small rows with link-style buttons. Now: a status dot, four summary cards, the chart in a card, a short details list, and three real buttons.
    private readonly Microsoft.Maui.Controls.Shapes.Ellipse _dot = new() { WidthRequest = 12, HeightRequest = 12, VerticalOptions = LayoutOptions.Center };
    private readonly Grid _tiles = new() { ColumnSpacing = 12, RowSpacing = 12, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) } };
    //JJQR - END
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

        _modeBtn = StaffUi.Btn("", async () => await SwitchModeAsync(), "SecondaryButton");
        _syncBtn = StaffUi.Btn("Sync now", async () => await SyncNowAsync(), "PrimaryButton");
        var buttons = StaffUi.Wrap(DoctorSpaced(_syncBtn), DoctorSpaced(StaffUi.Btn("Check for updates", async () => { await ClinicNode.App.Views.Shared.UpdateUi.ShowAsync(_sp); Render(); }, "SecondaryButton")), DoctorSpaced(_modeBtn));
        //JJQR - AI (Nov 22, 2026) - owner request: developer log button, only when the log is on (Debug builds)
        if (DevNetworkLog.Enabled) buttons.Children.Add(DoctorSpaced(StaffUi.Btn("Developer log", async () => await Navigation.PushModalAsync(new DevLogPage()), "GhostButton")));
        //JJQR - END
        //JJQR - AI (Nov 23, 2026) - header (dot + word + reason), summary cards, chart card, details, actions
        var header = new VerticalStackLayout { Spacing = 2, Children = { new HorizontalStackLayout { Spacing = 8, Children = { _dot, _word } }, _reason } };
        var chartCard = new Border
        {
            Style = ClinicTheme.Style("Card"), Padding = new Thickness(12),
            Content = new VerticalStackLayout { Spacing = 6, Children = { _chart, ClinicTheme.Text("Last 60 checks, newest on the right. Red = no answer.", "MutedSm") } },
        };
        var body = new VerticalStackLayout
        {
            Spacing = 16,
            Children = { header, _tiles, chartCard, _facts, _modeNote, buttons, _syncMsg },
        };
        //JJQR - END
        Content = ClinicModal.Build("Connection", body, null, () => Navigation.PopModalAsync(), 560, 640);
        Render();
    }

    static View DoctorSpaced(View v) { v.Margin = new Thickness(0, 0, 8, 8); return v; } //JJQR - AI (Nov 23, 2026) - gap between wrapped buttons //JJQR - END

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
        _dot.Fill = new SolidColorBrush(ClinicTheme.Color(ConnectionColors.KeyOf(st.Quality)));
        string ms(int? v) => v is null ? "-" : v + " ms";
        //JJQR - AI (Nov 23, 2026) - summary cards
        View Tile(string label, string value, string sub) => new Border
        {
            Style = ClinicTheme.Style("Card"), Padding = new Thickness(14, 12),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    ClinicTheme.Text(label, "MutedSm"),
                    new Label { Text = value, FontSize = 20, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("OnSurface"), LineBreakMode = LineBreakMode.TailTruncation },
                    ClinicTheme.Text(sub, "MutedSm"),
                },
            },
        };
        var pending = _sync.PendingCount;
        var lastSync = _sync.LastSyncedAt?.ToOffset(ClinicApp.Domain.ClinicClock.Offset);
        _tiles.Children.Clear();
        _tiles.RowDefinitions.Clear();
        _tiles.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); _tiles.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _tiles.Add(Tile("Response time", ms(n.MedianMs), $"last {ms(n.LastMs)} · worst {ms(n.WorstMs)}"), 0, 0);
        _tiles.Add(Tile("Changes waiting", pending.ToString(), pending == 0 ? "everything is saved on the server" : "waiting to be sent"), 1, 0);
        _tiles.Add(Tile("Last sync", lastSync is null ? "never" : lastSync.Value.ToString("h:mm tt"), lastSync is null ? "" : lastSync.Value.ToString("MMM d") + " (Manila)"), 0, 1);
        _tiles.Add(Tile("Work mode", _mode.Mode == WorkMode.Offline ? "Offline" : "Online", _mode.Mode == WorkMode.Offline ? "saved on this computer" : "connected to the server"), 1, 1);
        //JJQR - END

        _facts.Children.Clear();
        void Row(string k, string v)
        {
            var g = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(170), new ColumnDefinition(GridLength.Star) } };
            g.Add(ClinicTheme.Text(k, "MutedSm"), 0, 0);
            g.Add(new Label { Text = v, FontSize = 13, TextColor = ClinicTheme.Color("OnSurface"), LineBreakMode = LineBreakMode.WordWrap }, 1, 0);
            _facts.Children.Add(g);
        }
        Row("Last contact with the server", ConnectionColors.Manila(n.LastContactAt));
        Row("Database on the server", n.Database);
        Row("Checks failed", $"{n.Failed} of {n.Checks}");
        //JJQR - AI (Nov 22, 2026) - OTA: version and update status
        var upd = _sp.GetService<ClinicNode.App.Services.AppUpdateService>();
        if (upd is not null)
        {
            Row("App version", upd.CurrentText);
            Row("Updates", ClinicNode.App.Views.Shared.UpdateUi.StatusLine(upd.Last) + (upd.LastCheckedAt is { } at ? $"  (checked {ConnectionColors.Manila(at)})" : ""));
        }
        //JJQR - END

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
