//JJQR - AI (Nov 21, 2026) - print test against a COPY of the sandbox DB (never the original). Completes real consultations and records
// real payments on seeded bookings through the app services, then builds EVERY print document with the real ClinicFormsPrintDocumentFactory
// and writes the HTML + a manifest for packaging/print-smoke (--html-dir) to render through MacPrintRenderer.
// Run: CLINIC_PRINT_COPY_DIR=/tmp/cn-print-db dotnet test packaging/print-real
//      report: /tmp/cn-print-real/report.txt, documents: /tmp/cn-print-real/*.html + manifest.json
using System.Text;
using System.Text.Json;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.DoctorExtra;
using ClinicNode.Core.Printing;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using ClinicNode.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicNode.PrintReal;

public class PrintRealDbHarness
{
    const string Out = "/tmp/cn-print-real";
    readonly List<string> _lines = [];
    int _pass, _fail;
    readonly List<object> _manifest = [];

    async Task<T?> Step<T>(string what, Func<Task<T>> body)
    {
        try { var r = await body(); _pass++; _lines.Add("PASS | " + what); return r; }
        catch (Exception ex) { _fail++; var inner = ex; var msg = ""; while (inner is not null) { msg += $" => {inner.GetType().Name}: {inner.Message}"; inner = inner.InnerException; } _lines.Add($"FAIL | {what} |{msg}\n{ex.StackTrace?.Split('\n').FirstOrDefault(l => l.Contains("ClinicNode."))?.Trim()}\n"); return default; }
    }
    Task Step(string what, Func<Task> body) => Step(what, async () => { await body(); return true; });

    [Fact, Trait("Category", "PrintRealDb")]
    public async Task Every_print_document_from_real_seeded_bookings()
    {
        var src = Environment.GetEnvironmentVariable("CLINIC_PRINT_COPY_DIR");
        if (string.IsNullOrEmpty(src) || !Directory.Exists(src)) return;
        Assert.StartsWith("/tmp/", src);   // safety: never the sandbox container
        var run = Path.Combine("/tmp", "cn-print-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        foreach (var f in Directory.GetFiles(src, "clinic-node.db*")) File.Copy(f, Path.Combine(run, Path.GetFileName(f)));
        if (Directory.Exists(Out)) Directory.Delete(Out, true);
        Directory.CreateDirectory(Out);

        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddClinicNodeCore(new NodeOptions { DataDirectory = run, Demo = true });
        sc.AddClinicNodeSync();
        sc.AddClinicNodeStaff();
        sc.AddClinicNodeDoctorExtra();
        await using var sp = sc.BuildServiceProvider();
        T Get<T>() where T : notnull => sp.GetRequiredService<T>();
        var dbf = Get<IDbContextFactory<ClinicAppDbContext>>();
        // Existing node DBs were made by EnsureCreated before newer model tables existed (e.g. sync_changes from the CloudSync change log).
        // Report them, then add them to THIS COPY only so the print path can be exercised (the real fix belongs in LocalDatabase / backend).
        await Step("schema drift check (model tables missing from the existing DB)", async () =>
        {
            await using var db = await dbf.CreateDbContextAsync();
            var conn = (Microsoft.Data.Sqlite.SqliteConnection)db.Database.GetDbConnection();
            await conn.OpenAsync();
            var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
                await using var r = await cmd.ExecuteReaderAsync();
                while (await r.ReadAsync()) have.Add(r.GetString(0));
            }
            var missing = db.Model.GetEntityTypes().Select(t => t.GetTableName()).Where(n => n is not null && !have.Contains(n)).Distinct().ToList();
            _lines.Add("NOTE | model tables missing from the sandbox DB: " + (missing.Count == 0 ? "none" : string.Join(", ", missing)));
            if (missing.Count == 0) return;
            var script = db.Database.GenerateCreateScript();
            foreach (var stmt in script.Split(";", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!missing.Any(m => stmt.Contains($"\"{m}\"", StringComparison.Ordinal))) continue;
                if (!stmt.StartsWith("CREATE", StringComparison.OrdinalIgnoreCase)) continue;
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = stmt.Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ").Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ").Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ");
                await cmd.ExecuteNonQueryAsync();
            }
            _lines.Add("NOTE | created on the /tmp copy only: " + string.Join(", ", missing));
        });
        await Step("LocalDatabase.InitializeAsync on the copy", () => Get<LocalDatabase>().InitializeAsync());

