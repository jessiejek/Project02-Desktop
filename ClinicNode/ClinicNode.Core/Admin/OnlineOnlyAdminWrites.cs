//JJQR - AI (21 Nov 2026) - admin write-path audit (allen0): offline sides for the online-first router (AdminModule) that keep only the local writes
// the clinic server accepts from a sync node, and refuse the rest with a "needs an internet connection" answer instead of saving a change that
// will never sync. Every admin write already goes to its API when online (ApiAdmin*Service); these guards only decide what may happen OFFLINE.
//
// Server sync rules (ClinicApp.Infrastructure/Sync): pushable = patient, booking, payment, consultation, vital_reading, audit_log, patient_consent;
// doctor / doctor_schedule / doctor_blocked_date only as a self-edit WITH actor_user_id; everything else (announcement, staff_account, service,
// doctor_service, clinic_*, data requests) is cloud-owned and rejected; a refunded payment pushed from a node is rejected (refund_offline_forbidden).
//
//   KEPT offline (queued, server accepts): booking create / walk-in / status / reschedule, payment confirm / waive, patient create / update.
//   REFUSED offline: refund; announcement create / update / delete; staff update / invite / revoke invite; doctor update, weekly schedule
//   (the admin's local doctor_schedule row has no actor_user_id → rejected), blocked dates and doctor services (saved locally, never queued);
//   resolving a patient data request (saved locally, never synced).
//   Untouched: SetInvitedPasswordAsync (intentionally local-only), settings / fees / services (OnlineOnlyAdminSettingsService).
// The plain local services are unchanged (their tests use them directly); they are just no longer the router's offline side for these writes.
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;

namespace ClinicNode.Core.Admin;

public static class AdminOnlineOnly
{
    public const string NeedsInternet = "This change can only be made with an internet connection to the clinic server. Nothing was saved; connect and try again.";
    public const string RefundNeedsInternet = "Refunds are recorded online by an admin only. Nothing was saved; connect and try again.";

    /// <summary>True when a failed write was refused because this computer is offline (screens show this text instead of "try again").</summary>
    public static bool IsNeedsInternet(string? error) => error is NeedsInternet or RefundNeedsInternet;

    internal static Task<AdminResult> Fail(string msg = NeedsInternet) => Task.FromResult(AdminResult.Fail(msg));
    internal static Task<AdminResult<T>> Fail<T>(string msg = NeedsInternet) => Task.FromResult(AdminResult<T>.Fail(msg));
}

