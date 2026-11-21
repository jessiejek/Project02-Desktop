// JJQR - AI (Nov 21, 2026) - TopBar from docs/handoff/TopBar.cs.txt (fixed) + web Topbar.tsx.
// Draft issues fixed: ClinicTheme.Apply(string) vs a second Apply(Action) in the same Controls namespace was an ambiguous/unusable extension;
// dead role-pill assignment removed; Microsoft.Maui.Controls.Shapes imported for Ellipse; ISyncStatus chip added on the bar itself.
// Uses already-registered IGlobalSearchService. Shell owner places this as TitleView — do not place it here.
//JJQR - AI (21 Nov 2026) - allen0 I-1: wiring note resolved; wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs). //JJQR - END
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Events;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Staff;
using ClinicNode.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;

namespace ClinicNode.App.Views.Shared;

public sealed class TopBarView : Grid
{
    private readonly IServiceProvider _sp;
    private readonly IAuthService _auth;
    private readonly ISyncStatus _sync;
    private readonly IClinicEventBus _events;
    private readonly ContentView _badgeHost = new() { VerticalOptions = LayoutOptions.Center };
    private readonly Button _syncChip;
    private readonly Label _nameLabel;
    private readonly Ellipse _dot = new()
    {
        WidthRequest = 8,
        HeightRequest = 8,
        Fill = new SolidColorBrush(ClinicTheme.Color("Error")),
        HorizontalOptions = LayoutOptions.End,
        VerticalOptions = LayoutOptions.Start,
        IsVisible = false,
    };

    public TopBarView(IServiceProvider sp)
    {
        _sp = sp;
        _auth = sp.GetRequiredService<IAuthService>();
        _sync = sp.GetRequiredService<ISyncStatus>();
        _events = sp.GetRequiredService<IClinicEventBus>();
        ColumnSpacing = 12;
        Padding = new Thickness(0, 0, 12, 0);
        // Search takes leftover space; chrome stays Auto so nothing overflows a normal window.
        ColumnDefinitions = new ColumnDefinitionCollection
        {
            new ColumnDefinition(GridLength.Star),
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Auto),
            new ColumnDefinition(GridLength.Auto),
        };

        var search = new Entry
        {
            Placeholder = "Search…",
            MaximumWidthRequest = 288,
            MinimumWidthRequest = 120,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
        };
        SemanticProperties.SetDescription(search, "Search patients, doctors or appointments");
        search.Focused += async (_, _) => { search.Unfocus(); await OpenSearchAsync(); };
        PlaceIn(search, 0);

        PlaceIn(_badgeHost, 1);

        _syncChip = StaffUi.Btn("Online", async () => await OpenSyncMenuAsync(), "GhostButton");
        _syncChip.VerticalOptions = LayoutOptions.Center;
        //JJQR - AI (21 Nov 2026) - owner rule 6: room for "Offline, N changes waiting"
        _syncChip.MaximumWidthRequest = 220;
        //JJQR - END
        _syncChip.LineBreakMode = LineBreakMode.TailTruncation;
        SemanticProperties.SetDescription(_syncChip, "Sync status");
        PlaceIn(_syncChip, 2);

        var bell = new Button
        {
            ImageSource = ClinicIcons.FontImage("notifications", ClinicIcons.SizeSm, ClinicTheme.Color("OnSurfaceVariant")),
            Style = ClinicTheme.Style("GhostButton"),
            VerticalOptions = LayoutOptions.Center,
        };
        SemanticProperties.SetDescription(bell, "Notifications");
        bell.Clicked += async (_, _) => await Shell.Current.Navigation.PushModalAsync(new NotificationsPage(_sp, () => _ = RefreshAsync()));
        PlaceIn(new Grid { Children = { bell, _dot }, VerticalOptions = LayoutOptions.Center }, 3);

        var name = _auth.Current?.FullName ?? "";
        _nameLabel = ClinicTheme.Text(name, "LabelMd");
        _nameLabel.VerticalOptions = LayoutOptions.Center;
        _nameLabel.LineBreakMode = LineBreakMode.TailTruncation;
        _nameLabel.MaximumWidthRequest = 160;
        PlaceIn(_nameLabel, 4);
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        PlaceIn(new Border
        {
            WidthRequest = 32,
            HeightRequest = 32,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Stroke = ClinicTheme.Color("OutlineVariant"),
            BackgroundColor = ClinicTheme.Color("SurfaceContainerHigh"),
            //JJQR - AI (21 Nov 2026) - kept so a name change updates the avatar
            Content = _initialsLabel = new Label
            //JJQR - END
            {
                Text = initials,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                FontSize = 13,
                TextColor = ClinicTheme.Color("OnSurfaceVariant"),
            },
        }, 5);

