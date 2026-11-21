// JJQR - AI (Nov 21, 2026) - compose Data + Core services for the MAUI app and for tests.
using ClinicNode.Core.Auth;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Events;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Payments;
using ClinicNode.Core.Print;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Visits;
using ClinicNode.Data;
using Microsoft.Extensions.DependencyInjection;
//JJQR - AI (Nov 21, 2026)
using Microsoft.Extensions.DependencyInjection.Extensions;
using ClinicNode.Core.Sync;
//JJQR - END

namespace ClinicNode.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddClinicNodeCore(this IServiceCollection services, NodeOptions options, IEmailMemory? emailMemory = null)
    {
        services.AddClinicNodeData(options);
        services.TryAddSingleton(TimeProvider.System);
        //JJQR - AI (Nov 21, 2026) - no-op default; ClinicNode.Sync replaces it
        services.TryAddSingleton<ISyncBridge, NoOpSyncBridge>();
        //JJQR - END
        services.AddSingleton<IEmailMemory>(emailMemory ?? new FileEmailMemory(options.DataDirectory));
        services.AddSingleton<IClinicEventBus, ClinicEventBus>();
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IConsultationService, ConsultationService>();
        services.AddSingleton<IPatientService, PatientService>();
        services.AddSingleton<IQueueService, QueueService>();
        services.AddSingleton<IVisitStarter, VisitStarter>();
        services.AddSingleton<IPaymentService, PaymentService>();
        services.AddSingleton<IPrintDocumentFactory, PrintDocumentFactory>();
        // JJQR - AI (Nov 21, 2026) - doctor consultation catalogue / history helpers
        services.AddSingleton<ClinicNode.Core.Practice.IDoctorCatalogService, ClinicNode.Core.Practice.DoctorCatalogService>();
        services.AddSingleton<ClinicNode.Core.Practice.IDoctorDashboardService, ClinicNode.Core.Practice.DoctorDashboardService>();
        services.AddSingleton<ClinicNode.Core.Practice.IGlobalSearchService, ClinicNode.Core.Practice.GlobalSearchService>();
        // JJQR - END
        return services;
    }
}
// JJQR - END
