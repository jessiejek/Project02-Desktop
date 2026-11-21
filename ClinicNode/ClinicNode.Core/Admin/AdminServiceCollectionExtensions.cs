// JJQR - AI (Nov 21, 2026) - DI for admin services. Allen: call services.AddClinicNodeAdmin() from AddClinicNodeCore (or MauiProgram).
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Core.Admin;

public static class AdminServiceCollectionExtensions
{
    //JJQR - AI (21 Nov 2026) - allen0 I-1: wiring note resolved; wired by allen0 21 Nov 2026 (MauiProgram.cs / AppShell.xaml.cs). //JJQR - END
    public static IServiceCollection AddClinicNodeAdmin(this IServiceCollection services)
    {
        services.AddSingleton<IAdminAnnouncementService, AdminAnnouncementService>();
        services.AddSingleton<IAdminBookingService, AdminBookingService>();
        services.AddSingleton<IAdminDoctorService, AdminDoctorService>();
        services.AddSingleton<IAdminPatientService, AdminPatientService>();
        services.AddSingleton<IAdminStaffService, AdminStaffService>();
        services.AddSingleton<IAdminSettingsService, AdminSettingsService>();
        services.AddSingleton<IAdminAuditLogService, AdminAuditLogService>();
        services.AddSingleton<IAdminReportService, AdminReportService>();
        services.AddSingleton<IAdminDashboardService, AdminDashboardService>();
        return services;
    }
}
// JJQR - END