        _events.Changed += OnEvent;
        _sync.PropertyChanged += OnSync;
        //JJQR - AI (21 Nov 2026) - owner rules 2–6: start live sync (idempotent) and follow the online/syncing/offline indicator
        _indicator = sp.GetService<ClinicNode.App.Shared.OnlineStatusIndicator>();
        if (_indicator is not null) _indicator.Changed += OnIndicator;
        if (_auth is ClinicNode.Api.Auth.IOnlineAuthSession online) online.SessionChanged += OnSessionChanged; // name saved on the profile page → bar updates
        try { sp.GetService<ClinicNode.App.Shared.LiveSyncCoordinator>()?.Start(); } catch { /* never break the bar */ }
        //JJQR - END
        SizeChanged += (_, _) => ApplyNarrowLayout();
        _ = RefreshAsync();
    }

    void OnEvent(ClinicEvent ev) => MainThread.BeginInvokeOnMainThread(() => { _ = RefreshAsync(); });
    void OnSync(object? s, System.ComponentModel.PropertyChangedEventArgs e) => MainThread.BeginInvokeOnMainThread(() => _ = RefreshAsync());
    //JJQR - AI (21 Nov 2026)
    private readonly ClinicNode.App.Shared.OnlineStatusIndicator? _indicator;
    void OnIndicator() => MainThread.BeginInvokeOnMainThread(() => _ = RefreshAsync());
    void OnSessionChanged() => MainThread.BeginInvokeOnMainThread(() => _ = RefreshAsync());
    private Label? _initialsLabel;

    void ApplyName()
    {
        var name = _auth.Current?.FullName ?? "";
        if (_nameLabel.Text != name) _nameLabel.Text = name;
        if (_initialsLabel is not null)
            _initialsLabel.Text = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }
    //JJQR - END

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null)
        {
            _events.Changed -= OnEvent;
            _sync.PropertyChanged -= OnSync;
            if (_indicator is not null) _indicator.Changed -= OnIndicator; //JJQR - AI (21 Nov 2026) //JJQR - END
            //JJQR - AI (21 Nov 2026)
            if (_auth is ClinicNode.Api.Auth.IOnlineAuthSession online) online.SessionChanged -= OnSessionChanged;
            //JJQR - END
        }
        base.OnHandlerChanging(args);
    }

    public async Task RefreshAsync()
    {
        try
        {
            _syncChip.Text = CompactSyncLabel();
            SemanticProperties.SetDescription(_syncChip, _sync.Banner);
            var session = _auth.Current;
            if (session is null) return;
            ApplyName(); //JJQR - AI (21 Nov 2026) - follow a renamed session //JJQR - END
            if (session.Role == UserRole.Doctor && session.DoctorId is { } did)
            {
                var board = await _sp.GetRequiredService<IDoctorStatusService>().GetTodayAsync();
                var mine = board.Doctors.FirstOrDefault(d => d.DoctorId == did)?.Status ?? DoctorDayStatusEnum.Available;
                _badgeHost.Content = ClinicTheme.MakeStatusPill(mine.ToString());
            }
            else
            {
                _badgeHost.Content = new Border
                {
                    Style = ClinicTheme.Style("PillNeutral"),
                    Content = ClinicTheme.Text(session.Role.ToString(), "PillLabelNeutral"),
                };
            }
            _dot.IsVisible = await NotificationsPage.HasUnseenAsync(_sp);
            ApplyNarrowLayout();
        }
        catch
        {
            /* the top bar must never break a screen */
        }
    }

    // Online / Offline / Syncing / Conflicts — the long Banner (and Sync now / conflicts) live in the tap menu only.
    string CompactSyncLabel()
    {
        //JJQR - AI (21 Nov 2026) - owner rule 6: "Online, up to date" / "Syncing..." / "Offline, N changes waiting"; conflicts still win
        if (_indicator is not null && _sync.ConflictCount == 0 && _sync.Phase is not (SyncPhase.Revoked or SyncPhase.NeedsUpdate or SyncPhase.NotRegistered))
            return _indicator.Text;
        //JJQR - END
        if (_sync.IsSyncing || _sync.Phase == SyncPhase.Syncing) return "Syncing";
        if (_sync.ConflictCount > 0) return "Conflicts";
        return _sync.Phase switch
        {
            //JJQR - AI (Nov 21, 2026) - say what is really wrong: the API can be reachable while this computer is just not registered for sync
            SyncPhase.Offline or SyncPhase.Error => "Offline",
            SyncPhase.NotRegistered => "Online · not registered for sync",
            SyncPhase.Revoked => "Sync access revoked",
            SyncPhase.NeedsUpdate => "App update needed",
            //JJQR - END
            _ => "Online",
        };
    }

    async Task OpenSyncMenuAsync()
    {
        var shell = Shell.Current;
        if (shell is null) return;
        var buttons = new List<string> { "Sync now" };
        if (_sync.ConflictCount > 0) buttons.Add("Review conflicts");
        var choice = await shell.DisplayActionSheetAsync(_sync.Banner, "Close", null, [.. buttons]);
        if (choice == "Sync now") _sync.RequestSync();
        else if (choice == "Review conflicts") await shell.GoToAsync("sync-conflicts");
    }

    void ApplyNarrowLayout()
    {
        var w = Width;
        if (double.IsNaN(w) || w <= 0) return;
        // Hide lower-priority chrome first so search + sync + bell + avatar stay visible.
        _nameLabel.IsVisible = w >= 640;
        _badgeHost.IsVisible = w >= 520;
    }

    void PlaceIn(View view, int column)
    {
        Grid.SetColumn(view, column);
        Children.Add(view);
    }

    async Task OpenSearchAsync() => await Shell.Current.Navigation.PushModalAsync(new GlobalSearchPage(_sp));
}

