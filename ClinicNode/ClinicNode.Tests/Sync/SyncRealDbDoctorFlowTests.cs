//JJQR - AI (Nov 21, 2026)
// Headless doctor consultation + sync run against a COPY of the real app database (never the original).
// Skipped unless CLINIC_REALDB_DIR points at a folder holding a copied clinic-node.db (e.g. /tmp/allen2-realdb).
// Sync part also needs CLINIC_SYNC_IT_URL (+ NODE_ID/NODE_SECRET), same gate as SyncApiIntegrationTests. Secrets come only from env.
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Visits;
using ClinicNode.Data;
using ClinicNode.Sync;
using ClinicNode.Sync.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace ClinicNode.Tests.Sync;

public sealed class RealDbFactAttribute : FactAttribute
{
    public RealDbFactAttribute()
    {
        var dir = Environment.GetEnvironmentVariable("CLINIC_REALDB_DIR");
        if (string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, "clinic-node.db")))
            Skip = "Set CLINIC_REALDB_DIR to a folder with a COPY of clinic-node.db.";
        else if (dir.Contains("Library/Containers", StringComparison.OrdinalIgnoreCase))
            Skip = "Refusing to run against the app's own container: copy the database first.";
    }
}

public class SyncRealDbDoctorFlowTests
{
    private readonly ITestOutputHelper _out;
    public SyncRealDbDoctorFlowTests(ITestOutputHelper output) => _out = output;

    private readonly List<(string Step, string Result, string Detail)> _steps = new();

