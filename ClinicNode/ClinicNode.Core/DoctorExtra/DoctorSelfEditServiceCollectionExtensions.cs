//JJQR - AI (Nov 21, 2026) - DI for the doctor self-edit write path (called from AddClinicNodeDoctorExtra).
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Core.DoctorExtra;

public static class DoctorSelfEditServiceCollectionExtensions
{
    public static IServiceCollection AddClinicNodeDoctorSelfEdit(this IServiceCollection services)
    {
        services.AddSingleton<IDoctorSelfEditService, DoctorSelfEditService>();
        return services;
    }
}
//JJQR - END