        List<(Guid Id, string Queue, string DoctorEmail)> bookings = [];
        await Step("read seeded bookings", async () =>
        {
            await using var db = await dbf.CreateDbContextAsync();
            var rows = await db.Bookings.AsNoTracking().Include(b => b.Doctor).ToListAsync();
            foreach (var b in rows.OrderBy(b => b.QueueNumber))
            {
                var email = await db.Users.AsNoTracking().Where(u => u.Id == b.DoctorId).Select(u => u.Email).SingleOrDefaultAsync() ?? "";
                bookings.Add((b.BookingId, b.QueueNumber ?? "", email));
            }
            _lines.Add("NOTE | bookings: " + string.Join(", ", bookings.Select(b => $"{b.Queue} {b.Id} ({b.DoctorEmail})")));
            if (bookings.Count < 2) throw new InvalidOperationException("need 2 seeded bookings");
        });
        if (bookings.Count < 2) { Finish(run); return; }
        var auth = Get<IAuthService>();
        var consults = Get<IConsultationService>();

        async Task CompleteAsync((Guid Id, string Queue, string DoctorEmail) b, bool full)
        {
            var login = await auth.LoginAsync(b.DoctorEmail, DevDataSeeder.DevPassword, false);
            if (!login.Succeeded) throw new InvalidOperationException($"login {b.DoctorEmail}: {login.Error}");
            var ws = await consults.LoadByBookingAsync(b.Id);
            var d = ws.Draft;
            d.ChiefComplaint = "Cough and fever for 3 days";
            d.Subjective = "Dry cough, fever 38.5 C, no dyspnea.";
            d.Assessment = "r/o dengue";
            foreach (var v in d.Vitals)
                v.Value = v.FormKey switch { "blood_pressure" => "120/80", "pulse_rate" => "88", _ => string.IsNullOrWhiteSpace(v.Value) ? "1" : v.Value };
            d.Diagnoses = [new DiagnosisLine { Icd10Code = "J06.9", CustomDescription = "Acute upper respiratory infection" }];
            d.FollowUpMode = FollowUpMode.None;
            d.PfDecision = "Charge";
            d.PfAmount = ws.ComputedFee > 0 ? ws.ComputedFee : 500m;
            if (full)
            {
                d.NoMedsConfirmed = false;
                d.Prescriptions =
                [
                    new() { GenericName = "Paracetamol", Dosage = "500 mg", Quantity = "20", Instruction = "every 6 hours as needed", Timing = "Breakfast,Dinner", MealRelation = "After", DurationKind = "Days", DurationValue = 5, Indication = "for fever", Route = "PO" },
                    new() { GenericName = "Amoxicillin 500mg capsule", Dosage = "500mg", Quantity = "21", Instruction = "1 cap TID after meals", DurationKind = "Days", DurationValue = 7, Indication = "infection" },
                    new() { GenericName = "Losartan", Dosage = "50 mg", Quantity = "30", Instruction = "once daily at night", DurationKind = "Maintain", Indication = "hypertension" },
                ];
                d.LabOrders = [new() { TestName = "CBC" }, new() { TestName = "Urinalysis" }, new() { TestName = "Dengue NS1" }];
                d.MedCertRequested = true;
                d.CertificateExists = true; // the "Issue certificate" toggle on the consult page
                d.McAddress = "Pusok, Lapu-Lapu City";
                d.McExaminedAt = "Grace Medical Clinic";
                d.McExamFrom = DateOnly.FromDateTime(DateTime.Today);
                d.McExamTo = DateOnly.FromDateTime(DateTime.Today);
                d.McDiagnosis = "Acute upper respiratory infection";
                d.McRecommendations = "Rest for 3 days";
                d.McComeBackOn = DateOnly.FromDateTime(DateTime.Today.AddDays(7));
            }
            else d.NoMedsConfirmed = true;
            var missing = consults.MissingRequired(d);
            if (missing.Count > 0) _lines.Add("NOTE | missing before complete: " + string.Join(", ", missing));
            var r = await consults.CompleteAsync(d);
            if (!r.Succeeded) throw new InvalidOperationException("complete: " + r.Error);
        }

