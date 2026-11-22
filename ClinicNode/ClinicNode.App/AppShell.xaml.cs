// JJQR - AI (Nov 21, 2026) - role-based native shell (doctor / staff / admin). No patient portal.
// Flyout is Locked at 260px and visually matches the web Sidebar (light for doctor, inverse-surface for staff/admin).
using System.Windows.Input;
using ClinicApp.Domain.Enums;
using ClinicNode.App.Controls;
using ClinicNode.App.Views;
using ClinicNode.App.Views.Admin;
using ClinicNode.App.Views.Doctor;
using ClinicNode.App.Views.Doctor.Extra;
using ClinicNode.App.Views.Shared;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.Auth;
using ClinicNode.Sync;

namespace ClinicNode.App;

public partial class AppShell : Shell
{
    private readonly IAuthService _auth;
    private readonly ISyncStatus _sync;
    private readonly IServiceProvider _services;
    private ClinicSidebar? _sidebar;
    // JJQR - AI (Nov 21, 2026) - session expiry, 7-day offline lock, and staff status
    IDispatcherTimer? _sessionTimer;
    Window? _watchedWindow;
    bool _sessionCheckRunning;
    // JJQR - END

    public AppShell(IAuthService auth, ISyncStatus sync, IServiceProvider services)
    {
        InitializeComponent();
        _auth = auth;
        _sync = sync;
        _services = services;
        BindingContext = this;
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 2.1: when the mode changes, re-check the session at once (Staff/Admin are signed out in Offline mode)
        if (services.GetService<ClinicNode.Core.Sync.IWorkModeService>() is { } workMode) workMode.Changed += (_, _) => _ = CheckSessionAsync();
        //JJQR - END
        RefreshSyncCommand = new Command(async () => await RefreshSyncAsync());
        // JJQR - AI (Nov 21, 2026) - Sync now + conflict review; ISyncStatus events arrive off the UI thread.
        SyncNowCommand = new Command(async () => { try { await ClinicNode.App.Views.Shared.SyncResultPage.ShowAsync(_services, "manual"); } catch { /* the shell must not break */ } }); //JJQR - AI (Nov 22, 2026) - step 3.2 //JJQR - END
        OpenConflictsCommand = new Command(async () =>
        {
            try { await GoToAsync("sync-conflicts"); }
            catch (Exception ex) { await DisplayAlertAsync("Sync review", ex.Message, "OK"); }
        });
        Routing.RegisterRoute("consult", typeof(ConsultationPage));
        Routing.RegisterRoute("walkin", typeof(NewWalkInPage));
        Routing.RegisterRoute("sync-conflicts", typeof(SyncConflictsPage));
        // JJQR - AI (Nov 21, 2026) - admin detail/form routes pushed from list pages
        Routing.RegisterRoute("admin-booking-detail", typeof(AdminBookingDetailPage));
        Routing.RegisterRoute("admin-patient-detail", typeof(AdminPatientDetailPage));
        Routing.RegisterRoute("admin-doctor-new", typeof(AdminDoctorNewPage));
        Routing.RegisterRoute("admin-doctor-edit", typeof(AdminDoctorEditPage));
        // JJQR - END
        //JJQR - AI (21 Nov 2026) - wave-end wiring (allen0): module routes. Guarded so a second AppShell instance does not throw on duplicate routes.
        try { ClinicNode.App.StaffModule.RegisterStaffRoutes(); } catch (ArgumentException) { }
        try { ClinicNode.App.Admin.AdminModule.RegisterAdminRoutes(); } catch (ArgumentException) { }
        try { ClinicNode.App.Doctor.DoctorModule.RegisterDoctorRoutes(); } catch (ArgumentException) { }
        try { ClinicNode.App.DoctorExtra2Module.RegisterDoctorExtra2Routes(); } catch (ArgumentException) { }
        //JJQR - END
        FlyoutWidth = ClinicTheme.SidebarWidth;
        ApplyStatusFromSync();
        _sync.Changed += _ => MainThread.BeginInvokeOnMainThread(ApplyStatusFromSync);
        _sync.PropertyChanged += (_, _) => MainThread.BeginInvokeOnMainThread(ApplyStatusFromSync);
        // JJQR - END
        Navigated += (_, e) =>
        {
            var route = e.Current?.Location?.OriginalString?.Trim('/') ?? "";
            // "//staff-home" etc.
            var leaf = route.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? route;
            _sidebar?.SetActiveRoute(leaf);
            //JJQR - AI (Nov 22, 2026) - REMOVED: setting a top bar on pages opened over another page crashed MAUI navigation on Mac ("Failed to marshal the Objective-C object ... ShellPageRendererTracker+TitleViewContainer", seen live by the owner). The top bar stays on the main screens only until a safe way is found (set before the page is pushed, not after).
            //JJQR - END
        };
    }

