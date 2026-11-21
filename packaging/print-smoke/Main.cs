//JJQR - AI (Nov 21, 2026) - usage: PrintSmoke.app/Contents/MacOS/PrintSmoke <outDir> [--print] [--printer ipp://host/queue]
// Writes rx.pdf, rx-tall.pdf, lab.pdf, medcert.pdf, slip-web.pdf, slip-80.pdf, slip-58.pdf, slip-80.escpos and sizes.txt, then exits.
using ClinicNode.App.Services.Printing;
using ClinicNode.Core;
using ClinicNode.Core.Printing;
using Foundation;
using UIKit;

namespace PrintSmoke;

public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions) => true;
}

[Register("SceneDelegate")]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")] public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions options)
    {
        if (scene is not UIWindowScene ws) return;
        Window = new UIWindow(ws) { RootViewController = new UIViewController() };
        Window.RootViewController.View!.BackgroundColor = UIColor.White;
        Window.MakeKeyAndVisible();
        _ = RunAsync();
    }

    //JJQR - AI (Nov 21, 2026) - manifest: [{ name, title, page: { Name, Width, Height, Unit } }]
    static IEnumerable<(string, PrintDocument)> FromManifest(string dir)
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json")));
        foreach (var e in json.RootElement.EnumerateArray())
        {
            var name = e.GetProperty("name").GetString()!;
            var p = e.GetProperty("page");
            var page = new PrintPageSize(p.GetProperty("Name").GetString()!, p.GetProperty("Width").GetDouble(), p.GetProperty("Height").GetDouble(), p.GetProperty("Unit").GetString()!);
            yield return (name, new PrintDocument(e.GetProperty("title").GetString()!, page, File.ReadAllText(Path.Combine(dir, name + ".html"))));
        }
    }
    //JJQR - END

    static async Task RunAsync()
    {
        var args = NSProcessInfo.ProcessInfo.Arguments.Skip(1).ToList();
        //JJQR - AI (Nov 21, 2026) - --html-dir <dir>: render documents built from the real DB (packaging/print-real manifest.json) instead of samples
        var hi = args.IndexOf("--html-dir");
        var htmlDir = hi >= 0 && hi + 1 < args.Count ? args[hi + 1] : null;
        if (htmlDir is not null) args.RemoveRange(hi, 2);
        //JJQR - END
        var outDir = args.FirstOrDefault(a => !a.StartsWith("-") && !a.StartsWith("ipp")) ?? Path.Combine(Path.GetTempPath(), "clinicnode-print-smoke");
        var print = args.Contains("--print");
        var pi = args.IndexOf("--printer");
        var printer = pi >= 0 && pi + 1 < args.Count ? args[pi + 1] : null;
        Directory.CreateDirectory(outDir);
        var log = new List<string>();
        var code = 0;
        try
        {
            foreach (var (name, doc) in htmlDir is null ? Samples.All() : FromManifest(htmlDir))
            {
                var pdf = await MacPrintRenderer.RenderPdfAsync(doc);
                File.WriteAllBytes(Path.Combine(outDir, name + ".pdf"), pdf.ToArray());
                var size = MacPrintRenderer.FirstPageSize(pdf);
                log.Add($"{name}: {size.Width:0.##} x {size.Height:0.##} pt = {size.Width / 72 * 25.4:0.#} x {size.Height / 72 * 25.4:0.#} mm, {pdf.Length} bytes, paper '{doc.Page.Name}'");
                if (print)
                    await MacPrintRenderer.PrintPdfAsync(pdf, doc.Title, PrintPaper.KindOf(doc.Page) == PaperKind.Roll, showDialog: printer is null, printerUrl: printer);
            }
            if (htmlDir is null) File.WriteAllBytes(Path.Combine(outDir, "slip-80.escpos"), QueueSlip.EscPos(Samples.Ticket, PrintPaper.Thermal80));
        }
        catch (Exception ex)
        {
            log.Add("ERROR " + ex);
            code = 1;
        }
        File.WriteAllLines(Path.Combine(outDir, "sizes.txt"), log);
        Console.WriteLine(string.Join("\n", log));
        Environment.Exit(code);
    }
}

static class Samples
{
    static readonly PrintClinic Clinic = new("Grace Medical Clinic", "3ML Quezon National Highway, Buaya, Lapu Lapu City", "09285612976");
    static readonly PrintDoctor Doctor = new("Allyn Grace T. España-Gavino", "0123456", "PTR-2026-889", "S2-7788");
    static readonly PrintPatient Patient = new("Juan Dela Cruz", "MF-000123", "1990-05-14", "Male", "Pusok, Lapu-Lapu City");

