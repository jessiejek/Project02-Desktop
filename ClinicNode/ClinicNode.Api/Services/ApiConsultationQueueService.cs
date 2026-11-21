//JJQR - AI (21 Nov 2026) - inventory G9, desktop conversion only (no new endpoints):
//   GET /api/bookings/doctor/today          → this doctor's own bookings today (same source as ApiDoctorDashboardService)
//   PUT /api/queue/{bookingId}/call  {}     → CheckedIn → InProgress (QueueController.SetStatus: a doctor only moves their own queue;
//                                             400 = illegal transition, 403 = not your patient, 404 = unknown booking)
// Same rules as the local DoctorDashboardService: Call next = first CheckedIn (S- priority lane first, then queue order);
// Start = already InProgress → Ok, not CheckedIn → "This patient is not ready to consult.".
// Server refusals come back as StaffResult.Fail; network errors throw so the router falls back to the local service.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Doctor;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Staff;

namespace ClinicNode.Api.Services;

public sealed class ApiConsultationQueueService(IApiClient api) : IConsultationQueueService
{
    public async Task<StaffResult> CallNextAsync(Guid doctorId, CancellationToken ct = default)
    {
        var today = await TodayAsync(doctorId, ct);
        var next = today.Where(b => b.Status == BookingStatus.CheckedIn)
            .OrderBy(b => b.QueueNumber ?? "", Comparer<string>.Create(DoctorExtraRules.CompareQueueNumbers))
            .FirstOrDefault();
        if (next is null) return StaffResult.Fail("No one is waiting to be called.");
        return await CallAsync(next.BookingId, ct);
    }

    public async Task<StaffResult> StartConsultAsync(Guid doctorId, Guid bookingId, CancellationToken ct = default)
    {
        var row = (await TodayAsync(doctorId, ct)).FirstOrDefault(b => b.BookingId == bookingId);
        if (row is null) return StaffResult.Fail("This booking is not on your list today.");
        if (row.Status == BookingStatus.InProgress) return StaffResult.Ok(bookingId);
        if (row.Status != BookingStatus.CheckedIn) return StaffResult.Fail("This patient is not ready to consult.");
        return await CallAsync(bookingId, ct);
    }

    private async Task<List<Booking>> TodayAsync(Guid doctorId, CancellationToken ct) =>
        (await api.GetAsync<List<Booking>>("/api/bookings/doctor/today", null, ct) ?? []).Where(b => b.DoctorId == doctorId).ToList();

    private async Task<StaffResult> CallAsync(Guid bookingId, CancellationToken ct)
    {
        try
        {
            await api.SendAsync(HttpMethod.Put, $"/api/queue/{bookingId:D}/call", new { }, null, ct);
            return StaffResult.Ok(bookingId);
        }
        catch (ApiValidationException ex) { return StaffResult.Fail(ex.Message); }
        catch (ApiForbiddenException) { return StaffResult.Fail("This patient is on another doctor's list."); }
        catch (ApiNotFoundException) { return StaffResult.Fail("This booking was not found."); }
        catch (ApiConflictException ex) { return StaffResult.Fail(ex.Message); }
    }
}
//JJQR - END