public sealed class GlobalSearchPage : ContentPage
{
    private readonly IServiceProvider _sp;
    private readonly Entry _entry = new() { Placeholder = "Search patients, doctors or appointments" };
    private readonly Label _status = ClinicTheme.Text("Type at least 2 characters.", "MutedSm");
    private readonly VerticalStackLayout _results = new() { Spacing = 4 };
    private CancellationTokenSource? _cts;
    private IReadOnlyList<SearchHit> _hits = [];

    public GlobalSearchPage(IServiceProvider sp)
    {
        _sp = sp;
        Title = "Search";
        BackgroundColor = Colors.Transparent;
        ControlTemplate = null;
        _entry.TextChanged += (_, e) => _ = SearchAsync(e.NewTextValue ?? "");
        _entry.Completed += async (_, _) => { if (_hits.Count > 0) await OpenAsync(_hits[0]); };
        Content = ClinicModal.Build("Search", new VerticalStackLayout { Spacing = 10, Children = { _entry, _status, _results } }, null, () => Navigation.PopModalAsync(), 640, 460);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _entry.Focus();
    }

    async Task SearchAsync(string text)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var q = text.Trim();
        if (q.Length < 2)
        {
            _hits = [];
            _results.Children.Clear();
            _status.Text = "Type at least 2 characters.";
            return;
        }
        try
        {
            await Task.Delay(300, cts.Token);
            _status.Text = "Searching…";
            var hits = await _sp.GetRequiredService<IGlobalSearchService>().SearchAsync(q, cts.Token);
            if (cts.IsCancellationRequested) return;
            _hits = hits;
            _results.Children.Clear();
            _status.Text = hits.Count == 0 ? "No results" : "";
            foreach (var h in hits)
            {
                var hit = h;
                var can = TopBarNav.CanOpen(_sp, hit);
                var kind = new Border { Style = ClinicTheme.Style("PillInfo"), Padding = new Thickness(8, 3), VerticalOptions = LayoutOptions.Center, Content = ClinicTheme.Text(hit.KindLabel, "PillLabelInfo") };
                var g = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new ColumnDefinition(96), new ColumnDefinition(GridLength.Star) } };
                g.Add(kind, 0, 0); g.Add(new Label { Text = hit.Label, FontSize = 14, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation }, 1, 0);
                var row = new Border { StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Padding = new Thickness(10, 9), BackgroundColor = Colors.Transparent, Opacity = can ? 1 : 0.5, Content = g };
                if (can)
                {
                    var hv = new PointerGestureRecognizer();
                    hv.PointerEntered += (_, _) => row.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
                    hv.PointerExited += (_, _) => row.BackgroundColor = Colors.Transparent;
                    row.GestureRecognizers.Add(hv);
                    row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OpenAsync(hit)) });
                }
                _results.Children.Add(row);
            }
        }
        catch (OperationCanceledException) { }
        catch { _status.Text = "No results"; }
    }

    async Task OpenAsync(SearchHit hit)
    {
        if (!TopBarNav.CanOpen(_sp, hit)) return;
        await Navigation.PopModalAsync();
        await TopBarNav.OpenAsync(_sp, hit);
    }
}

public sealed class NotificationsPage : ContentPage
{
    private const string SeenKey = "topbar.announcements.seen";
    private readonly IServiceProvider _sp;
    private readonly VerticalStackLayout _list = new() { Spacing = 8 };
    private readonly Action _onClosed;

