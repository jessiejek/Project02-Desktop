//JJQR - AI (21 Nov 2026) - Api implementation of IStaffPaymentsService, the same calls the web /staff/payments and /staff/bookings/[id] make:
//   GET  /api/queue                                        (today: Completed with amount due > 0 — the authoritative list for today)
//   GET  /api/bookings/staff/for-payment?pageSize=500      (supplement: strictly EARLIER days, Completed, still owing; failures ignored)
//   GET  /api/payments/waived-awaiting-confirmation        (doctor-waived visits: "Waived by doctor", ₱0, "Confirm waiver"; failures ignored)
//   GET  /api/payments/booking/{bookingId}                 (payment id + the amount the doctor set)
//   POST /api/payments/{id}/confirm { payment_method, amount_received, reference_number, or_number, confirm_notes }   (EXACT amount only)
//   POST /api/payments/{id}/confirm-waiver {}
//   POST /api/payments/{id}/waive { reason }               (staff: only when the doctor already decided Waive — enforced by the server)
//   GET  /api/bookings/{id}                                (receipt)   ·   GET /api/admin/payment-methods
// OR numbers stay PER COMPUTER (OR-<node>-<yyyymmdd>-<seq>): they are allocated from this computer's node id and local counter, the same
// allocator the offline path uses, so the online and offline paths never hand out two different series.
using ClinicApp.Domain;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Api.Http;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;
using static ClinicNode.Api.Services.ApiStaff;

namespace ClinicNode.Api.Services;

public sealed class ApiStaffPaymentsService(IApiClient api, IDbContextFactory<ClinicAppDbContext> factory) : IStaffPaymentsService
{
    internal sealed record PaymentDto(Guid PaymentId, Guid BookingId, decimal Amount, PaymentStatus Status);
    internal sealed record WaivedDto(Guid BookingId, Guid PaymentId, string? PatientName, string? PatientCode, string? QueueNumber, DateOnly AppointmentDate, string? WaivedReason);
    internal sealed record MethodDto(string? PaymentMethod);

    public async Task<CollectBoard> GetCollectBoardAsync(CancellationToken ct = default)
    {
        var board = await api.GetAsync<QueueBoardDto>("/api/queue", null, ct);
        var today = board?.Date ?? ClinicClock.Today;
        var rows = (board?.Items ?? [])
            .Where(e => e.Status == BookingStatus.Completed && (e.AmountDue ?? 0) > 0)
            .Select(e => new CollectRow(e.BookingId, Guid.Empty, string.IsNullOrWhiteSpace(e.PatientName) ? "—" : e.PatientName!, e.PatientCode ?? "",
                today, e.QueueNumber, e.AmountDue ?? 0, null))
            .ToList();

        var seen = rows.Select(r => r.BookingId).ToHashSet();
        var forPayment = await Optional(() => api.GetAsync<Paged<BookingDto>>("/api/bookings/staff/for-payment", Q(("pageSize", "500")), ct), null);
        rows.AddRange((forPayment?.Items ?? [])
            .Where(b => !seen.Contains(b.BookingId) && b.Status == BookingStatus.Completed && b.AppointmentDate < ClinicClock.Today && (b.AmountDue ?? 0) > 0)
            .Select(b => new CollectRow(b.BookingId, b.Payments?.PaymentId ?? Guid.Empty, PatientName(b.Patients) is { Length: > 0 } n ? n : "—",
                b.Patients?.PatientCode ?? "", b.AppointmentDate, b.QueueNumber, b.AmountDue ?? 0, null)));

        var known = rows.Select(r => r.BookingId).ToHashSet();
        var waived = await Optional(() => api.GetAsync<List<WaivedDto>>("/api/payments/waived-awaiting-confirmation", null, ct), null);
        rows.AddRange((waived ?? [])
            .Where(w => !known.Contains(w.BookingId))
            .Select(w => new CollectRow(w.BookingId, w.PaymentId, string.IsNullOrWhiteSpace(w.PatientName) ? "—" : w.PatientName!.Trim(), w.PatientCode ?? "",
                w.AppointmentDate, w.QueueNumber, 0, w.WaivedReason ?? "")));

        var waiting = rows.Count(r => !r.IsWaived);
        return new CollectBoard(waiting, rows.Sum(r => r.AmountDue), rows.Count - waiting, rows);
    }

    //JJQR - AI (21 Nov 2026) - money path: confirm / waive / confirm-waiver / typed-OR retry. A 401 (session expired after the API was reachable) must
    // NOT reach OnlineFirstProxy as an exception (it would fall back to the LOCAL service and save the payment only on this computer): it becomes
    // StaffResult.Fail("sign in again"), nothing saved. Reusable by admin screens: RecordWithOrAsync / WaiveWithOrAsync / ApiStaffPaymentsService.Create.
    public const string SessionExpiredMessage = "Your session has expired. Please sign in again. Nothing was saved.";
    public const int OrNumberMaxLength = 100;

