//JJQR - AI (21 Nov 2026) - doctor lane DI + routes (Consultation + Today), so MauiProgram.cs / AppShell need ONE line each:
//   MauiProgram.cs (AFTER builder.Services.AddClinicNodeOnlineFirst(...)):  builder.Services.AddDoctorModule();
//   AppShell.xaml.cs constructor (after the existing Routing.RegisterRoute lines):  DoctorModule.RegisterDoctorRoutes();
// Only this lane's screens/services are registered here (allen3's Extra pages are not touched).
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.App.Views.Doctor;
using ClinicNode.Core.Admin; //JJQR - AI (2026-11-26) - GAV-11 //JJQR - END
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Doctor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.App.Doctor;

public static class DoctorModule
{
    /// <summary>
    /// Online-first consultation: ApiConsultationService / ApiConsultationLibraryService when the ClinicNode.Api (http://localhost:5055) is reachable
    /// and signed in, local SQLite (DoctorConsultationLocalService → ConsultationService, ConsultationLibraryService) otherwise.
    /// Must run after the Core registrations and after AddClinicNodeOnlineFirst (AddOnlineFirst replaces the interface registration).
    /// </summary>
    public static IServiceCollection AddDoctorModule(this IServiceCollection services)
    {
        // the existing local consultation service, resolvable as itself so the doctor-lane wrapper can delegate to it
        services.TryAddSingleton<ConsultationService>();
        services.TryAddSingleton<IConsultationLibraryService, ConsultationLibraryService>();

        services.AddOnlineFirst<IConsultationService, ApiConsultationService, DoctorConsultationLocalService>();
        services.AddOnlineFirst<IConsultationLibraryService, ApiConsultationLibraryService, ConsultationLibraryService>();
        //JJQR - AI (21 Nov 2026) - inventory G9: Call next / Start consultation online-first (PUT /api/queue/{id}/call)
        services.AddOnlineFirst<IConsultationQueueService, ApiConsultationQueueService, LocalConsultationQueueService>();
        //JJQR - END
        //JJQR - AI (2026-11-26) - GAV-11: "Wrong patient?" is online only (offline side = "needs an internet connection") and syncs this visit first
        services.TryAddSingleton<OfflineVisitReassignService>();
        services.AddOnlineFirst<IVisitReassignService, ApiVisitReassignService, OfflineVisitReassignService>();
        services.TryAddSingleton<IVisitSyncGate>(sp => new ClinicNode.Sync.Local.VisitSyncGate(
            sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>(), sp.GetService<ClinicNode.Sync.ISyncService>()));
        //JJQR - END
        //JJQR - AI (2026-11-26) - GAV-12: "Merge to existing patient" is online only (offline side = "needs an internet connection") and syncs this patient first
        services.TryAddSingleton<OfflinePatientMergeService>();
        services.AddOnlineFirst<IPatientMergeService, ApiPatientMergeService, OfflinePatientMergeService>();
        services.TryAddSingleton<IPatientSyncGate>(sp => new ClinicNode.Sync.Local.PatientSyncGate(
            sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>(), sp.GetService<ClinicNode.Sync.ISyncService>()));
        //JJQR - END

        // pages (already registered in MauiProgram today; TryAdd keeps this harmless and lets MauiProgram drop them later)
        services.TryAddTransient<ConsultationPage>();
        services.TryAddTransient<TodayPage>();
        return services;
    }

    /// <summary>Routes for this lane. "consult" is already registered by AppShell, so it is only added when missing.</summary>
    public static void RegisterDoctorRoutes()
    {
        TryRegister("consult", typeof(ConsultationPage));
    }

    private static void TryRegister(string route, Type page)
    {
        try { Routing.RegisterRoute(route, page); }
        catch (ArgumentException) { /* already registered (AppShell) */ }
    }
}
//JJQR - END