/// <summary>Offline bookings: everything stays local (pushable) except refunds.</summary>
public sealed class OfflineGuardedAdminBookingService(AdminBookingService local) : IAdminBookingService
{
    public Task<AdminPage<AdminBookingListItem>> ListAsync(string? q, Guid? doctorId, BookingStatus? status, DateOnly? date, DateOnly? from, DateOnly? to, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        local.ListAsync(q, doctorId, status, date, from, to, page, pageSize, ct);
    public Task<IReadOnlyList<AdminBookingListItem>> ListWeekAsync(DateOnly from, DateOnly to, CancellationToken ct = default) => local.ListWeekAsync(from, to, ct);
    public Task<AdminResult<AdminBookingDetail>> GetAsync(Guid bookingId, CancellationToken ct = default) => local.GetAsync(bookingId, ct);
    public Task<AdminResult<AdminBookingListItem>> CreateAsync(CreateAdminBookingRequest request, CancellationToken ct = default) => local.CreateAsync(request, ct);
    public Task<AdminResult<AdminBookingListItem>> CreateWalkInAsync(CreateAdminBookingRequest request, CancellationToken ct = default) => local.CreateWalkInAsync(request, ct);
    public Task<AdminResult> UpdateStatusAsync(Guid bookingId, BookingStatus status, string? reason, CancellationToken ct = default) => local.UpdateStatusAsync(bookingId, status, reason, ct);
    public Task<AdminResult> ConfirmPaymentAsync(Guid bookingId, CancellationToken ct = default) => local.ConfirmPaymentAsync(bookingId, ct);
    public Task<AdminResult> WaivePaymentAsync(Guid bookingId, string reason, CancellationToken ct = default) => local.WaivePaymentAsync(bookingId, reason, ct);
    public Task<AdminResult> RefundPaymentAsync(Guid bookingId, decimal amount, string reason, CancellationToken ct = default) => AdminOnlineOnly.Fail(AdminOnlineOnly.RefundNeedsInternet);
    public Task<AdminResult<RescheduleResult>> RescheduleAsync(Guid bookingId, DateOnly appointmentDate, string? reason, CancellationToken ct = default) => local.RescheduleAsync(bookingId, appointmentDate, reason, ct);
    public Task<string?> ClosedReasonAsync(DateOnly date, Guid doctorId, CancellationToken ct = default) => local.ClosedReasonAsync(date, doctorId, ct);
    public Task<IReadOnlyList<ClosedDateInfo>> ClosedDatesAsync(Guid? doctorId, int days = 90, CancellationToken ct = default) => local.ClosedDatesAsync(doctorId, days, ct);
}

/// <summary>Offline doctors: reads only.</summary>
public sealed class OfflineGuardedAdminDoctorService(AdminDoctorService local) : IAdminDoctorService
{
    public Task<AdminPage<AdminDoctorListItem>> SearchAsync(string? q, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default) => local.SearchAsync(q, page, pageSize, sort, ct);
    public Task<IReadOnlyList<AdminDoctorListItem>> ListAllAsync(bool includeInactive = true, CancellationToken ct = default) => local.ListAllAsync(includeInactive, ct);
    public Task<AdminDoctorListItem?> GetAsync(Guid doctorId, CancellationToken ct = default) => local.GetAsync(doctorId, ct);
    public Task<AdminResult<AdminDoctorListItem>> UpdateAsync(Guid doctorId, DoctorEdit edit, CancellationToken ct = default) => AdminOnlineOnly.Fail<AdminDoctorListItem>();
    public Task<IReadOnlyList<ScheduleDayEdit>> GetSchedulesAsync(Guid doctorId, CancellationToken ct = default) => local.GetSchedulesAsync(doctorId, ct);
    public Task<AdminResult> UpsertSchedulesAsync(Guid doctorId, IReadOnlyList<ScheduleDayEdit> days, CancellationToken ct = default) => AdminOnlineOnly.Fail();
    public Task<IReadOnlyList<BlockedDateItem>> GetBlockedDatesAsync(Guid doctorId, CancellationToken ct = default) => local.GetBlockedDatesAsync(doctorId, ct);
    public Task<AdminResult<BlockedDateItem>> AddBlockedDateAsync(Guid doctorId, DateOnly date, string? reason, CancellationToken ct = default) => AdminOnlineOnly.Fail<BlockedDateItem>();
    public Task<AdminResult> RemoveBlockedDateAsync(Guid blockedDateId, CancellationToken ct = default) => AdminOnlineOnly.Fail();
    public Task<IReadOnlyList<Guid>> GetServiceIdsAsync(Guid doctorId, CancellationToken ct = default) => local.GetServiceIdsAsync(doctorId, ct);
    public Task<AdminResult> SetServicesAsync(Guid doctorId, IReadOnlyList<Guid> serviceIds, CancellationToken ct = default) => AdminOnlineOnly.Fail();
}

/// <summary>Offline patients: create / update stay local (pushable); resolving a data request needs the server.</summary>
public sealed class OfflineGuardedAdminPatientService(AdminPatientService local) : IAdminPatientService
{
    public Task<AdminPage<AdminPatientListItem>> SearchAsync(string? q, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default) => local.SearchAsync(q, page, pageSize, sort, ct);
    public Task<AdminResult<AdminPatientDetail>> GetAsync(Guid patientId, CancellationToken ct = default) => local.GetAsync(patientId, ct);
    public Task<AdminResult<AdminPatientListItem>> CreateAsync(PatientEdit edit, CancellationToken ct = default) => local.CreateAsync(edit, ct);
    public Task<AdminResult<AdminPatientListItem>> UpdateAsync(Guid patientId, PatientEdit edit, CancellationToken ct = default) => local.UpdateAsync(patientId, edit, ct);
    public Task<IReadOnlyList<AccessLogEntry>> GetAccessLogAsync(Guid patientId, int take = 200, CancellationToken ct = default) => local.GetAccessLogAsync(patientId, take, ct);
    public Task<IReadOnlyList<DataRequestItem>> ListDataRequestsAsync(string? status, CancellationToken ct = default) => local.ListDataRequestsAsync(status, ct);
    public Task<AdminResult<DataRequestItem>> ResolveDataRequestAsync(Guid id, string status, string? note, CancellationToken ct = default) => AdminOnlineOnly.Fail<DataRequestItem>();
    //JJQR - AI (2026-11-26) - GAV-10: the doctor's patient details edit stays local offline (pushable), like UpdateAsync
    public Task<AdminResult<PatientDetails>> GetDetailsAsync(Guid patientId, CancellationToken ct = default) => local.GetDetailsAsync(patientId, ct);
    public Task<AdminResult<PatientDetails>> UpdateDetailsAsync(Guid patientId, PatientDetails details, CancellationToken ct = default) => local.UpdateDetailsAsync(patientId, details, ct);
    //JJQR - END
}

/// <summary>Offline staff: reads only, plus the intentionally local invite-password path (untouched).</summary>
public sealed class OfflineGuardedAdminStaffService(AdminStaffService local) : IAdminStaffService
{
    public Task<AdminPage<StaffListItem>> SearchAsync(string? q, string? role, int page = 1, int pageSize = 25, string? sort = "name", CancellationToken ct = default) => local.SearchAsync(q, role, page, pageSize, sort, ct);
    public Task<StaffAccount?> GetAsync(Guid staffId, CancellationToken ct = default) => local.GetAsync(staffId, ct);
    public Task<AdminResult<StaffAccount>> UpdateAsync(Guid staffId, string fullName, string? contactNumber, StaffStatus? status, CancellationToken ct = default) => AdminOnlineOnly.Fail<StaffAccount>();
    public Task<AdminResult<StaffInviteResult>> InviteAsync(StaffInviteRequest request, CancellationToken ct = default) => AdminOnlineOnly.Fail<StaffInviteResult>();
    public Task<AdminResult> SetInvitedPasswordAsync(Guid userId, string password, CancellationToken ct = default) => local.SetInvitedPasswordAsync(userId, password, ct);
    public Task<AdminResult> RevokeInviteAsync(Guid userId, CancellationToken ct = default) => AdminOnlineOnly.Fail();
    public Task<AdminResult> SetPasswordAsync(Guid staffId, string newPassword, CancellationToken ct = default) => AdminOnlineOnly.Fail(); //JJQR - AI (Nov 23, 2026) //JJQR - END
}

/// <summary>Offline announcements: reads only (announcement is cloud-owned).</summary>
public sealed class OfflineGuardedAdminAnnouncementService(AdminAnnouncementService local) : IAdminAnnouncementService
{
    public Task<IReadOnlyList<Announcement>> ListAsync(bool activeOnly = false, CancellationToken ct = default) => local.ListAsync(activeOnly, ct);
    public Task<AdminResult<Announcement>> CreateAsync(AnnouncementEdit edit, CancellationToken ct = default) => AdminOnlineOnly.Fail<Announcement>();
    public Task<AdminResult<Announcement>> UpdateAsync(Guid id, AnnouncementEdit edit, CancellationToken ct = default) => AdminOnlineOnly.Fail<Announcement>();
    public Task<AdminResult> DeleteAsync(Guid id, CancellationToken ct = default) => AdminOnlineOnly.Fail();
}
//JJQR - END