    /// <summary>Online-only instance for the typed-OR retry (never goes through the local fallback).</summary>
    public static ApiStaffPaymentsService Create(IServiceProvider sp) =>
        new((IApiClient)sp.GetService(typeof(IApiClient))!, (IDbContextFactory<ClinicAppDbContext>)sp.GetService(typeof(IDbContextFactory<ClinicAppDbContext>))!);

    /// <summary>Trimmed OR, or the error to show (required, max 100).</summary>
    public static (string? Or, string? Error) CheckOrNumber(string? text)
    {
        var t = text?.Trim() ?? "";
        if (t.Length == 0) return (null, "Enter the OR number.");
        if (t.Length > OrNumberMaxLength) return (null, $"The OR number must be {OrNumberMaxLength} characters or fewer.");
        return (t, null);
    }

    private static async Task<StaffResult> MoneyAsync(Func<Task<StaffResult>> call)
    {
        try { return await call(); }
        catch (ApiUnauthorizedException)
        {
            var r = StaffResult.Fail(SessionExpiredMessage);
            PaymentOutcome.MarkSessionExpired(r);
            return r;
        }
    }

    public Task<StaffResult> RecordAsync(RecordPaymentInput input, CancellationToken ct = default)
    {
        //JJQR - AI (Nov 22, 2026) - step 2.4: a receipt number typed from the booklet goes to the server as it is
        if (input.OrNumber is not null) return RecordWithOrAsync(input, input.OrNumber, ct);
        //JJQR - END
        return MoneyAsync(() => RecordCoreAsync(input, null, ct));
    }

    /// <summary>Retry after 409 or_number_duplicate with the OR the desk typed (trimmed, required, max 100).</summary>
    public Task<StaffResult> RecordWithOrAsync(RecordPaymentInput input, string? orNumber, CancellationToken ct = default)
    {
        var (or, error) = CheckOrNumber(orNumber);
        return or is null ? Task.FromResult(StaffResult.Fail(error!)) : MoneyAsync(() => RecordCoreAsync(input, or, ct));
    }

    private async Task<StaffResult> RecordCoreAsync(RecordPaymentInput input, string? typedOr, CancellationToken ct)
    {
    //JJQR - END
        PaymentDto? payment;
        try { payment = await api.GetAsync<PaymentDto>($"/api/payments/booking/{input.BookingId:D}", null, ct); }
        catch (ApiNotFoundException) { return StaffResult.Fail("Payment not found."); }
        if (payment is null) return StaffResult.Fail("Payment not found.");

        // Exact amount only: no change handed back, no overpayment (the server checks the same rule).
        var amount = StaffRules.CheckAmountReceived(input.AmountReceivedText, payment.Amount);
        if (!amount.Ok) return StaffResult.Fail(amount.Error!);

        string orNumber;
        //JJQR - AI (21 Nov 2026) - typed OR (retry after a clash) is used as-is; otherwise the existing allocation (unchanged)
        if (typedOr is not null) orNumber = typedOr;
        else
        //JJQR - END
        try { orNumber = await AllocateOrNumberAsync(payment.PaymentId, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return StaffResult.Fail("Could not assign an official receipt number. Please try again.");
        }

        //JJQR - AI (21 Nov 2026) - 409 {error:"or_number_duplicate", message, or_number}: the server saved NOTHING. Show a clear error; no local fallback
        // (OnlineFirstProxy only falls back when offline; a 409 is a server answer). OR generation itself is unchanged (backend question).
        try
        {
            await api.SendAsync(HttpMethod.Post, $"/api/payments/{payment.PaymentId:D}/confirm", new
            {
                payment_method = input.Method.ToString(),
                amount_received = amount.Value,
                reference_number = string.IsNullOrWhiteSpace(input.ReferenceNumber) ? null : input.ReferenceNumber.Trim(),
                or_number = orNumber,
                confirm_notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
            }, null, ct);
            return StaffResult.Ok(payment.PaymentId);
        }
        catch (ApiConflictException ex) when (OrDuplicate(ex.Body) is { } dup)
        {
            var r = StaffResult.Fail(OrDuplicateMessage(dup.OrNumber ?? orNumber));
            PaymentOutcome.MarkOrClash(r, payment.PaymentId, dup.OrNumber ?? orNumber); //JJQR - AI (21 Nov 2026) - the screen shows an OR field prefilled with it //JJQR - END
            //JJQR - AI (21 Nov 2026) - TASK 12: AllocateOrNumberAsync reserved this OR on the LOCAL payment row (SaveChanges) before the POST. The server
            // saved nothing, so the clashing OR must not stay on this computer (a later local confirm reuses a stored OR and the row would push it).
            if (typedOr is null) await ForgetClashingOrAsync(payment.PaymentId, orNumber, dup.OrNumber, ct);
            //JJQR - END
            return r;
        }
        catch (Exception ex) when (ApiStaff.IsServerAnswer(ex))
        {
            return StaffResult.Fail(string.IsNullOrWhiteSpace(ex.Message) ? "The payment was not recorded. Please try again." : ex.Message);
        }
        //JJQR - END
    }

    //JJQR - AI (21 Nov 2026) - TASK 12: clear an OR this computer reserved on its local payment row when the server answered 409 or_number_duplicate.
    // Only an unpaid local row whose OR equals the clashing number is touched (a settled local payment is never changed). No new OR is invented.
    private async Task ForgetClashingOrAsync(Guid paymentId, string sentOr, string? serverOr, CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var local = await db.Payments.SingleOrDefaultAsync(p => p.PaymentId == paymentId, ct);
            if (local is null || local.Status != PaymentStatus.Unpaid || string.IsNullOrEmpty(local.OrNumber)) return;
            if (!string.Equals(local.OrNumber, sentOr, StringComparison.Ordinal) && !string.Equals(local.OrNumber, serverOr, StringComparison.Ordinal)) return;
            local.OrNumber = null;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* best effort; the desk still types a new OR */ }
    }
    //JJQR - END

