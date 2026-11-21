//JJQR - AI (21 Nov 2026) - allen3 gap: the queue slip after an ONLINE walk-in and the receipt after an ONLINE payment are printed from the local
// SQLite copy (IPrintDocumentFactory), but the new booking / payment row only arrives with the next sync pull. This write-through copies the
// server's rows into SQLite right away, using the same apply rules the sync pull uses (AggregateDocuments, ServerResult = the server already decided):
//   GET /api/bookings/{id}            → booking  (+ the patient from GET /api/patients/{id} when this computer does not have it yet)
//   GET /api/payments/booking/{id}    → payment  (receipts only)
//JJQR - AI (21 Nov 2026) - + the doctor when this computer does not have it yet (a doctor added online after the last pull made the slip/receipt fail):
//   GET /api/doctors/{doctor_id} (staff read, includes its staff_accounts embed) → "staff_account" then "doctor" (cloud-owned aggregates, same as the sync pull;
//   Doctors.DoctorId = StaffAccounts.StaffId, FK). Never the user / password rows: those only come through the sync pull.
//JJQR - END
// Offline / offline sign-in → nothing to do (the write already went to SQLite). A row with unpushed local changes is never overwritten.
// Every failure is swallowed: printing then behaves exactly as before. Nothing is logged (patient data).
using System.Text.Json.Nodes;
using ClinicApp.Domain.Entities;
using ClinicApp.Infrastructure;
using ClinicNode.Api.Http;
using ClinicNode.Sync;
using ClinicNode.Sync.Local;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.App.ViewModels.Staff;

public static class StaffLocalWriteThrough
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Copies the booking (and with <paramref name="withPayment"/> its payment) from the server into SQLite. Safe to call before every print.</summary>
    public static async Task EnsureBookingAsync(IServiceProvider sp, Guid bookingId, bool withPayment, CancellationToken ct = default)
    {
        var api = sp.GetService<IApiClient>();
        var tokens = sp.GetService<IApiTokenStore>();
        var connectivity = sp.GetService<IApiConnectivity>();
        var factory = sp.GetService<IDbContextFactory<ClinicAppDbContext>>();
        if (api is null || tokens is null || connectivity is null || factory is null) return;
        if (tokens.AccessToken is not { Length: > 0 } || !connectivity.IsOnline) return; // offline: SQLite already has it

        await Gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8)); // never hold up the print for long

            var booking = await api.GetAsync<JsonObject>($"/api/bookings/{bookingId:D}", null, timeout.Token);
            if (booking is null) return;
            JsonObject? payment = null;
            if (withPayment)
            {
                try { payment = await api.GetAsync<JsonObject>($"/api/payments/booking/{bookingId:D}", null, timeout.Token); }
                catch (ApiNotFoundException) { }
            }

            await using var db = await factory.CreateDbContextAsync(timeout.Token);
            await SyncLocalStore.EnsureSchemaAsync(db, timeout.Token);

            //JJQR - AI (21 Nov 2026) - parents first: the doctor (staff_account → doctor). Its own try: a refused doctor read still lets the booking try.
            try
            {
            if (Guid.TryParse(Str(booking, "doctor_id", "doctorId"), out var doctorId)
                && !await db.Set<ClinicApp.Domain.Entities.Doctor>().AsNoTracking().AnyAsync(d => d.DoctorId == doctorId, timeout.Token))
            {
                var doctor = await api.GetAsync<JsonObject>($"/api/doctors/{doctorId:D}", null, timeout.Token);
                if (doctor is not null)
                {
                    var staff = doctor["staff_accounts"] as JsonObject ?? doctor["staff_account"] as JsonObject ?? doctor["staffAccount"] as JsonObject;
                    if (staff is null)
                    {
                        try { staff = await api.GetAsync<JsonObject>($"/api/staff-accounts/{doctorId:D}", null, timeout.Token); }
                        catch (ApiNotFoundException) { }
                    }
                    if (staff is not null && !await db.Set<ClinicApp.Domain.Entities.StaffAccount>().AsNoTracking().AnyAsync(x => x.StaffId == doctorId, timeout.Token))
                        await ApplyAsync(db, Aggregates.StaffAccount, doctorId.ToString(), Strip(staff), timeout.Token);
                    if (await db.Set<ClinicApp.Domain.Entities.StaffAccount>().AsNoTracking().AnyAsync(x => x.StaffId == doctorId, timeout.Token)) // FK: doctor needs its staff row
                        await ApplyAsync(db, "doctor", doctorId.ToString(), Strip(doctor), timeout.Token);
                }
            }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ChangeTracker.Clear(); // a failed insert must not ride along with the booking save
            }
            //JJQR - END

            // parent first: the patient (a patient registered online a minute ago is not here yet)
            if (Guid.TryParse(Str(booking, "patient_id", "patientId"), out var patientId)
                && !await db.Set<ClinicApp.Domain.Entities.Patient>().AsNoTracking().AnyAsync(p => p.PatientId == patientId, timeout.Token))
            {
                var patient = await api.GetAsync<JsonObject>($"/api/patients/{patientId:D}", null, timeout.Token);
                if (patient is not null)
                    await ApplyAsync(db, Aggregates.Patient, patientId.ToString(), Strip(patient), timeout.Token);
            }

            await ApplyAsync(db, Aggregates.Booking, bookingId.ToString(), Strip(booking), timeout.Token);

            var pay = payment ?? (withPayment ? booking["payments"] as JsonObject ?? booking["payment"] as JsonObject : null);
            if (pay is not null)
            {
                pay = Strip(pay);
                if (!pay.ContainsKey("booking_id") && !pay.ContainsKey("bookingId")) pay["booking_id"] = bookingId.ToString("D");
                var paymentId = Str(pay, "payment_id", "paymentId");
                if (!string.IsNullOrEmpty(paymentId)) await ApplyAsync(db, Aggregates.Payment, paymentId, pay, timeout.Token);
            }
        }
        catch (Exception)
        {
            // offline mid-way, server refusal, FK miss (doctor not synced yet) …: the next sync pull brings the rows; the print path reports its own error
        }
        finally { Gate.Release(); }
    }

    private static async Task ApplyAsync(ClinicAppDbContext db, string aggregate, string id, JsonObject data, CancellationToken ct)
    {
        if (await SyncLocalStore.HasPendingAsync(db, aggregate, id, ct)) return; // never overwrite unpushed local work
        await AggregateDocuments.ApplyAsync(db, new PulledChange(aggregate, id, false, data), ApplyMode.ServerResult, ct);
    }

    /// <summary>REST rows carry embeds (patients / doctors / payments / staff_accounts): keep only the row's own scalar columns.</summary>
    private static JsonObject Strip(JsonObject row)
    {
        var copy = new JsonObject();
        foreach (var (k, v) in row)
            if (v is not JsonObject and not JsonArray) copy[k] = v?.DeepClone();
        return copy;
    }

    private static string? Str(JsonObject o, params string[] keys)
    {
        foreach (var k in keys)
            if (o[k] is JsonValue v && v.TryGetValue<string>(out var s)) return s;
        return null;
    }
}
//JJQR - END