        async Task PayAsync(Guid bookingId)
        {
            var login = await auth.LoginAsync("staff@clinic.test", DevDataSeeder.DevPassword, false);
            if (!login.Succeeded) throw new InvalidOperationException("staff login: " + login.Error);
            var pay = Get<IStaffPaymentsService>();
            await using var db = await dbf.CreateDbContextAsync();
            var due = (await db.Bookings.AsNoTracking().SingleAsync(x => x.BookingId == bookingId)).AmountDue;
            var r = await pay.RecordAsync(new RecordPaymentInput(bookingId, PaymentMethod.Cash, due.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), null, null));
            if (!r.Succeeded) throw new InvalidOperationException("record payment: " + r.Error);
        }

        var a = bookings[0];
        var b2 = bookings[1];
        await Step($"doctor completes consult with Rx/labs/med cert on {a.Queue}", () => CompleteAsync(a, full: true));
        await Step($"doctor completes consult on {b2.Queue}", () => CompleteAsync(b2, full: false));
        await Step($"staff records payment {b2.Queue}", () => PayAsync(b2.Id));
        await Step("ack all outbox rows (simulates a successful sync) so the first receipt is synced", async () =>
        {
            await using var db = await dbf.CreateDbContextAsync();
            var outbox = Get<ISyncOutbox>();
            foreach (var e in await outbox.ListPendingAsync(db, 1000)) await outbox.MarkAckedAsync(db, e.OperationId);
        });
        await Step($"staff records payment {a.Queue} (stays unsynced → PENDING SYNC)", () => PayAsync(a.Id));

        var factory = new ClinicFormsPrintDocumentFactory(dbf, new PrintingOptions(), null, Get<IStaffPaymentsService>());
        async Task Doc(string name, Func<Task<PrintDocument>> make, Action<PrintDocument>? check = null) => await Step("build " + name, async () =>
        {
            var doc = await make();
            check?.Invoke(doc);
            await File.WriteAllTextAsync(Path.Combine(Out, name + ".html"), doc.Html);
            _manifest.Add(new { name, title = doc.Title, page = new { doc.Page.Name, doc.Page.Width, doc.Page.Height, doc.Page.Unit } });
        });
        static void Has(PrintDocument d, string s) { if (!d.Html.Contains(s, StringComparison.Ordinal)) throw new InvalidOperationException($"'{s}' missing from {d.Title}"); }
        static void HasNot(PrintDocument d, string s) { if (d.Html.Contains(s, StringComparison.Ordinal)) throw new InvalidOperationException($"'{s}' unexpectedly in {d.Title}"); }

        await Doc("rx", () => factory.PrescriptionAsync(a.Id), d => { Has(d, "Paracetamol"); Has(d, "Losartan"); });
        await Doc("lab", () => factory.LabRequestAsync(a.Id), d => Has(d, "J06.9"));
        await Doc("medcert", () => factory.MedicalCertificateAsync(a.Id), d => Has(d, "Rest for 3 days"));
        await Doc("slip-web", () => factory.QueueSlipAsync(a.Id), d => Has(d, a.Queue));
        await Doc("slip-80", () => new ClinicFormsPrintDocumentFactory(dbf, new PrintingOptions { QueueSlipStyle = QueueSlipStyle.Thermal80 }).QueueSlipAsync(a.Id), d => Has(d, a.Queue));
        await Doc("slip-58", () => new ClinicFormsPrintDocumentFactory(dbf, new PrintingOptions { QueueSlipStyle = QueueSlipStyle.Thermal58 }).QueueSlipAsync(b2.Id), d => Has(d, b2.Queue));
        await Doc("receipt-pending", () => factory.PaymentReceiptAsync(a.Id), d => Has(d, "PENDING SYNC"));
        await Doc("receipt-synced", () => factory.PaymentReceiptAsync(b2.Id), d => HasNot(d, "PENDING SYNC"));
        // error paths a user can hit from the UI
        await Step("Rx on a visit without medicines gives a clear message", async () =>
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.PrescriptionAsync(b2.Id));
            _lines.Add("NOTE | " + ex.Message);
        });
        if (bookings.Count > 2)
            await Step("receipt before payment gives a clear message", async () =>
            {
                var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.PaymentReceiptAsync(bookings[2].Id));
                _lines.Add("NOTE | " + ex.Message);
            });

        await File.WriteAllTextAsync(Path.Combine(Out, "manifest.json"), JsonSerializer.Serialize(_manifest, new JsonSerializerOptions { WriteIndented = true }));
        Finish(run);
    }

    void Finish(string run)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Run dir (copy of the copy): {run}");
        sb.AppendLine($"TOTAL: {_pass} pass / {_fail} fail");
        foreach (var l in _lines) sb.AppendLine(l);
        Directory.CreateDirectory(Out);
        File.WriteAllText(Path.Combine(Out, "report.txt"), sb.ToString());
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(run, true); } catch { }
        Assert.True(_fail == 0, $"{_fail} step(s) failed; see {Out}/report.txt");
    }
}
//JJQR - END