    //JJQR - AI (21 Nov 2026) - or_number_duplicate body reader (contract: {error:"or_number_duplicate", message, or_number})
    internal sealed record OrDuplicateBody(string? OrNumber, string? Message);

    internal static OrDuplicateBody? OrDuplicate(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(body) is not System.Text.Json.Nodes.JsonObject o) return null;
            if (!string.Equals(o["error"]?.GetValue<string>(), "or_number_duplicate", StringComparison.OrdinalIgnoreCase)) return null;
            return new OrDuplicateBody(o["or_number"]?.GetValue<string>(), o["message"]?.GetValue<string>());
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or FormatException) { return null; }
    }

    internal static string OrDuplicateMessage(string? orNumber) =>
        $"OR number {orNumber ?? ""} is already used on another payment. Nothing was saved. Use a different OR number, or ask an admin to check the OR series.".Replace("OR number  is", "This OR number is");
    //JJQR - END

    /// <summary>This computer's next OR number (OR-&lt;node&gt;-&lt;yyyymmdd&gt;-&lt;seq&gt;). Reuses / reserves it on the local copy of the payment when that row is
    /// already here (so a retry gets the same number); otherwise takes the next free sequence of this computer's series for today.</summary>
    async Task<string> AllocateOrNumberAsync(Guid paymentId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var local = await db.Payments.SingleOrDefaultAsync(p => p.PaymentId == paymentId, ct);
        if (local is not null) return await StaffPaymentsService.AllocateOrNumberAsync(db, local, ct);

        var identity = await SyncOutbox.GetOrCreateIdentityAsync(db, ct);
        var prefix = StaffRules.NodePrefixFrom(identity.NodeId);
        var today = ClinicClock.Today;
        var dayPrefix = StaffRules.OrDayPrefix(prefix, today);
        var existing = await db.Payments.AsNoTracking()
            .Where(p => p.OrNumber != null && p.OrNumber.StartsWith(dayPrefix))
            .Select(p => p.OrNumber!)
            .ToListAsync(ct);
        var next = 1;
        foreach (var n in existing)
            if (StaffRules.SeqOf(n, dayPrefix) is { } seq && seq >= next) next = seq + 1;
        return StaffRules.FormatOrNumber(prefix, today, next);
    }

    public Task<StaffResult> ConfirmWaiverAsync(Guid paymentId, CancellationToken ct = default) =>
        MoneyAsync(() => Write(() => api.SendAsync(HttpMethod.Post, $"/api/payments/{paymentId:D}/confirm-waiver", new { }, null, ct),
            paymentId, "Could not confirm the waiver. Please try again.")); //JJQR - AI (21 Nov 2026) - MoneyAsync: 401 never falls back //JJQR - END

    //JJQR - AI (21 Nov 2026) - waive: same 401 rule; optional or_number (body {reason, or_number?}); 409 or_number_duplicate -> OR field on the screen
    public Task<StaffResult> WaiveAsync(Guid bookingId, string? reason, CancellationToken ct = default) =>
        MoneyAsync(() => WaiveCoreAsync(bookingId, reason, null, ct));

    /// <summary>Waive with an OR the desk typed (retry after 409 or_number_duplicate): trimmed, required, max 100.</summary>
    public Task<StaffResult> WaiveWithOrAsync(Guid bookingId, string? reason, string? orNumber, CancellationToken ct = default)
    {
        var (or, error) = CheckOrNumber(orNumber);
        return or is null ? Task.FromResult(StaffResult.Fail(error!)) : MoneyAsync(() => WaiveCoreAsync(bookingId, reason, or, ct));
    }

    private async Task<StaffResult> WaiveCoreAsync(Guid bookingId, string? reason, string? orNumber, CancellationToken ct)
    {
    //JJQR - END
        var trimmed = reason?.Trim() ?? "";
        if (trimmed.Length < StaffRules.MinDialogReasonLength) return StaffResult.Fail("At least 5 characters.");
        PaymentDto? payment;
        try { payment = await api.GetAsync<PaymentDto>($"/api/payments/booking/{bookingId:D}", null, ct); }
        catch (ApiNotFoundException) { return StaffResult.Fail("Payment not found."); }
        if (payment is null) return StaffResult.Fail("Payment not found.");
        //JJQR - AI (21 Nov 2026) - or_number only when typed (the old body {reason} is unchanged otherwise)
        try
        {
            if (orNumber is null) await api.SendAsync(HttpMethod.Post, $"/api/payments/{payment.PaymentId:D}/waive", new { reason = trimmed }, null, ct);
            else await api.SendAsync(HttpMethod.Post, $"/api/payments/{payment.PaymentId:D}/waive", new { reason = trimmed, or_number = orNumber }, null, ct);
            return StaffResult.Ok(payment.PaymentId);
        }
        catch (ApiConflictException ex) when (OrDuplicate(ex.Body) is { } dup)
        {
            var r = StaffResult.Fail(OrDuplicateMessage(dup.OrNumber ?? orNumber));
            PaymentOutcome.MarkOrClash(r, payment.PaymentId, dup.OrNumber ?? orNumber ?? "");
            return r;
        }
        catch (Exception ex) when (IsServerAnswer(ex))
        {
            return StaffResult.Fail(string.IsNullOrWhiteSpace(ex.Message) ? "Could not waive the fee." : ex.Message);
        }
        //JJQR - END
    }

    public async Task<ReceiptView?> GetReceiptAsync(Guid bookingId, CancellationToken ct = default)
    {
        BookingDto? b;
        try { b = await api.GetAsync<BookingDto>($"/api/bookings/{bookingId:D}", null, ct); }
        catch (ApiNotFoundException) { return null; }
        if (b?.Payments is not { } p) return null;
        var status = p.Status ?? PaymentStatus.Unpaid;
        return new ReceiptView(b.BookingId, PatientName(b.Patients) is { Length: > 0 } n ? n : "—", b.Doctors?.StaffAccounts?.FullName ?? "", b.AppointmentDate,
            p.AmountReceived ?? p.Amount ?? 0, status, p.PaymentMethod, p.ReferenceNumber,
            StaffRules.DisplayOrNumber(p.OrNumber, b.BookingId, status is PaymentStatus.Paid or PaymentStatus.Waived),
            p.WaivedReason, p.ConfirmedAt ?? DateTimeOffset.UtcNow, PendingSync: false);
    }

    public async Task<IReadOnlyList<PaymentMethod>> ListMethodsAsync(CancellationToken ct = default)
    {
        var rows = await Optional(() => api.GetAsync<List<MethodDto>>("/api/admin/payment-methods", null, ct), null);
        var list = (rows ?? [])
            .Select(r => Enum.TryParse<PaymentMethod>(r.PaymentMethod, true, out var m) ? (PaymentMethod?)m : null)
            .Where(m => m is not null).Select(m => m!.Value).Distinct().ToList();
        return list.Count > 0 ? list : Enum.GetValues<PaymentMethod>().ToList();
    }
}
//JJQR - END

