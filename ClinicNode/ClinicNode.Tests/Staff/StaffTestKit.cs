//JJQR - AI (Nov 21, 2026) - helpers for staff tests: build Core/Staff services from the shared TestHost without editing it.
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Queue;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.Tests.Staff;

internal static class StaffTestKit
{
    public static T Make<T>(this TestHost host) where T : class => ActivatorUtilities.CreateInstance<T>(host.Services);

    public static async Task<ClinicApp.Domain.Entities.Patient> NewPatientAsync(this TestHost host, string first = "Ana", string last = "Reyes", DateOnly? dob = null) =>
        await host.Get<IPatientService>().CreateAsync(new NewPatientRequest(first, last, dob ?? new DateOnly(1990, 1, 1), SexType.Female, ContactNumber: "0917 123 4567"));

    public static async Task<WalkInTicket> WalkInAsync(this TestHost host, Guid patientId, string? discount = null)
    {
        var r = await host.Get<IQueueService>().CheckInWalkInAsync(new WalkInRequest(patientId, null, DiscountCategory: discount));
        Assert.True(r.Succeeded, r.Error);
        return r.Ticket!;
    }

    public static async Task<WalkInTicket> CompletedVisitAsync(this TestHost host, string first = "Ana")
    {
        var p = await host.NewPatientAsync(first);
        var t = await host.WalkInAsync(p.PatientId);
        var q = host.Get<IQueueService>();
        Assert.True((await q.CallAsync(t.BookingId)).Succeeded);
        Assert.True((await q.CompleteAsync(t.BookingId)).Succeeded);
        return t;
    }

    public static async Task<ClinicAppDbContext> DbAsync(this TestHost host) =>
        await host.Get<IDbContextFactory<ClinicAppDbContext>>().CreateDbContextAsync();
}
//JJQR - END