    public NotificationsPage(IServiceProvider sp, Action onClosed)
    {
        _sp = sp;
        _onClosed = onClosed;
        Title = "Notifications";
        BackgroundColor = Colors.Transparent;
        ControlTemplate = null;
        Content = ClinicModal.Build("Notifications", _list, null, async () => { await Navigation.PopModalAsync(); _onClosed(); }, 600, 480);
    }

    public static async Task<bool> HasUnseenAsync(IServiceProvider sp)
    {
        var sync = sp.GetRequiredService<ISyncStatus>();
        if (sync.ConflictCount > 0) return true;
        var rows = await StaffUi.Get<IStaffAnnouncementsService>(sp).ListActiveAsync();
        if (rows.Count == 0) return false;
        return rows.Max(r => r.PostedDate.UtcTicks) > Preferences.Default.Get(SeenKey, 0L);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _list.Children.Clear();
        var sync = _sp.GetRequiredService<ISyncStatus>();
        if (sync.ConflictCount > 0)
        {
            _list.Children.Add(StaffUi.Row(
                ClinicTheme.Text($"{sync.ConflictCount} sync conflict(s) need review", "LabelLg"),
                StaffUi.Btn("Review conflicts", async () =>
                {
                    await Navigation.PopModalAsync();
                    await Shell.Current.GoToAsync("sync-conflicts");
                }, "SecondaryButton")));
        }
        _list.Children.Add(StaffUi.Row(ClinicTheme.Text("Sync", "LabelLg"), ClinicTheme.Text($"{sync.Banner} · {sync.PendingCount} pending", "MutedSm")));
        try
        {
            var rows = await StaffUi.Get<IStaffAnnouncementsService>(_sp).ListActiveAsync();
            if (rows.Count == 0) _list.Children.Add(ClinicTheme.Text("No announcements.", "MutedSm"));
            foreach (var a in rows)
                _list.Children.Add(StaffUi.Row(
                    ClinicTheme.Text(a.Title, "LabelLg"),
                    ClinicTheme.Text(a.Body, "BodyMd"),
                    ClinicTheme.Text($"{a.PostedBy} · {StaffUi.DateTimeText(a.PostedDate)}", "MutedSm")));
            if (rows.Count > 0) Preferences.Default.Set(SeenKey, rows.Max(r => r.PostedDate.UtcTicks));
        }
        catch
        {
            _list.Children.Add(ClinicTheme.Text("Announcements are unavailable.", "MutedSm"));
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _onClosed();
        return base.OnBackButtonPressed();
    }
}

/// <summary>Where a search hit goes, per role (web hitHref). Doctor patient/booking targets switch to allen3's chart/appointment routes when they land.</summary>
public static class TopBarNav
{
    /// <summary>Route a doctor's patient hit opens; replace with allen3's patient-chart route when delivered.</summary>
    public static string DoctorPatientRoute(Guid id) => "//patient-charts";
    /// <summary>Route a doctor's booking hit opens; replace with allen3's appointment-detail route when delivered.</summary>
    public static string DoctorBookingRoute(Guid id) => "//appointments";

    static UserRole? Role(IServiceProvider sp) => sp.GetRequiredService<IAuthService>().Current?.Role;

    public static bool CanOpen(IServiceProvider sp, SearchHit hit) => (Role(sp), hit.Kind) switch
    {
        (UserRole.Doctor, SearchHitKind.Doctor) => false,
        (null, _) => false,
        _ => true,
    };

    public static async Task OpenAsync(IServiceProvider sp, SearchHit hit)
    {
        var shell = Shell.Current;
        switch (Role(sp), hit.Kind)
        {
            case (UserRole.Doctor, SearchHitKind.Patient): await shell.GoToAsync(DoctorPatientRoute(hit.Id)); break;
            case (UserRole.Doctor, SearchHitKind.Booking): await shell.GoToAsync(DoctorBookingRoute(hit.Id)); break;
            case (UserRole.Staff, SearchHitKind.Patient): await StaffUi.PushAsync<StaffPatientDetailPage>(shell.CurrentPage, hit.Id); break;
            case (UserRole.Staff, SearchHitKind.Booking): await StaffUi.PushAsync<StaffBookingDetailPage>(shell.CurrentPage, hit.Id); break;
            case (UserRole.Staff, SearchHitKind.Doctor): await shell.GoToAsync("//staff-doctor-status"); break;
            case (UserRole.Admin, SearchHitKind.Patient): await shell.GoToAsync($"admin-patient-detail?id={hit.Id}"); break;
            case (UserRole.Admin, SearchHitKind.Booking): await shell.GoToAsync($"admin-booking-detail?id={hit.Id}"); break;
            case (UserRole.Admin, SearchHitKind.Doctor): await shell.GoToAsync($"admin-doctor-edit?id={hit.Id}"); break;
        }
    }
}
// JJQR - END
