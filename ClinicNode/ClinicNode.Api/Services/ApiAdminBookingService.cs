//JJQR - AI (21 Nov 2026) - Api implementation of IAdminBookingService, using the SAME endpoints as the web /admin/bookings, /admin/bookings/[id] and lib/data/payments.ts:
//   GET /api/bookings/staff/all?q&pageSize (search) · GET /api/bookings?doctorId&status&date&from&to · GET /api/bookings/{id}
//   PUT /api/bookings/{id}/status {status,reason} · PUT /api/bookings/{id}/reschedule {appointment_date,reason} · GET /api/bookings/closed-dates?days
//   POST /api/bookings · POST /api/bookings/walk-in
//   GET /api/payments/booking/{bookingId} → POST /api/payments/{paymentId}/confirm | waive | refund
// OR numbers: the server only stores or_number, so confirm sends this computer's next per-computer number (OR-XXXX-YYYYMMDD-NNNN, IAdminOrNumberAllocator).
// The web's "Confirm payment" only flips the booking to Confirmed and invents OR-<booking id 8> client-side; here the payment really is recorded as paid
// (exactly the amount due, as the API requires) and then the booking is confirmed, same as the local AdminBookingService.
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core.Admin;

namespace ClinicNode.Api.Services;

//JJQR - AI (21 Nov 2026) - OR uniqueness: optional prompt for a replacement OR number after a 409 or_number_duplicate
public sealed class ApiAdminBookingService(IApiClient api, IAdminOrNumberAllocator orNumbers, IAdminOrNumberPrompt? orPrompt = null) : IAdminBookingService
//JJQR - END
{
    //JJQR - AI (21 Nov 2026) - 409 { error: "or_number_duplicate", message, or_number } → ask for another OR number (max 100) and retry the same call.
    // Null result = sent; otherwise the failure to show. Allocation is unchanged: the first try always uses the allocated number.
    static bool IsOrDuplicate(ApiConflictException ex, out string message, out string? rejected)
    {
        message = ex.Message; rejected = null;
        if (string.IsNullOrWhiteSpace(ex.Body)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(ex.Body);
            var root = doc.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            if (!root.TryGetProperty("error", out var e) || e.GetString() != AdminOrNumberRules.DuplicateError) return false;
            if (root.TryGetProperty("message", out var m) && m.GetString() is { Length: > 0 } text) message = text;
            if (root.TryGetProperty("or_number", out var o)) rejected = o.GetString();
            return true;
        }
        catch (System.Text.Json.JsonException) { return false; }
    }

    async Task<string?> SendWithOrRetryAsync(Guid bookingId, string firstOr, Func<string, Task> send, CancellationToken ct)
    {
        var or = firstOr;
        var released = false;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try { await send(or); return null; }
            catch (ApiConflictException ex) when (IsOrDuplicate(ex, out var message, out var rejected))
            {
                // nothing is saved locally on a clash: undo the allocator's local copy of the refused number (once)
                if (!released) { released = true; await orNumbers.ReleaseAsync(bookingId, firstOr, ct); }
                if (orPrompt is null) return message;
                var problem = message;
                while (true)
                {
                    // the field opens prefilled with the clashing number; required, trimmed, max 100
                    var prefill = string.IsNullOrWhiteSpace(rejected) ? or : rejected!;
                    var typed = await orPrompt.AskForAnotherAsync(prefill, problem, ct);
                    if (typed is null) return message;
                    var bad = AdminOrNumberRules.Problem(typed);
                    if (bad is null) { or = typed.Trim(); break; }
                    problem = bad;
                    rejected = typed.Trim().Length > AdminOrNumberRules.MaxLength ? typed.Trim()[..AdminOrNumberRules.MaxLength] : typed.Trim();
                }
            }
        }
        return "The OR number could not be saved. Try again.";
    }
    //JJQR - END

    public async Task<AdminPage<AdminBookingListItem>> ListAsync(string? q, Guid? doctorId, BookingStatus? status, DateOnly? date, DateOnly? from, DateOnly? to,
        int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 500);
        List<Booking> rows;
        if (!string.IsNullOrWhiteSpace(q))
        {
            // web BookingsList: server search on patient name / code / queue number, then the other filters client-side
            var res = await api.GetAsync<ApiPaged<Booking>>("/api/bookings/staff/all", ApiAdmin.Q(("pageSize", 500), ("q", q)), ct);
            rows = res?.Items ?? [];
            if (doctorId is { } d) rows = rows.Where(b => b.DoctorId == d).ToList();
            if (status is { } s) rows = rows.Where(b => b.Status == s).ToList();
            if (date is { } dt) rows = rows.Where(b => b.AppointmentDate == dt).ToList();
            if (from is { } f) rows = rows.Where(b => b.AppointmentDate >= f).ToList();
            if (to is { } t) rows = rows.Where(b => b.AppointmentDate <= t).ToList();
        }
        else
        {
            rows = await api.GetAsync<List<Booking>>("/api/bookings",
                ApiAdmin.Q(("doctorId", doctorId), ("status", status), ("date", date), ("from", from), ("to", to)), ct) ?? [];
        }
        var items = rows.Select(ApiAdmin.ToListItem).ToList();
        return new AdminPage<AdminBookingListItem>(items.Skip((page - 1) * pageSize).Take(pageSize).ToList(), items.Count, page, pageSize);
    }

    public async Task<IReadOnlyList<AdminBookingListItem>> ListWeekAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await api.GetAsync<List<Booking>>("/api/bookings", ApiAdmin.Q(("from", from), ("to", to)), ct) ?? [];
        return rows.Where(b => b.Status is not (BookingStatus.Cancelled or BookingStatus.Expired))
            .OrderBy(b => b.SlotStartTime)
            .Select(ApiAdmin.ToListItem).ToList();
    }

    public async Task<AdminResult<AdminBookingDetail>> GetAsync(Guid bookingId, CancellationToken ct = default)
    {
        Booking? b;
        try { b = await api.GetAsync<Booking>($"/api/bookings/{bookingId:D}", null, ct); }
        catch (ApiNotFoundException) { return AdminResult<AdminBookingDetail>.Fail("Booking not found."); }
        if (b is null) return AdminResult<AdminBookingDetail>.Fail("Booking not found.");
        var services = (b.BookingServices ?? []).Select(s => s.Service?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();
        return AdminResult<AdminBookingDetail>.Ok(new AdminBookingDetail(
            ApiAdmin.ToListItem(b),
            b.Patient?.ContactNumber,
            b.Patient?.Email,
            b.Doctor?.Specialization ?? "",
            b.PaymentMode,
            b.AmountDue,
            b.TotalFee,
            b.Payment?.OrNumber,
            b.CancellationReason,
            b.Notes,
            b.VisitType,
            b.DiscountCategory,
            services,
            b.Payment?.PaymentId));
    }

    public Task<AdminResult<AdminBookingListItem>> CreateAsync(CreateAdminBookingRequest request, CancellationToken ct = default) =>
        CreateCoreAsync("/api/bookings", request, ct);

    public Task<AdminResult<AdminBookingListItem>> CreateWalkInAsync(CreateAdminBookingRequest request, CancellationToken ct = default) =>
        CreateCoreAsync("/api/bookings/walk-in", request with { IsWalkIn = true }, ct);

    private async Task<AdminResult<AdminBookingListItem>> CreateCoreAsync(string path, CreateAdminBookingRequest r, CancellationToken ct)
    {
        //JJQR - AI (21 Nov 2026) - POST /api/bookings (and /walk-in, same request) now take optional visit_type (New | FollowUp) and
        // discount_category (Senior | PWD), approved by Jessie, added by allen1. They are sent online instead of falling back to a local create.
        // Absent (null) = the server's unchanged default, so a plain New / no-discount booking sends nulls.
        //JJQR - END
        try
        {
            var bookingId = Guid.NewGuid();
            var created = await api.SendAsync<Booking>(HttpMethod.Post, path, new
            {
                patient_id = r.PatientId,
                doctor_id = r.DoctorId,
                appointment_date = r.AppointmentDate,
                slot_start_time = r.SlotStartTime,
                slot_end_time = r.SlotEndTime,
                service_ids = r.ServiceIds,
                payment_mode = r.PaymentMode,
                is_walk_in = r.IsWalkIn,
                notes = ApiAdmin.Blank(r.Notes),
                booking_id = bookingId,
                payment_id = Guid.NewGuid(),
                //JJQR - AI (21 Nov 2026)
                visit_type = r.VisitType == VisitType.New ? (VisitType?)null : r.VisitType,
                discount_category = ApiAdmin.Blank(r.DiscountCategory),
                //JJQR - END
            }, null, ct);
            var id = created?.BookingId is { } cid && cid != Guid.Empty ? cid : bookingId;
            var full = await api.GetAsync<Booking>($"/api/bookings/{id:D}", null, ct) ?? created;
            return full is null
                ? AdminResult<AdminBookingListItem>.Fail("The booking was created but could not be read back.")
                : AdminResult<AdminBookingListItem>.Ok(ApiAdmin.ToListItem(full));
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<AdminBookingListItem>.Fail(ex.Message); }
    }

    public async Task<AdminResult> UpdateStatusAsync(Guid bookingId, BookingStatus status, string? reason, CancellationToken ct = default)
    {
        try
        {
            await api.SendAsync(HttpMethod.Put, $"/api/bookings/{bookingId:D}/status", new { status, reason = ApiAdmin.Blank(reason) }, null, ct);
            return AdminResult.Ok();
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult.Fail(ex.Message); }
    }

    public async Task<AdminResult> ConfirmPaymentAsync(Guid bookingId, CancellationToken ct = default)
    {
        try
        {
            var booking = await api.GetAsync<Booking>($"/api/bookings/{bookingId:D}", null, ct);
            if (booking is null) return AdminResult.Fail("Booking not found.");
            if (booking.Status != BookingStatus.ProofSubmitted)
                return AdminResult.Fail("Only a booking with payment proof submitted can be confirmed as paid here.");
            var payment = await api.GetAsync<Payment>($"/api/payments/booking/{bookingId:D}", null, ct);
            if (payment is null) return AdminResult.Fail("Payment not found.");
            if (payment.Status == PaymentStatus.Waived) return AdminResult.Fail("The doctor waived this professional fee, so there is nothing to collect.");
            if (payment.Status == PaymentStatus.Paid) return AdminResult.Fail("This payment has already been confirmed.");

            var or = await orNumbers.NextAsync(bookingId, ct);
            //JJQR - AI (21 Nov 2026) - retry with a typed OR number when the server says the number is already used
            var orFailure = await SendWithOrRetryAsync(bookingId, or, orToSend => api.SendAsync(HttpMethod.Post, $"/api/payments/{payment.PaymentId:D}/confirm", new
            {
                payment_method = payment.PaymentMethod ?? PaymentMethod.Cash,
                amount_received = payment.Amount, // the API records exactly the amount due
                reference_number = ApiAdmin.Blank(payment.ReferenceNumber ?? booking.ProofValue),
                or_number = orToSend,
                confirm_notes = (string?)null,
            }, null, ct), ct);
            if (orFailure is not null) return AdminResult.Fail(orFailure);
            //JJQR - END
            await api.SendAsync(HttpMethod.Put, $"/api/bookings/{bookingId:D}/status", new { status = BookingStatus.Confirmed, reason = (string?)null }, null, ct);
            return AdminResult.Ok();
        }
        catch (ApiNotFoundException) { return AdminResult.Fail("Payment not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult.Fail(ex.Message); }
    }

    public async Task<AdminResult> WaivePaymentAsync(Guid bookingId, string reason, CancellationToken ct = default)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0) return AdminResult.Fail("A waiver reason is required.");
        if (trimmed.Length > 1000) return AdminResult.Fail("Waiver reason is too long (max 1000 characters).");
        try
        {
            var payment = await api.GetAsync<Payment>($"/api/payments/booking/{bookingId:D}", null, ct);
            if (payment is null) return AdminResult.Fail("Payment not found.");
            if (payment.Status == PaymentStatus.Waived) return AdminResult.Fail("This payment has already been waived.");
            //JJQR - AI (21 Nov 2026) - POST /waive now takes { reason, or_number } and refuses a used number with 409 or_number_duplicate.
            // The number reserved on this computer (same allocation as before) is sent; on a clash the user types another and the call is retried.
            var waiveOr = await orNumbers.NextAsync(bookingId, ct);
            var orFailure = await SendWithOrRetryAsync(bookingId, waiveOr, orToSend =>
                api.SendAsync(HttpMethod.Post, $"/api/payments/{payment.PaymentId:D}/waive", new { reason = trimmed, or_number = orToSend }, null, ct), ct);
            if (orFailure is not null) return AdminResult.Fail(orFailure);
            //JJQR - END
            return AdminResult.Ok();
        }
        catch (ApiNotFoundException) { return AdminResult.Fail("Payment not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult.Fail(ex.Message); }
    }

    public async Task<AdminResult> RefundPaymentAsync(Guid bookingId, decimal amount, string reason, CancellationToken ct = default)
    {
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length == 0) return AdminResult.Fail("A refund reason is required.");
        if (amount <= 0) return AdminResult.Fail("Refund amount must be greater than zero.");
        try
        {
            var payment = await api.GetAsync<Payment>($"/api/payments/booking/{bookingId:D}", null, ct);
            if (payment is null) return AdminResult.Fail("Payment not found.");
            await api.SendAsync(HttpMethod.Post, $"/api/payments/{payment.PaymentId:D}/refund", new { amount, reason = trimmed }, null, ct);
            return AdminResult.Ok();
        }
        catch (ApiNotFoundException) { return AdminResult.Fail("Payment not found."); }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult.Fail(ex.Message); }
    }

    public async Task<AdminResult<RescheduleResult>> RescheduleAsync(Guid bookingId, DateOnly appointmentDate, string? reason, CancellationToken ct = default)
    {
        try
        {
            var res = await api.SendAsync<RescheduleResult>(HttpMethod.Put, $"/api/bookings/{bookingId:D}/reschedule", new
            {
                appointment_date = appointmentDate,
                reason = ApiAdmin.Blank(reason),
                new_booking_id = Guid.NewGuid(),
                new_payment_id = Guid.NewGuid(),
            }, null, ct);
            return res is null
                ? AdminResult<RescheduleResult>.Fail("The server did not return the new booking.")
                : AdminResult<RescheduleResult>.Ok(res);
        }
        catch (ApiException ex) when (ApiAdmin.IsAnswer(ex)) { return AdminResult<RescheduleResult>.Fail(ex.Message); }
    }

    public async Task<string?> ClosedReasonAsync(DateOnly date, Guid doctorId, CancellationToken ct = default)
    {
        var days = date.DayNumber - ClinicClock.Today.DayNumber + 1;
        if (days < 1 || days > 366) throw new ApiNotSupportedException("Only the next 366 days are answered by the server.");
        var rows = await ClosedDatesAsync(doctorId, days, ct);
        return rows.FirstOrDefault(r => r.Date == date)?.Reason;
    }

    public async Task<IReadOnlyList<ClosedDateInfo>> ClosedDatesAsync(Guid? doctorId, int days = 90, CancellationToken ct = default) =>
        await api.GetAsync<List<ClosedDateInfo>>("/api/bookings/closed-dates", ApiAdmin.Q(("days", Math.Clamp(days, 1, 366))), ct) ?? [];
}
//JJQR - END