    public string SyncBanner { get; set; } = "Starting…";
    public ICommand RefreshSyncCommand { get; }
    // JJQR - AI (Nov 21, 2026)
    public string PendingLabel { get; set; } = "0 pending";
    public string ConflictsLabel { get; set; } = "Conflicts";
    public ICommand SyncNowCommand { get; }
    public ICommand OpenConflictsCommand { get; }
    // JJQR - END

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        HookSessionWindow();
        await RefreshSyncAsync();
    }

    public async Task RefreshSyncAsync()
    {
        try
        {
            // JJQR - AI (Nov 21, 2026) - bind banner + pending/conflict counts from ISyncStatus
            await _sync.RefreshAsync();
            ApplyStatusFromSync();
        }
        catch
        {
            SyncBanner = "Sync status unavailable";
            OnPropertyChanged(nameof(SyncBanner));
        }
    }

    void ApplyStatusFromSync()
    {
        SyncBanner = _sync.Banner;
        PendingLabel = _sync.PendingCount == 1 ? "1 pending" : $"{_sync.PendingCount} pending";
        ConflictsLabel = _sync.ConflictCount == 0 ? "Conflicts" : $"Conflicts ({_sync.ConflictCount})";
        OnPropertyChanged(nameof(SyncBanner));
        OnPropertyChanged(nameof(PendingLabel));
        OnPropertyChanged(nameof(ConflictsLabel));
        // JJQR - AI (Nov 21, 2026) - revoked node: sign out and return to login on the UI thread
        if (_sync.Phase == SyncPhase.Revoked && _auth.Current is not null && !_handlingRevoked)
            _ = HandleRevokedAsync();
        // JJQR - END
    }

    bool _handlingRevoked;

    async Task HandleRevokedAsync()
    {
        if (_handlingRevoked || _auth.Current is null) return;
        _handlingRevoked = true;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (_auth.Current is null) return;
                try
                {
                    await DisplayAlertAsync("Signed out", "This computer was disconnected from the cloud by an admin.", "OK");
                }
                catch { /* alert is optional; still return to login */ }
                await SignOutAsync();
            });
        }
        finally
        {
            _handlingRevoked = false;
        }
    }

    public async Task EnterRoleAsync()
    {
        var session = _auth.Current ?? throw new InvalidOperationException("Not signed in.");
        Items.Clear();
        FlyoutBehavior = FlyoutBehavior.Locked;
        FlyoutWidth = ClinicTheme.SidebarWidth;
        ApplyRoleChrome(session.Role);

        List<ClinicNavGroup> groups;
        string firstRoute;
        switch (session.Role)
        {
            case UserRole.Doctor:
                AddFlyout("Today", "today", typeof(TodayPage));
                // JJQR - AI (Nov 21, 2026) - allen3 doctor extra screens; Performance stays a placeholder
                AddFlyout("Appointments", "appointments", typeof(DoctorAppointmentsPage));
                AddFlyout("Patient charts", "patient-charts", typeof(DoctorPatientsPage));
                //JJQR - AI (21 Nov 2026) - wave-end wiring: Extra2 Performance replaces the placeholder
                AddFlyout("Performance", "performance", typeof(ClinicNode.App.Views.Doctor.Extra2.DoctorPerformancePage));
                //JJQR - END
                AddFlyout("Availability", "availability", typeof(DoctorSchedulePage));
                //JJQR - AI (21 Nov 2026) - wave-end wiring: Extra2 Templates workspace (list + editor)
                AddFlyout("Templates", "templates", typeof(ClinicNode.App.Views.Doctor.Extra2.DoctorTemplatesWorkspacePage));
                //JJQR - END
                AddFlyout("Profile", "doctor-profile", typeof(DoctorProfilePage));
                // JJQR - END
                groups =
                [
                    new("Patient care",
                    [
                        new ClinicNavItem("Today", "today", true, "dashboard"),
                        new ClinicNavItem("Appointments", "appointments", true, "event_note"),
                        new ClinicNavItem("Patient charts", "patient-charts", true, "groups"),
                    ]),
                    new("Your practice",
                    [
                        new ClinicNavItem("Performance", "performance", true, "monitoring"), //JJQR - AI (21 Nov 2026) - now a real page //JJQR - END
                        new ClinicNavItem("Availability", "availability", true, "schedule"),
                        new ClinicNavItem("Templates", "templates", true, "description"),
                        new ClinicNavItem("Profile", "doctor-profile", true, "account_circle"),
                    ]),
                ];
                firstRoute = "today";
                break;
            case UserRole.Staff:
                AddFlyout("Dashboard", "staff-home", typeof(StaffHomePage));
                AddFlyout("Payments", "staff-payments", typeof(StaffPaymentsPage));
                AddFlyout("Walk-In", "staff-walk-in", typeof(StaffWalkInPage));
                AddFlyout("Queue", "staff-queue", typeof(StaffQueuePage));
                AddFlyout("Patients", "staff-patients", typeof(StaffPatientsPage));
                AddFlyout("Doctor Status", "staff-doctor-status", typeof(StaffDoctorStatusPage));
                AddFlyout("Announcements", "staff-announcements", typeof(StaffAnnouncementsPage));
                AddFlyout("Profile", "staff-profile", typeof(StaffProfilePage));
                groups =
                [
                    new(null,
                    [
                        new ClinicNavItem("Dashboard", "staff-home", true, "dashboard"),
                        new ClinicNavItem("Payments", "staff-payments", true, "payments"),
                        new ClinicNavItem("Walk-In", "staff-walk-in", true, "directions_walk"),
                        new ClinicNavItem("Queue", "staff-queue", true, "view_list"),
                        new ClinicNavItem("Patients", "staff-patients", true, "group"),
                        new ClinicNavItem("Doctor Status", "staff-doctor-status", true, "medical_services"),
                        new ClinicNavItem("Announcements", "staff-announcements", true, "campaign"),
                        new ClinicNavItem("Profile", "staff-profile", true, "manage_accounts"),
                    ]),
                ];
                firstRoute = "staff-home";
                break;
            case UserRole.Admin:
                AddFlyout("Dashboard", "admin-home", typeof(AdminHomePage));
                AddFlyout("Bookings", "admin-bookings", typeof(AdminBookingsPage));
                AddFlyout("Walk-In", "admin-walk-in", typeof(AdminWalkInPage));
                AddFlyout("Doctors", "admin-doctors", typeof(AdminDoctorsPage));
                AddFlyout("Patients", "admin-patients", typeof(AdminPatientsPage));
                AddFlyout("Staff", "admin-staff", typeof(AdminStaffPage));
                AddFlyout("Announcements", "admin-announcements", typeof(AdminAnnouncementsPage));
                AddFlyout("Settings", "admin-settings", typeof(AdminSettingsPage));
                AddFlyout("Audit Logs", "admin-audit", typeof(AdminAuditPage));
                AddFlyout("Reports", "admin-reports", typeof(AdminReportsPage));
                //JJQR - AI (21 Nov 2026) - wave-end wiring: System group pages (allen4)
                AddFlyout("Sync computers", "admin-sync-computers", typeof(AdminSyncComputersPage));
                AddFlyout("Receipt clashes", "admin-or-clashes", typeof(AdminOrClashesPage));
                //JJQR - END
                AddFlyout("Register this node", "admin-register-node", typeof(AdminRegisterNodePage));
                //JJQR - AI (Nov 21, 2026) - allen5: this-computer settings (admin only; can change the sync URL)
                AddFlyout("This computer", "node-settings", typeof(ClinicNode.App.Views.Settings.Node.NodeSettingsPage));
                //JJQR - END
                groups =
                [
                    new("Operations",
                    [
                        new ClinicNavItem("Dashboard", "admin-home", true, "dashboard"),
                        new ClinicNavItem("Bookings", "admin-bookings", true, "calendar_today"),
                        new ClinicNavItem("Walk-In", "admin-walk-in", true, "directions_walk"),
                    ]),
                    new("Management",
                    [
                        new ClinicNavItem("Doctors", "admin-doctors", true, "medical_services"),
                        new ClinicNavItem("Patients", "admin-patients", true, "group"),
                        new ClinicNavItem("Staff", "admin-staff", true, "badge"),
                        new ClinicNavItem("Announcements", "admin-announcements", true, "campaign"),
                    ]),
                    new("System",
                    [
                        new ClinicNavItem("Settings", "admin-settings", true, "settings"),
                        new ClinicNavItem("Audit Logs", "admin-audit", true, "history"),
                        new ClinicNavItem("Reports", "admin-reports", true, "monitoring"),
                        //JJQR - AI (21 Nov 2026) - wave-end wiring
                        new ClinicNavItem("Sync computers", "admin-sync-computers", true, "devices"),
                        new ClinicNavItem("Receipt clashes", "admin-or-clashes", true, "receipt_long"),
                        //JJQR - END
                        //JJQR - AI (Nov 21, 2026) - allen5: this-computer settings (admin only)
                        new ClinicNavItem("This computer", "node-settings", true, "settings"),
                        //JJQR - END
                    ]),
                ];
                firstRoute = "admin-home";
                break;
            default:
                throw new InvalidOperationException("Unsupported role on the clinic node.");
        }

        _sidebar = new ClinicSidebar(this, session.Role, groups, firstRoute);
        //JJQR - AI (Nov 21, 2026) - Mac Catalyst never drew a custom FlyoutContent (blank column). The flyout HEADER slot renders reliably, so the whole sidebar lives there and the default item list is emptied.
        FlyoutContent = null;
        Shell.SetItemTemplate(this, new DataTemplate(() => new BoxView { HeightRequest = 0, WidthRequest = 0, Color = Colors.Transparent }));
        FlyoutHeader = _sidebar;
        FlyoutHeaderBehavior = FlyoutHeaderBehavior.Fixed;
        //JJQR - END
        // JJQR - AI (Nov 21, 2026)
        Shell.SetTitleView(this, _services.GetRequiredService<TopBarView>());
        StartSessionWatch();
        // JJQR - END
        await GoToAsync("//" + firstRoute);
        //JJQR - AI (Nov 21, 2026) - Mac Catalyst drew an empty sidebar column when FlyoutContent was attached while the flyout was still switching from Disabled to Locked: attach it again once the first page is up
        //JJQR - END
    }

    //JJQR - AI (Nov 22, 2026) - offline-mode plan step 3.5: the Logout button: sync what is waiting first, ask if some is still waiting. Forced sign-outs (revoked, expired) do not ask.
    //JJQR - AI (Nov 22, 2026) - owner: Logout could be clicked many times and started the sign-out many times. One sign-out at a time; extra clicks do nothing.
    bool _signOutRunning;
    public async Task UserSignOutAsync()
    {
        if (_signOutRunning) return;
        _signOutRunning = true;
        try
        {
            if (await ClinicNode.App.Views.Shared.SyncGuard.ConfirmLeavingAsync(_services, "signout", "Sign out anyway")) await SignOutAsync();
        }
        finally { _signOutRunning = false; }
    }
    //JJQR - END
    //JJQR - END

    public async Task SignOutAsync()
    {
        // JJQR - AI (Nov 21, 2026)
        StopSessionWatch();
        // JJQR - END
        await _auth.LogoutAsync();
        Items.Clear();
        FlyoutContent = null;
        FlyoutHeader = null; //JJQR - AI (Nov 21, 2026) - the sidebar now lives in the header slot //JJQR - END
        _sidebar = null;
        FlyoutBehavior = FlyoutBehavior.Disabled;
        ApplyLoginChrome();
        Items.Add(new ShellContent { Title = "Sign in", Route = "login", ContentTemplate = new DataTemplate(typeof(Views.LoginPage)) });
        // JJQR - AI (Nov 21, 2026)
        Shell.SetTitleView(this, null);
        // JJQR - END
        await GoToAsync("//login");
    }

    void ApplyRoleChrome(UserRole role)
    {
        var light = false; //JJQR - AI (Nov 21, 2026) desktop redesign: dark teal sidebar for every role //JJQR - END
        FlyoutBackgroundColor = light ? ClinicTheme.Color("SurfaceContainerLowest") : ClinicTheme.Color("InverseSurface");
        SetValue(Shell.BackgroundColorProperty, ClinicTheme.Color("SurfaceContainerLowest"));
        SetValue(Shell.ForegroundColorProperty, ClinicTheme.Color("OnSurface"));
        SetValue(Shell.TitleColorProperty, ClinicTheme.Color("OnSurface"));
    }

    void ApplyLoginChrome()
    {
        FlyoutBackgroundColor = ClinicTheme.Color("SurfaceContainerLowest");
        SetValue(Shell.BackgroundColorProperty, ClinicTheme.Color("SurfaceContainerLowest"));
        SetValue(Shell.ForegroundColorProperty, ClinicTheme.Color("OnSurface"));
        SetValue(Shell.TitleColorProperty, ClinicTheme.Color("OnSurface"));
    }

    void AddFlyout(string title, string route, Type pageType)
    {
        Items.Add(new FlyoutItem
        {
            Title = title,
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsSingleItem,
            Items =
            {
                new ShellContent
                {
                    Title = title,
                    Route = route,
                    ContentTemplate = new DataTemplate(() => (Page)_services.GetRequiredService(pageType)),
                }
            }
        });
    }

    void AddPlaceholder(string title, string route, string message)
    {
        Items.Add(new FlyoutItem
        {
            Title = title,
            FlyoutDisplayOptions = FlyoutDisplayOptions.AsSingleItem,
            Items =
            {
                new ShellContent
                {
                    Title = title,
                    Route = route,
                    Content = new PlaceholderPage(title, message),
                }
            }
        });
    }
    // JJQR - AI (Nov 21, 2026) - periodic session check (every minute and on resume)
    void StartSessionWatch()
    {
        if (_sessionTimer is null)
        {
            _sessionTimer = Dispatcher.CreateTimer();
            _sessionTimer.Interval = TimeSpan.FromMinutes(1);
            _sessionTimer.Tick += OnSessionTimerTick;
        }
        _sessionTimer.Start();
        HookSessionWindow();
    }

    void StopSessionWatch()
    {
        if (_sessionTimer is not null)
        {
            _sessionTimer.Stop();
            _sessionTimer.Tick -= OnSessionTimerTick;
            _sessionTimer = null;
        }
        if (_watchedWindow is not null)
        {
            _watchedWindow.Resumed -= OnWindowResumed;
            _watchedWindow = null;
        }
    }

    void HookSessionWindow()
    {
        var window = Window ?? Application.Current?.Windows.FirstOrDefault();
        if (ReferenceEquals(_watchedWindow, window)) return;
        if (_watchedWindow is not null)
            _watchedWindow.Resumed -= OnWindowResumed;
        _watchedWindow = window;
        if (_watchedWindow is not null)
            _watchedWindow.Resumed += OnWindowResumed;
    }

    void OnSessionTimerTick(object? sender, EventArgs e) => _ = CheckSessionAsync();

    void OnWindowResumed(object? sender, EventArgs e) => _ = CheckSessionAsync();

    async Task CheckSessionAsync()
    {
        if (_auth.Current is null || _sessionCheckRunning) return;
        _sessionCheckRunning = true;
        try
        {
            var message = await _auth.ValidateCurrentAsync();
            if (string.IsNullOrEmpty(message)) return;
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (_auth.Current is null && _sidebar is null) return;
                try { await DisplayAlertAsync("Signed out", message, "OK"); }
                catch { /* alert is optional; still return to login */ }
                await SignOutAsync();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _sessionCheckRunning = false;
        }
    }
    // JJQR - END

}

public sealed class PlaceholderPage : ContentPage
{
    public PlaceholderPage(string title, string message)
    {
        Title = title;
        BackgroundColor = ClinicTheme.Color("Background");
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = 24,
                Spacing = 12,
                MaximumWidthRequest = 720,
                Children =
                {
                    ClinicTheme.Text(title, "HeadlineMd"),
                    ClinicTheme.Text(message, "BodyMd"),
                    ClinicTheme.Text("This screen is inventoried for a 1:1 port and will be built in the next slice. Existing clinic-day screens already run against the local database.", "MutedBody"),
                }
            }
        };
    }
}
// JJQR - END
