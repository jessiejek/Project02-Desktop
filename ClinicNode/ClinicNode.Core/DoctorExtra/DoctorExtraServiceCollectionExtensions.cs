//JJQR - AI (Nov 21, 2026) - DI for the extra doctor screens (appointments, patient charts, availability, profile, templates).
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Core.DoctorExtra;

public static class DoctorExtraServiceCollectionExtensions
{
    public static IServiceCollection AddClinicNodeDoctorExtra(this IServiceCollection services)
    {
        services.AddSingleton<IDoctorAppointmentsService, DoctorAppointmentsService>();
        services.AddSingleton<IDoctorPatientsService, DoctorPatientsService>();
        services.AddSingleton<IDoctorScheduleService, DoctorScheduleService>();
        services.AddSingleton<IDoctorProfileService, DoctorProfileService>();
        services.AddSingleton<IDoctorTemplatesService, DoctorTemplatesService>();
        services.AddClinicNodeDoctorSelfEdit(); //JJQR - AI (Nov 21, 2026) doctor self-edit write path (§5.7) //JJQR - END
        return services;
    }
}
//JJQR - END
