//JJQR - AI (21 Nov 2026) - staff (front desk) module: every staff DI registration and route in ONE place, so MauiProgram.cs / AppShell stay untouched.
// Online-first: each staff service is routed Api (http://localhost:5055 via ClinicNode.Api) → local SQLite only when the server can't be reached
// (or there is no online sign-in); writes made online ask the sync loop for a pull so the local copy follows.
//
// Wire-up (owner / allen0):
//   MauiProgram.AddClinicNodeAppServices — AFTER the AddClinicNodeOnlineFirst(...) line:   services.AddStaffModule();
//   AppShell constructor (next to the other Routing.RegisterRoute calls):                  StaffModule.RegisterStaffRoutes();
using ClinicNode.Api.Http;
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.App.Services;
using ClinicNode.App.ViewModels.Staff;
using ClinicNode.App.Views.Staff;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Staff;
using ClinicNode.Core.Sync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.App;

public static class StaffModule
{
    /// <summary>
    /// Registers the staff services online-first (ApiStaffXService + the existing local service) and the staff pages.
    /// Call once, after AddClinicNodeStaff() and AddClinicNodeOnlineFirst(...) (it needs the API client and connectivity monitor they register).
    /// </summary>
    public static IServiceCollection AddStaffModule(this IServiceCollection services)
    {
        // ── online-first routers (one line per screen's service) ─────────────────────────────────────
        services.AddOnlineFirst<IStaffDashboardService, ApiStaffDashboardService, StaffDashboardService>();      // dashboard
        services.AddOnlineFirst<IStaffQueueBoardService, ApiStaffQueueBoardService, StaffQueueBoardService>();   // queue
        services.AddOnlineFirst<IStaffWalkInService, ApiStaffWalkInService, StaffWalkInService>();               // walk-in (R-/S- ticket)
        services.AddOnlineFirst<IStaffPaymentsService, ApiStaffPaymentsService, StaffPaymentsService>();         // payments (+ booking-detail record / waive / receipt)
        services.AddOnlineFirst<IStaffPatientsService, ApiStaffPatientsService, StaffPatientsService>();         // patients + detail, quick register
        services.AddOnlineFirst<IStaffBookingService, ApiStaffBookingService, StaffBookingService>();            // booking detail
        services.AddOnlineFirst<IDoctorStatusService, ApiStaffDoctorStatusService, DoctorStatusService>();       // doctor status
        services.AddOnlineFirst<IStaffAnnouncementsService, ApiStaffAnnouncementsService, StaffAnnouncementsService>(); // announcements

        // profile: same router, kept inside the existing SessionRefreshingStaffProfileService so a name change still refreshes the signed-in session.
        services.TryAddSingleton<StaffProfileService>(sp => ActivatorUtilities.CreateInstance<StaffProfileService>(sp));
        services.TryAddSingleton<ApiStaffProfileService>(sp => ActivatorUtilities.CreateInstance<ApiStaffProfileService>(sp));
        services.RemoveAll<IStaffProfileService>();
        services.AddSingleton<IStaffProfileService>(sp => new SessionRefreshingStaffProfileService(
            OnlineFirstProxy<IStaffProfileService>.Create(
                sp.GetRequiredService<ApiStaffProfileService>(), sp.GetRequiredService<StaffProfileService>(),
                sp.GetRequiredService<IApiConnectivity>(), sp.GetService<ISyncBridge>(),
                hasApiSession: () => sp.GetRequiredService<IApiTokenStore>().AccessToken is { Length: > 0 },
                dropApiSession: () => sp.GetRequiredService<IApiTokenStore>().Clear()),
            sp.GetRequiredService<IAuthService>()));

        // ── pages & view-models (TryAdd: harmless next to the lines already in MauiProgram) ────────────
        services.TryAddTransient<WalkInWizardViewModel>();
        services.TryAddTransient<StaffHomePage>();
        services.TryAddTransient<StaffQueuePage>();
        services.TryAddTransient<StaffWalkInPage>();
        services.TryAddTransient<StaffPaymentsPage>();
        services.TryAddTransient<StaffPatientsPage>();
        services.TryAddTransient<StaffDoctorStatusPage>();
        services.TryAddTransient<StaffAnnouncementsPage>();
        services.TryAddTransient<StaffProfilePage>();
        return services;
    }

    private static bool _routesRegistered;

    /// <summary>
    /// Staff routes. The eight sidebar routes (staff-home, staff-payments, staff-walk-in, staff-queue, staff-patients, staff-doctor-status,
    /// staff-announcements, staff-profile) are created by AppShell's flyout; the detail screens (booking, patient, record payment, quick register,
    /// receipt) take an id and are pushed with StaffUi.PushAsync / modals, so no Shell route is needed for them. Idempotent.
    /// </summary>
    public static void RegisterStaffRoutes()
    {
        if (_routesRegistered) return;
        _routesRegistered = true;
        // Intentionally empty today: kept as the single place to add a staff deep-link route later.
    }
}
//JJQR - END
