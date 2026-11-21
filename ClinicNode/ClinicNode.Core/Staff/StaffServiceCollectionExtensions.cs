//JJQR - AI (Nov 21, 2026) - one call to register every front-desk service (call after AddClinicNodeCore).
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Core.Staff;

public static class StaffServiceCollectionExtensions
{
    public static IServiceCollection AddClinicNodeStaff(this IServiceCollection services)
    {
        services.AddSingleton<IStaffDashboardService, StaffDashboardService>();
        services.AddSingleton<IStaffQueueBoardService, StaffQueueBoardService>();
        services.AddSingleton<IStaffPaymentsService, StaffPaymentsService>();
        services.AddSingleton<IStaffPatientsService, StaffPatientsService>();
        services.AddSingleton<IStaffBookingService, StaffBookingService>();
        services.AddSingleton<IStaffProfileService, StaffProfileService>();
        services.AddSingleton<IDoctorStatusService, DoctorStatusService>();
        services.AddSingleton<IStaffAnnouncementsService, StaffAnnouncementsService>();
        return services;
    }
}
//JJQR - END