//JJQR - AI (21 Nov 2026) - side data on a payment StaffResult (Core's StaffResult has no slot for it; the proxy returns the same instance):
// the clashing OR (409 or_number_duplicate) and "session expired". Screens read it with TryGetOrClash / IsSessionExpired.
public sealed record PaymentOrClash(Guid PaymentId, string OrNumber);

public static class PaymentOutcome
{
    private sealed class Box { public PaymentOrClash? Clash; public bool SessionExpired; }
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StaffResult, Box> Data = new();

    internal static void MarkOrClash(StaffResult r, Guid paymentId, string orNumber) => Data.GetOrCreateValue(r).Clash = new PaymentOrClash(paymentId, orNumber);
    internal static void MarkSessionExpired(StaffResult r) => Data.GetOrCreateValue(r).SessionExpired = true;

    public static bool TryGetOrClash(StaffResult r, out PaymentOrClash clash)
    {
        clash = Data.TryGetValue(r, out var b) && b.Clash is { } c ? c : new PaymentOrClash(Guid.Empty, "");
        return clash.PaymentId != Guid.Empty || clash.OrNumber.Length > 0;
    }

    public static bool IsSessionExpired(StaffResult r) => Data.TryGetValue(r, out var b) && b.SessionExpired;
}
//JJQR - END
