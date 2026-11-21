//JJQR - AI (Nov 21, 2026)
// Offline-then-sync round trip against the REAL backend sync API (same env gate as SyncApiIntegrationTests):
//   CLINIC_SYNC_IT_URL (e.g. http://localhost:5077; never :5055/:3000) + CLINIC_SYNC_IT_NODE_ID/NODE_SECRET (or ADMIN_TOKEN).
// Credentials come only from the environment; nothing is written to disk by this test except the temp node DB.
using ClinicApp.Domain.Enums;
using ClinicNode.Core;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Payments;
using ClinicNode.Core.Visits;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Auth;
using ClinicNode.Sync.Wire;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests.Sync;

public class SyncApiRoundTripTests
{
    [SyncApiFact]
    public async Task SyncApiRoundTrip_offline_visit_then_sync_lands_in_the_cloud_and_a_second_sync_is_a_no_op()
    {
        var url = Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_URL")!;
        await using var host = await SyncTestHost.StartAsync(registered: false, baseUrl: url, configure: o => o.HttpHandlerFactory = null);
        var nodeId = Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_NODE_ID");
        var secret = Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_NODE_SECRET");
        if (!string.IsNullOrEmpty(secret) && Guid.TryParse(nodeId, out var nid))
            await host.Get<INodeCredentialStore>().SaveAsync(new NodeCredential(nid, secret));
        else
        {
            var reg = await host.Get<ISyncNodeRegistration>().RegisterAsync(
                Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_ADMIN_TOKEN")!, "round-trip " + DateTime.Now.ToString("HHmmss"), "test", "0.0.0");
            Assert.True(reg.Succeeded, reg.Error);
        }
        var sync = host.Get<ISyncService>();
        var endpoint = host.Get<ISyncEndpoint>();

        // 0. initial sync: pulls the cloud's catalogs/accounts so the node and cloud agree on doctors and services
        var initial = await sync.RunOnceAsync();
        Assert.True(initial.Succeeded, $"initial sync: {initial.Failure}: {initial.Error}");

        // 1. go offline (blank URL = local-only) and work
        await endpoint.SetBaseUrlAsync("");
        var session = await host.LoginAsync();
        var patient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Round", "Trip" + DateTime.Now.ToString("HHmmss"), new DateOnly(1991, 2, 3), SexType.Female));
        var officialCode = patient.PatientCode;                              // from the node's reserved MF- block
        Assert.StartsWith("MF-", officialCode);
        var bookingId = await host.Get<IVisitStarter>().StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var consults = host.Get<IConsultationService>();
        var draft = (await consults.LoadByBookingAsync(bookingId)).Draft;
        draft.ChiefComplaint = "Cough";
        draft.Subjective = "Dry cough for 3 days.";
        draft.NoMedsConfirmed = true;
        draft.FollowUpMode = FollowUpMode.None;
        foreach (var v in draft.Vitals)
            v.Value = v.FormKey == "blood_pressure" ? "120/80" : v.FormKey == "pulse_rate" ? "72" : "1";
        draft.PfDecision = "Charge";
        draft.PfAmount = 500m;
        var done = await consults.CompleteAsync(draft);
        Assert.True(done.Succeeded, done.Error);
        var consultationId = done.Draft!.ConsultationId!.Value;
        var unpaid = (await host.Get<IPaymentService>().ListUnpaidAsync()).First(p => p.BookingId == bookingId);
        var paid = await host.Get<IPaymentService>().ConfirmAsync(unpaid.PaymentId, new ConfirmPaymentRequest(PaymentMethod.Cash));
        Assert.True(paid.Succeeded, paid.Error);

        // a second patient after the reserved block is exhausted gets a temporary MF-T- code
        await using (var dbx = await host.DbAsync())
            await dbx.Database.ExecuteSqlRawAsync("UPDATE node_patient_code_blocks SET next_value = end_inclusive + 1 WHERE prefix = 'MF-';");
        var tempPatient = await host.Get<IPatientService>().CreateAsync(new NewPatientRequest("Temp", "Code" + DateTime.Now.ToString("HHmmss"), new DateOnly(1992, 4, 5), SexType.Male));
        Assert.StartsWith("MF-T-", tempPatient.PatientCode);

        var pendingOffline = await host.PendingAsync();
        Assert.True(pendingOffline >= 5, $"expected patient/booking/consultation/vital/payment ops, got {pendingOffline}");
        var offlineCycle = await sync.RunOnceAsync();
        Assert.False(offlineCycle.Succeeded);                               // local-only: nothing leaves the box
        Assert.Equal(pendingOffline, await host.PendingAsync());

        // 2. back online (runtime URL change, no restart) and sync
        await endpoint.SetBaseUrlAsync(url);
        var online = await sync.RunOnceAsync();
        var conflicts = (await host.Get<ISyncStatus>().GetOpenConflictsAsync())
            .Select(c => $"{c.Kind}/{c.ReasonCode}/{c.Aggregate}: {c.Message}").ToList();
        Assert.True(online.Succeeded, $"{online.Failure}: {online.Error}");
        var detail = $"pending={await host.PendingAsync()} pushed={online.Pushed} acked={online.Acked} retry={online.RetryLater} review=[{string.Join(" | ", conflicts)}]";
        Assert.True(await host.PendingAsync() == 0, detail);
        // informational review entries are fine (code replaced / cloud assigned a code); failures are not
        Assert.True(conflicts.All(c => c.StartsWith("patient_code_replaced") || c.StartsWith("merged/patient_code_taken")
            || c.StartsWith("merged//booking") || c.StartsWith("merged/queue_number_reassigned")), detail);

        // 3. the temporary MF-T- code was replaced by the cloud's real code; payment landed as Paid
        await using (var db = await host.DbAsync())
        {
            var temp = (await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == tempPatient.PatientId)).PatientCode;
            Assert.False(temp.StartsWith("MF-T-", StringComparison.OrdinalIgnoreCase), $"patient code still temporary: {temp}; {detail}");
            var official = (await db.Patients.AsNoTracking().SingleAsync(p => p.PatientId == patient.PatientId)).PatientCode;
            Assert.False(official.StartsWith("MF-T-", StringComparison.OrdinalIgnoreCase), detail);
            Assert.Equal(PaymentStatus.Paid, (await db.Payments.AsNoTracking().SingleAsync(p => p.BookingId == bookingId)).Status);
        }

        // 3b. v1.1: edit allergies and amend the finished visit (online or offline, same path), then sync again
        await host.Get<IPatientService>().UpdateAllergiesAsync(patient.PatientId, new[] { new AllergyInput("Penicillin", "Rash", "Severe") });
        var amendDraft = (await consults.LoadByBookingAsync(bookingId)).Draft;
        amendDraft.ChiefComplaint = "Cough and mild fever (amended)";
        amendDraft.NoMedsConfirmed = true;
        amendDraft.FollowUpMode = FollowUpMode.None;
        var amended = await consults.AmendAsync(amendDraft);
        Assert.True(amended.Succeeded, amended.Error);
        var v11 = await sync.RunOnceAsync();
        var v11Review = (await host.Get<ISyncStatus>().GetOpenConflictsAsync())
            .Where(c => c.Aggregate is "consultation" || (c.Aggregate == "patient" && c.ReasonCode == "allergy_union"))
            .Select(c => $"{c.Kind}/{c.ReasonCode}/{c.Aggregate}: {c.Message}").ToList();
        Assert.True(v11.Succeeded, $"{v11.Failure}: {v11.Error}");
        Assert.True(await host.PendingAsync() == 0 && v11Review.Count == 0, $"pending={await host.PendingAsync()} review=[{string.Join(" | ", v11Review)}]");

        // 4. replay: a second sync pushes nothing and finds no new conflicts
        var again = await sync.RunOnceAsync();
        Assert.True(again.Succeeded, $"{again.Failure}: {again.Error}");
        Assert.Equal(0, again.Pushed);
        Assert.Equal(0, await host.PendingAsync());
        _ = consultationId;
    }
}
//JJQR - END
