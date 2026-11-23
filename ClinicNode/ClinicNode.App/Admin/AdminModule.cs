//JJQR - AI (21 Nov 2026) - Admin module wiring (online-first admin portal). Call from MauiProgram AFTER AddClinicNodeAdmin() and AFTER
// AddClinicNodeOnlineFirst(...) (the Api client must be registered first), and call AdminModule.RegisterAdminRoutes() from the AppShell constructor.
//   services.AddAdminModule();
//   AdminModule.RegisterAdminRoutes();
// Each admin service becomes Api-first with the local SQLite service as the fallback (no API session / clinic server unreachable).
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.App.Services.Files;
using ClinicNode.App.ViewModels.Admin;
using ClinicNode.App.Views.Admin;
using ClinicNode.Core.Admin;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.App.Admin;

public static class AdminModule
{
    /// <summary>Routes pushed from admin pages that AppShell does not already register. Flyout routes (admin-sync-computers) come from AddFlyout.</summary>
    //JJQR - AI (21 Nov 2026) - admin-or-clashes is a flyout route too (AddFlyout in AppShell), so it is not listed here. //JJQR - END
    static readonly (string Route, Type Page)[] PushedRoutes =
    [
        ("admin-booking-detail", typeof(AdminBookingDetailPage)),
        ("admin-patient-detail", typeof(AdminPatientDetailPage)),
        ("admin-doctor-new", typeof(AdminDoctorNewPage)),
        ("admin-doctor-edit", typeof(AdminDoctorEditPage)),
    ];

    static bool _routesRegistered;

    public static IServiceCollection AddAdminModule(this IServiceCollection services)
    {
        // Local (SQLite) services are kept; these only add the Api twins and the router in front of them.
        services.TryAddSingleton<IAdminOrNumberAllocator, AdminOrNumberAllocator>();
        services.AddOnlineFirst<IAdminDashboardService, ApiAdminDashboardService, AdminDashboardService>();
        //JJQR - AI (21 Nov 2026) - write-path audit: the offline side keeps only the writes the server accepts from a node (see OnlineOnlyAdminWrites.cs);
        // the plain local services are registered as concretes for the guards to wrap.
        services.TryAddSingleton<AdminBookingService>();
        services.TryAddSingleton<AdminDoctorService>();
        services.TryAddSingleton<AdminStaffService>();
        services.TryAddSingleton<AdminPatientService>();
        services.TryAddSingleton<AdminAnnouncementService>();
        services.AddOnlineFirst<IAdminBookingService, ApiAdminBookingService, OfflineGuardedAdminBookingService>();
        services.AddOnlineFirst<IAdminDoctorService, ApiAdminDoctorService, OfflineGuardedAdminDoctorService>();
        services.AddOnlineFirst<IAdminStaffService, ApiAdminStaffService, OfflineGuardedAdminStaffService>();
        services.AddOnlineFirst<IAdminPatientService, ApiAdminPatientService, OfflineGuardedAdminPatientService>();
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - settings/fees are online-only (Jessie): the offline side refuses writes instead of queuing an outbox row
        services.AddOnlineFirst<IAdminSettingsService, ApiAdminSettingsService, OnlineOnlyAdminSettingsService>();
        //JJQR - END
        //JJQR - AI (21 Nov 2026) - announcements are cloud-owned: offline writes refused
        services.AddOnlineFirst<IAdminAnnouncementService, ApiAdminAnnouncementService, OfflineGuardedAdminAnnouncementService>();
        //JJQR - END
        services.AddOnlineFirst<IAdminAuditLogService, ApiAdminAuditLogService, AdminAuditLogService>();
        services.AddOnlineFirst<IAdminReportService, ApiAdminReportService, AdminReportService>();
        services.AddOnlineFirst<IAdminNodeService, ApiAdminNodeService, LocalAdminNodeService>();

        // CSV export (reports) through the OS Save dialog.
        services.TryAddSingleton<IFileSaver, FileSaver>();

        // Sync computers screen (new).
        services.TryAddTransient<AdminSyncComputersViewModel>();
        services.TryAddTransient<AdminSyncComputersPage>();

        //JJQR - AI (21 Nov 2026) - OR uniqueness (approved by Jessie via allen0): the 409 or_number_duplicate prompt used by ApiAdminBookingService,
        // and the read-only "Receipt number clashes" screen (System group, flyout route admin-or-clashes, next to Sync computers).
        services.TryAddSingleton<IAdminOrNumberPrompt, AdminOrNumberPrompt>();
        services.AddOnlineFirst<IAdminOrClashService, ApiAdminOrClashService, LocalAdminOrClashService>();
        services.TryAddTransient<AdminOrClashesViewModel>();
        services.TryAddTransient<AdminOrClashesPage>();
        //JJQR - END
        //JJQR - AI (Nov 23, 2026) - web parity: admin Sync review (possible duplicate patients and computer problems; merge, dismiss, resolve). Online only.
        services.AddOnlineFirst<IAdminSyncReviewService, ApiAdminSyncReviewService, LocalAdminSyncReviewService>();
        services.TryAddTransient<AdminSyncReviewViewModel>();
        services.TryAddTransient<AdminSyncReviewPage>();
        //JJQR - END
        return services;
    }

    /// <summary>Safe to call more than once and alongside AppShell's own registrations (already-known routes are skipped).</summary>
    public static void RegisterAdminRoutes()
    {
        if (_routesRegistered) return;
        _routesRegistered = true;
        foreach (var (route, page) in PushedRoutes)
        {
            try { Routing.RegisterRoute(route, page); }
            catch (ArgumentException) { /* already registered by AppShell */ }
        }
    }
}
//JJQR - END