    private async Task<T?> Step<T>(string name, Func<Task<T>> body, Func<T, string?>? check = null)
    {
        try
        {
            var r = await body();
            var problem = check?.Invoke(r);
            _steps.Add((name, problem is null ? "pass" : "fail", problem ?? ""));
            return problem is null ? r : default;
        }
        catch (Exception ex)
        {
            _steps.Add((name, "fail", $"{ex.GetType().Name}: {ex.Message}" + (ex.InnerException is { } i ? $" -> {i.GetType().Name}: {i.Message}" : "")
                + " @ " + (ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("ClinicNode")) ?? "").Trim()));
            return default;
        }
    }

    [RealDbFact]
    public async Task RealDb_doctor_consultation_and_sync()
    {
        var dir = Environment.GetEnvironmentVariable("CLINIC_REALDB_DIR")!;
        var url = Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_URL");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClinicNodeCore(new NodeOptions { DataDirectory = dir, Demo = false });
        services.AddSingleton<INodeCredentialStore>(new InMemoryNodeCredentialStore(null));
        services.AddClinicNodeSync(o => o.BaseUrl = url ?? "");
        await using var sp = services.BuildServiceProvider();
        T Get<T>() where T : notnull => sp.GetRequiredService<T>();

        await Step("1 open DB copy (LocalDatabase.InitializeAsync)", async () => { await Get<LocalDatabase>().InitializeAsync(); return true; });

        var session = await Step("2 login doctor@clinic.test", async () =>
            (await Get<IAuthService>().LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, rememberEmail: false)),
            r => r.Succeeded ? null : $"login failed: {r.Error}");
        var doctorId = session?.Session?.DoctorId;
        if (doctorId is null) { Report(); return; }

        // pick a ready booking for today, else register a walk-in
        var bookingId = await Step("3 open by bookingId (today list / start consult or walk-in)", async () =>
        {
            var today = await Get<IVisitStarter>().ListTodayAsync(doctorId.Value);
            var ready = today.FirstOrDefault(v => v.Status is BookingStatus.InProgress or BookingStatus.CheckedIn);
            if (ready is not null)
            {
                var started = await Get<IDoctorDashboardService>().StartConsultAsync(doctorId.Value, ready.BookingId);
                if (started.Succeeded) return ready.BookingId;
            }
            var p = await Get<IPatientService>().CreateAsync(new NewPatientRequest("RealDb", "Harness" + DateTime.Now.ToString("HHmmss"), new DateOnly(1980, 1, 2), SexType.Female));
            return await Get<IVisitStarter>().StartWalkInConsultAsync(p.PatientId, doctorId.Value, null);
        });
        if (bookingId == default) { Report(); return; }

        var consults = Get<IConsultationService>();
        var catalog = Get<IDoctorCatalogService>();
        var ws = await Step("4 LoadByBookingAsync", () => consults.LoadByBookingAsync(bookingId));
        if (ws is null) { Report(); return; }
        var draft = ws.Draft;

        await Step("5 save draft (SOAP)", async () =>
        {
            draft.ChiefComplaint = "Cough and colds for 3 days"; draft.Subjective = "Productive cough, no fever.";
            draft.Objective = "Clear breath sounds."; draft.Assessment = "Acute URTI"; draft.Plan = "Symptomatic care.";
            return await consults.SaveDraftAsync(draft);
        }, r => r.Succeeded ? null : r.Error);

        await Step("6 vitals", async () =>
        {
            foreach (var v in draft.Vitals)
                v.Value = v.FormKey switch { "blood_pressure" => "120/80", "pulse_rate" => "76", "temperature" => "36.8", "respiratory_rate" => "18", "weight" => "62", "height" => "160", _ => "1" };
            return await consults.SaveDraftAsync(draft);
        }, r => r.Succeeded ? null : r.Error);

        var icd = await Step("7 ICD-10 search ('j06')", () => catalog.SearchIcd10Async("j06"), r => r.Count > 0 ? null : "no hits");
        await Step("8 diagnosis + save", async () =>
        {
            draft.Diagnoses.Clear();
            draft.Diagnoses.Add(new DiagnosisLine { Icd10Code = icd?.FirstOrDefault()?.Code ?? "J06.9", Type = DiagnosisType.Primary });
            return await consults.SaveDraftAsync(draft);
        }, r => r.Succeeded ? null : r.Error);

        var meds = await Step("9 medicine search ('paracetamol')", () => catalog.SearchMedicinesAsync("paracetamol"), r => r.Count > 0 ? null : "no hits");
        await Step("10 Rx + save", async () =>
        {
            draft.NoMedsConfirmed = false;
            draft.Prescriptions.Clear();
            draft.Prescriptions.Add(new RxLine { MedicineId = meds?.FirstOrDefault()?.MedicineId, GenericName = meds?.FirstOrDefault()?.GenericName ?? "Paracetamol",
                Dosage = "500 mg", Quantity = "10", Instruction = "1 tab every 6 hours as needed for fever", DurationKind = "Days", DurationValue = 3 });
            return await consults.SaveDraftAsync(draft);
        }, r => r.Succeeded ? null : r.Error);

        var labs = await Step("11 lab test names", () => catalog.ListLabTestNamesAsync(), r => r.Count > 0 ? null : "empty lab catalog");
        await Step("12 lab order + save", async () =>
        {
            draft.LabOrders.Clear();
            draft.LabOrders.Add(new LabLine { TestName = labs?.FirstOrDefault() ?? "CBC", Reason = "Baseline" });
            return await consults.SaveDraftAsync(draft);
        }, r => r.Succeeded ? null : r.Error);

        await Step("13 medical certificate + save", async () =>
        {
            draft.MedCertRequested = true;
            draft.McDiagnosis = "Acute upper respiratory tract infection"; draft.McRecommendations = "Rest for 2 days";
            draft.McExamFrom = DateOnly.FromDateTime(DateTime.Today); draft.McExamTo = DateOnly.FromDateTime(DateTime.Today);
            draft.FollowUpMode = FollowUpMode.None;
            return await consults.SaveDraftAsync(draft);
        }, r => r.Succeeded ? null : r.Error);

        await Step("14 complete", async () =>
        {
            draft.PfDecision = "Charge"; draft.PfAmount ??= ws.ComputedFee > 0 ? ws.ComputedFee : 500m;
            return await consults.CompleteAsync(draft);
        }, r => r.Succeeded ? null : $"{r.Error} [{string.Join(", ", r.MissingRequired)}]");

        var viewed = await Step("15 view completed (reload + history)", async () =>
        {
            var again = await consults.LoadByBookingAsync(bookingId);
            await catalog.GetHistoryAsync(again.Draft.PatientId);
            return again;
        }, r => r.Draft.Status == ConsultationStatus.Completed ? null : $"status {r.Draft.Status}");

        await Step("16 amend", async () =>
        {
            var d = viewed?.Draft ?? (await consults.LoadByBookingAsync(bookingId)).Draft;
            d.Plan = "Symptomatic care. Amended: add steam inhalation.";
            return await consults.AmendAsync(d);
        }, r => r.Succeeded && r.Draft!.Status == ConsultationStatus.Amended ? null : r.Error ?? $"status {r.Draft?.Status}");

        // ---- sync -------------------------------------------------------------------------------------------------
        var nodeId = Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_NODE_ID");
        var secret = Environment.GetEnvironmentVariable("CLINIC_SYNC_IT_NODE_SECRET");
        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(secret) && Guid.TryParse(nodeId, out var nid) && !url.Contains(":5055") && !url.Contains(":3000"))
        {
            await Step("17 node credentials (IT node) + endpoint", async () =>
            {
                await Get<INodeCredentialStore>().SaveAsync(new NodeCredential(nid, secret));
                return await Get<ISyncEndpoint>().SetBaseUrlAsync(url);
            }, r => r.Succeeded ? null : r.Error);
            int pendingBefore = 0;
            await using (var db = await Get<IDbContextFactory<ClinicApp.Infrastructure.ClinicAppDbContext>>().CreateDbContextAsync())
                pendingBefore = await Get<ISyncOutbox>().CountPendingAsync(db);
            var first = await Step($"18 sync push+pull ({pendingBefore} pending)", () => Get<ISyncService>().RunOnceAsync(), r => r.Succeeded ? null : $"{r.Failure}: {r.Error}");
            var review = await Get<ISyncStatus>().GetOpenConflictsAsync();
            var bad = review.Where(c => c.Kind is "rejected" or "apply_failed").ToList();
            _steps.Add(("19 push results / review list", bad.Count == 0 ? "pass" : "fail",
                $"pushed={first?.Pushed} acked={first?.Acked} retry={first?.RetryLater} pulled={first?.Pulled}; review: " +
                string.Join(" | ", review.GroupBy(c => $"{c.Kind}/{c.ReasonCode}/{c.Aggregate}").Select(g => $"{g.Key} x{g.Count()}: {g.First().Message}"))));
            await Step("20 second sync is a no-op", () => Get<ISyncService>().RunOnceAsync(),
                r => r.Succeeded && r.Pushed == 0 ? null : $"{r.Failure}: {r.Error} pushed={r.Pushed}");
        }
        else _steps.Add(("17-20 sync", "skipped", "CLINIC_SYNC_IT_URL / NODE_ID / NODE_SECRET not set"));

        Report();
        Assert.DoesNotContain(_steps, s => s.Result == "fail");
    }

    private void Report()
    {
        var lines = _steps.Select(s => $"| {s.Step} | {s.Result} | {s.Detail.Replace("|", "/")} |").ToList();
        foreach (var l in lines) _out.WriteLine(l);
        var dir = Environment.GetEnvironmentVariable("CLINIC_REALDB_DIR")!;
        File.WriteAllLines(Path.Combine(dir, "harness-report.md"), lines);
    }
}
//JJQR - END
