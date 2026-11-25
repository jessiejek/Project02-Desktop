//JJQR - AI (2026-11-25) - why: GAV-4 "Not finished on an earlier day" online: GET /api/queue/leftovers (the server scopes it: a doctor's own, staff the
// clinic's), PUT /api/queue/{id}/move-to-today (409 when it already changed → the server's sentence), PUT /api/queue/{id}/no-show (existing).
// An older server without the endpoints: the list reads as empty and Move to today says it needs the updated server; nothing crashes.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;

namespace ClinicNode.Api.Services;

public sealed class ApiLeftoverQueueService(IApiClient api) : ILeftoverQueueService
{
    internal sealed record ItemDto(Guid BookingId, DateOnly AppointmentDate, string? QueueNumber, bool IsPriority, Guid PatientId, string? PatientName,
        string? PatientCode, Guid DoctorId, BookingStatus Status, VisitType VisitType, DateTimeOffset CheckedInAt, DateTimeOffset UpdatedAt);
    internal sealed record BoardDto(List<ItemDto>? Items);
    internal sealed record MovedDto(Guid BookingId, string? QueueNumber);

    public async Task<IReadOnlyList<LeftoverRow>> ListAsync(Guid? doctorId, CancellationToken ct = default)
    {
        BoardDto? board;
        try { board = await api.GetAsync<BoardDto>("/api/queue/leftovers", null, ct); }
        catch (ApiNotFoundException) { return []; } // older clinic server: no leftovers section
        return (board?.Items ?? [])
            .Where(i => doctorId is not { } d || i.DoctorId == d)
            .Select(i => new LeftoverRow(i.BookingId, i.AppointmentDate, i.QueueNumber ?? "—", i.IsPriority, i.PatientId, i.PatientName ?? "", i.PatientCode ?? "",
                i.DoctorId, i.Status, i.VisitType, i.CheckedInAt, i.UpdatedAt))
            .ToList();
    }

    public async Task<StaffResult> MoveToTodayAsync(Guid bookingId, CancellationToken ct = default)
    {
        try
        {
            var moved = await api.SendAsync<MovedDto>(HttpMethod.Put, $"/api/queue/{bookingId:D}/move-to-today", new { }, null, ct);
            return StaffResult.Ok(moved?.BookingId ?? bookingId);
        }
        catch (ApiConflictException ex) { return StaffResult.Fail(ex.Message); }
        catch (ApiValidationException ex) { return StaffResult.Fail(ex.Message); }
        catch (ApiForbiddenException) { return StaffResult.Fail("Only this visit's own doctor or the front desk can move it."); }
        catch (ApiNotFoundException) { return StaffResult.Fail(LeftoverGroups.MoveOldServer); }
    }

    public async Task<StaffResult> MarkNoShowAsync(Guid bookingId, CancellationToken ct = default)
    {
        try
        {
            await api.SendAsync(HttpMethod.Put, $"/api/queue/{bookingId:D}/no-show", new { }, null, ct);
            return StaffResult.Ok(bookingId);
        }
        catch (ApiValidationException ex) { return StaffResult.Fail(ex.Message); }
        catch (ApiConflictException ex) { return StaffResult.Fail(ex.Message); }
        catch (ApiForbiddenException) { return StaffResult.Fail("This patient is on another doctor's list."); }
        catch (ApiNotFoundException) { return StaffResult.Fail("This booking was not found."); }
    }
}
//JJQR - END
