//JJQR - AI (21 Nov 2026) - doctor Templates (full workspace: SOAP / phrases / Rx sets / favorites / certificates, add-edit-duplicate-delete-undo-reorder-restore) and Performance:
// every DI registration + the routes in ONE place, so MauiProgram.cs and AppShell stay untouched.
// Wire-up (owner / allen0):
//   MauiProgram.AddClinicNodeAppServices - AFTER AddDoctorExtraModule():   services.AddDoctorExtra2Module();
//   AppShell (doctor flyout): replace the "Performance" placeholder and the DoctorTemplatesPage entry:
//       AddFlyout("Performance", "performance", typeof(DoctorPerformancePage));     // ClinicNode.App.Views.Doctor.Extra2
//       AddFlyout("Templates",   "templates",   typeof(DoctorTemplatesWorkspacePage)); // (set the Templates nav item's isShellRoute accordingly)
//   or, with no AppShell edit at all, call DoctorExtra2Module.RegisterDoctorExtra2Routes() once and navigate to "doctor-templates" / "doctor-performance".
// Performance needs AddOnlineFirstDoctorPerformance() (already part of AddClinicNodeOnlineFirst(...)); registered again here with TryAdd only if missing.
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.App.Views.Doctor.Extra2;
using ClinicNode.Core.DoctorExtra;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.App;

public static class DoctorExtra2Module
{
    public static IServiceCollection AddDoctorExtra2Module(this IServiceCollection services)
    {
        // templates workspace: API first (GET/POST/PUT/DELETE /api/soap-templates, /api/soap-phrases, /api/prescription-templates, /api/doctor-favorite-medicines, /api/medical-certificate-templates), local read when offline
        services.AddOnlineFirst<IDoctorTemplatesWorkspaceService, ApiDoctorTemplatesWorkspaceService, DoctorTemplatesWorkspaceService>();
        // performance: the existing online-first registration (GET /api/doctor/performance); skipped if AddClinicNodeOnlineFirst already added it
        if (!services.Any(d => d.ServiceType == typeof(IDoctorPerformanceService))) services.AddOnlineFirstDoctorPerformance();
        services.TryAddTransient<DoctorTemplatesWorkspacePage>();
        services.TryAddTransient<DoctorPerformancePage>();
        return services;
    }

    /// <summary>Registers Shell routes "doctor-templates" and "doctor-performance" (no AppShell edit needed to reach the pages with Shell.Current.GoToAsync).</summary>
    public static void RegisterDoctorExtra2Routes()
    {
        Routing.RegisterRoute("doctor-templates", typeof(DoctorTemplatesWorkspacePage));
        Routing.RegisterRoute("doctor-performance", typeof(DoctorPerformancePage));
    }
}
//JJQR - END
