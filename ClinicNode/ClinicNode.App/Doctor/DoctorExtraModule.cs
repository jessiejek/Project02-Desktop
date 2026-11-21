//JJQR - AI (21 Nov 2026) - doctor-portal overflow screens module (Appointments, Patient charts + prescriptions, Schedule, Profile, printouts):
// every DI registration for them in ONE place, so MauiProgram.cs stays untouched. Templates and Performance belong to allen1 (Views/Doctor/Extra2) and
// are NOT registered here.
// Online-first: each service is routed Api (ClinicNode.Api, snake_case JSON) -> the existing local SQLite service only when the server can't be
// reached (or there is no online sign-in).
//
// Wire-up (owner / allen0):
//   MauiProgram.AddClinicNodeAppServices - AFTER the AddClinicNodeOnlineFirst(...) line:   services.AddDoctorExtraModule();
//   AppShell: nothing - these screens use the existing flyout routes; detail screens are pushed (no new routes).
using ClinicNode.Api.OnlineFirst;
using ClinicNode.Api.Services;
using ClinicNode.App.Views.Doctor.Extra;
using ClinicNode.Core.DoctorExtra;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClinicNode.App;

public static class DoctorExtraModule
{
    /// <summary>
    /// Registers the doctor overflow services online-first and their pages. Call once, after AddClinicNodeDoctorExtra() and
    /// AddClinicNodeOnlineFirst(...) (it needs the local services, the API client and the connectivity monitor they register).
    /// </summary>
    public static IServiceCollection AddDoctorExtraModule(this IServiceCollection services)
    {
        // ── existing read services (tested Core interfaces, unchanged) ────────────────────────────────
        services.AddOnlineFirst<IDoctorAppointmentsService, ApiDoctorAppointmentsService, DoctorAppointmentsService>(); // appointments list + detail
        services.AddOnlineFirst<IDoctorPatientsService, ApiDoctorPatientsService, DoctorPatientsService>();             // patient list + chart
        services.AddOnlineFirst<IDoctorScheduleService, ApiDoctorScheduleService, DoctorScheduleService>();             // weekly hours + blocked dates
        services.AddOnlineFirst<IDoctorProfileService, ApiDoctorProfileService, DoctorProfileService>();                // GET /api/doctors/me (password: local -> staff profile)

        // ── new editable surfaces (Core/DoctorExtra/DoctorExtraEditing.cs) ────────────────────────────
        services.AddOnlineFirst<IDoctorBioService, ApiDoctorBioService, DoctorBioService>();                                       // PUT /api/doctors/me { bio }
        services.AddOnlineFirst<IDoctorPrescriptionsService, ApiDoctorPrescriptionsService, DoctorPrescriptionsService>();         // chart Rx create / edit / copy / delete
        services.AddOnlineFirst<IDoctorPrintDataService, ApiDoctorPrintDataService, DoctorPrintDataService>();                     // patient card, appointment summary

        // ── pages (TryAdd: harmless next to the lines already in MauiProgram) ─────────────────────────
        services.TryAddTransient<DoctorAppointmentsPage>();
        services.TryAddTransient<DoctorPatientsPage>();
        services.TryAddTransient<DoctorSchedulePage>();
        services.TryAddTransient<DoctorProfilePage>();
        return services;
    }
}
//JJQR - END
