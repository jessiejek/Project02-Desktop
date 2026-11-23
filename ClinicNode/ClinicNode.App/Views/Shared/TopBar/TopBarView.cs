// JJQR - AI (Nov 21, 2026) - TopBar from docs/handoff/TopBar.cs.txt (fixed) + web Topbar.tsx.
// Draft issues fixed: ClinicTheme.Apply(string) vs a second Apply(Action) in the same Controls namespace was an ambiguous/unusable extension;
// dead role-pill assignment removed; Microsoft.Maui.Controls.Shapes imported for Ellipse; ISyncStatus chip added on the bar itself.
// Uses already-registered IGlobalSearchService. Shell owner places this as TitleView — do not place it here.
//JJQR - AI (21 Nov 2026) - allen0 I-1: wiring note resolved; wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs). //JJQR - END
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.Core.Diagnostics;
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
    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.5: [dot  Online | Offline mode  42 ms] opens the connection panel
    private readonly Ellipse _qualityDot = new() { WidthRequest = 9, HeightRequest = 9, VerticalOptions = LayoutOptions.Center, Fill = new SolidColorBrush(ClinicTheme.Color("OnSurfaceVariant")) };
    private readonly Label _modeText = new() { FontSize = 12, FontFamily = ClinicTheme.FontSemiBold, VerticalOptions = LayoutOptions.Center, TextColor = ClinicTheme.Color("OnSurface") };
    private readonly Label _latencyText = new() { FontSize = 12, VerticalOptions = LayoutOptions.Center, TextColor = ClinicTheme.Color("OnSurfaceVariant") };
    private readonly Border _modePill = new() { VerticalOptions = LayoutOptions.Center, Padding = new Thickness(10, 5), StrokeShape = new RoundRectangle { CornerRadius = 14 }, StrokeThickness = 1, Stroke = ClinicTheme.Color("OutlineVariant") };
    private ClinicNode.Api.Http.IConnectionMonitor? _monitor;
    private ClinicNode.Core.Sync.IWorkModeService? _workMode;
    //JJQR - END
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
            new ColumnDefinition(GridLength.Auto), //JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.5: mode pill + status dot //JJQR - END
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

        //JJQR - AI (Nov 22, 2026) - owner: the moving line under the top bar was REMOVED (annoying; it also stayed on). Skeletons on empty screens stay.
        //JJQR - END
        //JJQR - AI (Nov 22, 2026) - OTA: a small "Update 0.3.0" chip next to the connection pill, shown only when a newer version exists
        var updates = sp.GetService<ClinicNode.App.Services.AppUpdateService>();
        var updateChip = new Button { Text = "", IsVisible = false, HeightRequest = 30, Padding = new Thickness(12, 0), VerticalOptions = LayoutOptions.Center };
        updateChip.Apply("PrimaryButton");
        updateChip.Clicked += async (_, _) => await ClinicNode.App.Views.Shared.UpdateUi.ShowAsync(_sp);
        void RefreshChip() => MainThread.BeginInvokeOnMainThread(() =>
        {
            var i = updates?.Last;
            updateChip.IsVisible = i is { HasUpdate: true };
            if (i is { HasUpdate: true }) updateChip.Text = "⬆  Update " + i.Latest!.ToString(3);
        });
        if (updates is not null) { updates.Changed += RefreshChip; Unloaded += (_, _) => updates.Changed -= RefreshChip; RefreshChip(); }
        var pillHost = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, Children = { updateChip, _modePill } };
        PlaceIn(pillHost, 1); //JJQR - AI (Nov 22, 2026) - step 1.5 //JJQR - END
        PlaceIn(_badgeHost, 2);

        _syncChip = StaffUi.Btn("Online", async () => await OpenSyncMenuAsync(), "GhostButton");
        _syncChip.VerticalOptions = LayoutOptions.Center;
        //JJQR - AI (21 Nov 2026) - owner rule 6: room for "Offline, N changes waiting"
        _syncChip.MaximumWidthRequest = 220;
        //JJQR - END
        _syncChip.LineBreakMode = LineBreakMode.TailTruncation;
        SemanticProperties.SetDescription(_syncChip, "Sync status");
        PlaceIn(_syncChip, 3);
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 1.5
        _monitor = sp.GetService<ClinicNode.Api.Http.IConnectionMonitor>();
        _workMode = sp.GetService<ClinicNode.Core.Sync.IWorkModeService>();
        _modePill.Content = new HorizontalStackLayout { Spacing = 7, Children = { _qualityDot, _modeText, _latencyText } };
        _modePill.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Shell.Current.Navigation.PushModalAsync(new ConnectionPanelPage(_sp))) });
        SemanticProperties.SetDescription(_modePill, "Connection and work mode. Opens the connection panel.");
        _modePill.IsVisible = _monitor is not null && _workMode is not null;
        if (_monitor is not null) _monitor.Changed += OnMonitor;
        if (_workMode is not null) _workMode.Changed += OnWorkMode;
        //JJQR - END

        var bell = new Button
        {
            ImageSource = ClinicIcons.FontImage("notifications", ClinicIcons.SizeSm, ClinicTheme.Color("OnSurfaceVariant")),
            Style = ClinicTheme.Style("GhostButton"),
            VerticalOptions = LayoutOptions.Center,
        };
        SemanticProperties.SetDescription(bell, "Notifications");
        bell.Clicked += async (_, _) => await Shell.Current.Navigation.PushModalAsync(new NotificationsPage(_sp, () => _ = RefreshAsync()));
        PlaceIn(new Grid { Children = { bell, _dot }, VerticalOptions = LayoutOptions.Center }, 4);

        var name = _auth.Current?.FullName ?? "";
        _nameLabel = ClinicTheme.Text(name, "LabelMd");
        _nameLabel.VerticalOptions = LayoutOptions.Center;
        //JJQR - AI (Nov 23, 2026) - owner: "show the full name". It was cut at 160 px ("Dr. Allyn Grace T. Españ..."). The column sizes itself, so let the whole name show (the narrow-window rule still hides it first when space is short).
        _nameLabel.LineBreakMode = LineBreakMode.NoWrap;
        _nameLabel.MaximumWidthRequest = 420;
        //JJQR - END
        PlaceIn(_nameLabel, 5);
        //JJQR - AI (Nov 23, 2026) - owner: on Windows the admin could not reach Logout in the sidebar (the menu did not scroll). The name and avatar in the top bar are always visible: tap either for a menu with Sign out.
        _nameLabel.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await AccountMenuAsync()) });
        //JJQR - END
        var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
        var avatar = new Border
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
        };
        PlaceIn(avatar, 6);
        //JJQR - AI (Nov 23, 2026) - the avatar opens the same menu
        avatar.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await AccountMenuAsync()) });
        //JJQR - END

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

    //JJQR - AI (Nov 23, 2026) - owner: "when I click the name, a dropdown with Settings or Logout". A small menu under the name: the full name and role on top, then Settings (this person's own account page) and Logout. Logout is always one click away, whatever the size of the sidebar.
    async Task AccountMenuAsync()
    {
        try
        {
            var shell = Shell.Current;
            if (shell is null) return;
            var session = _auth.Current;
            var page = shell.CurrentPage as ContentPage;
            var who = string.IsNullOrWhiteSpace(session?.FullName) ? "Account" : session!.FullName;
            var settingsRoute = session?.Role switch { UserRole.Doctor => "doctor-profile", UserRole.Staff => "staff-profile", UserRole.Admin => "admin-profile", _ => null };

            async Task GoSettingsAsync() { if (settingsRoute is not null) { try { await shell.GoToAsync("//" + settingsRoute); } catch (Exception ex) { await shell.DisplayAlertAsync("Settings", ex.Message, "OK"); } } }
            async Task LogoutAsync() { if (shell is AppShell app) await app.UserSignOutAsync(); }

            if (page is null)
            {
                // no page to hang a dropdown on: fall back to the plain choice sheet
                var choice = await shell.DisplayActionSheetAsync(who, "Cancel", null, "Settings", "Logout");
                if (choice == "Settings") await GoSettingsAsync();
                else if (choice == "Logout") await LogoutAsync();
                return;
            }

            Border Item(string icon, string text, Color color, Func<Task> action)
            {
                var row = new Border
                {
                    StrokeThickness = 0, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 }, Padding = new Thickness(12, 10), BackgroundColor = Colors.Transparent,
                    Content = new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Children =
                        {
                            new ClinicNode.App.Controls.ClinicIcon { Name = icon, IconSize = ClinicNode.App.Controls.ClinicIcons.SizeSm, IconColor = color, VerticalOptions = LayoutOptions.Center },
                            new Label { Text = text, FontSize = 14, FontFamily = ClinicTheme.FontMedium, TextColor = color, VerticalOptions = LayoutOptions.Center },
                        },
                    },
                };
                var hv = new PointerGestureRecognizer();
                hv.PointerEntered += (_, _) => row.BackgroundColor = ClinicTheme.Color("PrimarySubtle");
                hv.PointerExited += (_, _) => row.BackgroundColor = Colors.Transparent;
                row.GestureRecognizers.Add(hv);
                row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => { ClinicNode.App.Controls.ClinicPopup.Hide(page); await action(); }) });
                return row;
            }

            var menu = new VerticalStackLayout { Spacing = 2 };
            menu.Children.Add(new VerticalStackLayout
            {
                Spacing = 2, Padding = new Thickness(12, 8, 12, 8),
                Children = { new Label { Text = who, FontSize = 14, FontFamily = ClinicTheme.FontSemiBold, TextColor = ClinicTheme.Color("OnSurface") }, ClinicTheme.Text(session?.Role.ToString() ?? "", "MutedSm") },
            });
            menu.Children.Add(new BoxView { HeightRequest = 1, Color = ClinicTheme.Color("OutlineVariant").WithAlpha(0.5f), Margin = new Thickness(8, 2) });
            if (settingsRoute is not null) menu.Children.Add(Item("settings", "Settings", ClinicTheme.Color("OnSurface"), GoSettingsAsync));
            menu.Children.Add(Item("logout", "Logout", ClinicTheme.Color("StatusDanger"), LogoutAsync));
            var panel = new Border
            {
                Style = ClinicTheme.Style("Card"), Padding = new Thickness(6), WidthRequest = 330, //JJQR - AI (Nov 23, 2026) - wide enough for the full name on one line //JJQR - END
                Content = menu,
            };
            ClinicNode.App.Controls.ClinicPopup.Show(page, _nameLabel.IsVisible ? _nameLabel : this, panel, 330, 150);
        }
        catch { /* never break the bar */ }
    }
    //JJQR - END

    //JJQR - AI (Nov 22, 2026) - step 1.5
    void OnMonitor() => MainThread.BeginInvokeOnMainThread(ApplyModePill);
    void OnWorkMode(object? s, ClinicNode.Core.Sync.WorkModeInfo i) => MainThread.BeginInvokeOnMainThread(ApplyModePill);

    void ApplyModePill()
    {
        if (_monitor is null || _workMode is null) return;
        var st = _monitor.Status;
        var offline = _workMode.Mode == ClinicNode.Core.Sync.WorkMode.Offline;
        _modeText.Text = offline ? "Offline mode" : "Online";
        _qualityDot.Fill = new SolidColorBrush(ClinicTheme.Color(ConnectionColors.KeyOf(st.Quality)));
        var last = _monitor.Samples.Count > 0 ? _monitor.Samples[^1] : null;
        _latencyText.Text = last is { Ok: true } ? last.Ms + " ms" : ConnectionColors.Word(st.Quality);
        _modePill.BackgroundColor = offline ? ClinicTheme.Color("PrimarySubtle") : Colors.Transparent;
        SemanticProperties.SetDescription(_modePill, $"{_modeText.Text}. Connection {ConnectionColors.Word(st.Quality)}. {st.Reason} Opens the connection panel.");
    }
    //JJQR - END

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
            if (_monitor is not null) _monitor.Changed -= OnMonitor; //JJQR - AI (Nov 22, 2026) - step 1.5 //JJQR - END
            if (_workMode is not null) _workMode.Changed -= OnWorkMode;
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
            ApplyModePill(); //JJQR - AI (Nov 22, 2026) - step 1.5 //JJQR - END
            SemanticProperties.SetDescription(_syncChip, _sync.Banner);
            var session = _auth.Current;
            if (session is null) return;
            ApplyName(); //JJQR - AI (21 Nov 2026) - follow a renamed session //JJQR - END
            if (session.Role == UserRole.Doctor && session.DoctorId is { } did)
            {
                //JJQR - AI (Nov 22, 2026) - owner: the doctor's day status (Available...) is shown on the Availability card of the Today page, not in the top bar
                _badgeHost.Content = null;
                _badgeHost.IsVisible = false;
                //JJQR - END
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
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 0.5: the three badge words come first. Sync problem wins over everything but a computer that is not registered (that has its own wording below).
        if (_sync.Phase != SyncPhase.NotRegistered && (_indicator is not null ? _indicator.Kind == ClinicNode.App.Shared.OnlineIndicatorKind.Problem : _sync.HasSyncProblem))
            return "Sync problem";
        //JJQR - END
        if (_indicator is not null && _sync.ConflictCount == 0 && _sync.Phase is not (SyncPhase.Revoked or SyncPhase.NeedsUpdate or SyncPhase.NotRegistered))
            return _indicator.Text;
        //JJQR - END
        if (_sync.IsSyncing || _sync.Phase == SyncPhase.Syncing) return "Syncing";
        if (_sync.ConflictCount > 0) return "Conflicts";
        return _sync.Phase switch
        {
            //JJQR - AI (Nov 21, 2026) - say what is really wrong: the API can be reachable while this computer is just not registered for sync
            SyncPhase.Offline => "Offline",
            //JJQR - AI (Nov 22, 2026) - step 0.5: an error is a Sync problem, not "Offline"; an unregistered computer says whether the server answers (it used to print a fixed "Online")
            SyncPhase.Error => "Sync problem",
            SyncPhase.NotRegistered => (_indicator is not null && _indicator.Kind == ClinicNode.App.Shared.OnlineIndicatorKind.Offline) ? "Offline · not registered for sync" : "Online · not registered for sync",
            SyncPhase.Revoked => "Sync problem · access revoked",
            SyncPhase.NeedsUpdate => "Sync problem · app update needed",
            //JJQR - END
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
        if (choice == "Sync now") await SyncResultPage.ShowAsync(_sp, "manual"); //JJQR - AI (Nov 22, 2026) - step 3.2 //JJQR - END
        else if (choice == "Review conflicts") await shell.GoToAsync("sync-conflicts");
    }

    void ApplyNarrowLayout()
    {
        var w = Width;
        if (double.IsNaN(w) || w <= 0) return;
        // Hide lower-priority chrome first so search + sync + bell + avatar stay visible.
        _nameLabel.IsVisible = w >= 640;
        _badgeHost.IsVisible = w >= 520 && _badgeHost.Content is not null; //JJQR - AI (Nov 22, 2026) //JJQR - END
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