    public static readonly QueueSlipData Ticket = new()
    {
        ClinicName = Clinic.ClinicName, ClinicAddress = Clinic.Address, QueueNumber = "S-004", IsPriority = true,
        PatientName = Patient.FullName, PatientCode = "MF-000123", DoctorName = "Dr. Allyn Grace España-Gavino",
        VisitType = "FollowUp", ProvisionalFee = 350m, IssuedAt = DateTimeOffset.Now,
    };

    static readonly RxLine[] Meds =
    [
        new() { GenericName = "Paracetamol", Dosage = "500 mg", Quantity = "20", Instruction = "1 tab every 6 hours as needed", Timing = "Breakfast, Dinner", MealRelation = "After", DurationKind = "Days", DurationValue = 5, Indication = "for fever", Route = "PO" },
        new() { GenericName = "Amoxicillin 500mg capsule", Dosage = "500mg", Quantity = "21", Instruction = "1 cap TID after meals", DurationKind = "Days", DurationValue = 7, Indication = "infection" },
        new() { GenericName = "Losartan", Dosage = "50 mg", Quantity = "30", Instruction = "once daily at night", DurationKind = "Maintain", Indication = "hypertension" },
    ];

    public static IEnumerable<(string, PrintDocument)> All()
    {
        yield return ("rx", ClinicPrintForms.Prescription(Clinic, Doctor, Patient, Meds, "2026-11-21"));
        yield return ("rx-tall", ClinicPrintForms.Prescription(Clinic, Doctor, Patient, [.. Meds, .. Meds], "2026-11-21"));
        yield return ("lab", ClinicPrintForms.LabRequest(Clinic, Doctor, Patient, ["CBC", "Urinalysis", "FBS", "Dengue NS1", "HbA1c"], "2026-11-21", ["J06.9 — Acute URTI"], "r/o dengue"));
        yield return ("medcert", ClinicPrintForms.MedicalCertificate(Clinic, Doctor with { S2Number = null }, Patient, new MedCertData
        {
            IssueDate = "2026-11-21", ExaminationDateFrom = "2026-11-20", ExaminationDateTo = "2026-11-21",
            DiagnosisText = "Acute upper respiratory tract infection", Recommendations = "Rest for 3 days; increase oral fluids.",
            PurposeException = "", ComeBackOn = "2026-11-28",
        }));
        yield return ("slip-web", QueueSlip.Web(Ticket));
        yield return ("slip-80", QueueSlip.Thermal(Ticket, PrintPaper.Thermal80));
        yield return ("slip-58", QueueSlip.Thermal(Ticket, PrintPaper.Thermal58));
        var receipt = new ReceiptPrintModel { OrNumber = "OR-2026-000123", BookingId = Guid.NewGuid(), PatientName = Patient.FullName,
            DoctorName = "Allyn Grace T. España-Gavino", AppointmentDate = new DateOnly(2026, 11, 21), Amount = 450m, PaymentStatus = "Paid" };
        yield return ("receipt", ReceiptDocument.Build(receipt));
        // calibration: boxes at known CSS lengths (mm, px, in) on printer-default paper, to measure the layout scale in the PDF
        foreach (var (cn, cp) in new[] { ("calib-a5", PrintPaper.A5), ("calib-lab", PrintPaper.LabForm), ("calib-80", PrintPaper.Thermal80) })
            yield return (cn, new PrintDocument(cn, cp, PrintHtmlDocument.Wrap(cn,
                "<div style='position:absolute;left:0;top:0;width:60mm;height:10mm;background:#f00'></div><div style='position:absolute;left:0;top:20mm;width:96px;height:10mm;background:#00f'></div>", bare: true)));
        yield return ("calib", new PrintDocument("calib", PrintPaper.PrinterDefault, PrintHtmlDocument.Wrap("calib",
            "<div style='position:absolute;left:0;top:0;width:100mm;height:10mm;background:#f00'></div>" +
            "<div style='position:absolute;left:0;top:20mm;width:96px;height:10mm;background:#00f'></div>" +
            "<div style='position:absolute;left:0;top:40mm;width:1in;height:10mm;background:#0a0'></div>", bare: true)));
        yield return ("receipt-pending", ReceiptDocument.Build(receipt with { IsPendingSync = true }));
    }
}
//JJQR - END
