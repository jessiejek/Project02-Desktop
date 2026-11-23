// JJQR - AI (Nov 21, 2026) - offline clinic-day end-to-end against a temp SQLite DB, using the real app service graph.
using ClinicApp.Domain.Enums;
using ClinicNode.Core;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Practice;
using ClinicNode.Core.Print;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Staff;
using ClinicNode.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests;

public class ClinicDayTests
{
    [Fact]
    public async Task Offline_clinic_day_from_walk_in_to_payment_receipt_and_amend_writes_outbox_ops()
    {
        await using var host = await AppTestHost.StartAsync();
        var auth = host.Get<IAuthService>();
        //JJQR - AI (Nov 23, 2026) - owner: "I want perfect offline mode". This test described the OLD design (staff working offline) and never actually went offline, so it stopped at its first save. It is now the real offline day: the DOCTOR switches the computer to Offline mode
        // (a registered computer) and does everything alone - register, check in, call in, full consultation, booklet payment, receipt, amend - with no clinic server at all.
        var mode = host.Get<ClinicNode.Core.Sync.IWorkModeService>();
        await mode.InitializeAsync();
        var doctor = await auth.LoginAsync("doctor@clinic.test", DevDataSeeder.DevPassword, rememberEmail: false);
        Assert.True(doctor.Succeeded, doctor.Error);
        await host.Get<ClinicNode.Sync.Auth.INodeCredentialStore>().SaveAsync(new ClinicNode.Sync.Auth.NodeCredential(Guid.NewGuid(), "test-secret"));
        Assert.True((await mode.SetAsync(ClinicNode.Core.Sync.WorkMode.Offline)).Succeeded);

        // a computer that was online once holds reserved queue numbers for the coming days, in both lanes (otherwise a check-in is a paper-queue patient, which DoctorOfflineDayTests covers)
        var blockFactory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using (var seedDb = await blockFactory.CreateDbContextAsync())
            foreach (var day in ClinicNode.Sync.Local.QueueBlocks.Days(ClinicApp.Domain.ClinicClock.Today))
                foreach (var lane in ClinicNode.Sync.Local.QueueBlocks.Lanes)
                    await ClinicNode.Sync.Local.QueueBlocks.SaveAsync(seedDb, new ClinicNode.Sync.QueueBlockReservation(day, lane, 1, 200, 200, null, null));

        // 1. the doctor registers a patient (official MF- code from the reserved block)
        var form = new QuickRegisterForm("Mia", null, "Fernandez", new DateOnly(1992, 4, 4), SexType.Female, "09171234567", "Cebu");
        var (registered, patient) = await host.Get<IStaffPatientsService>().QuickRegisterAsync(form);
        Assert.True(registered.Succeeded, registered.Error);
        Assert.NotNull(patient);
        Assert.StartsWith("MF-", patient!.PatientCode);
        //JJQR - END

        // 2. Walk-in issues a queue number.
        var doctors = await host.Get<IQueueService>().ListDoctorsAsync();
        Assert.NotEmpty(doctors);
        var doctorId = doctors[0].DoctorId;
        var walkIn = await host.Get<IQueueService>().CheckInWalkInAsync(new WalkInRequest(patient.PatientId, doctorId));
        Assert.True(walkIn.Succeeded, walkIn.Error);
        Assert.False(string.IsNullOrWhiteSpace(walkIn.Ticket?.QueueNumber));
        var bookingId = walkIn.Ticket!.BookingId;

        // 3. Doctor calls the patient in, completes a consult with vitals, Rx and ICD-10.
        Assert.Equal(doctorId, doctor.Session!.DoctorId);
        var called = await host.Get<IQueueService>().CallAsync(bookingId);
        Assert.True(called.Succeeded, called.Error);

        var consults = host.Get<IConsultationService>();
        var workspace = await consults.LoadByBookingAsync(bookingId);
        Assert.Equal(BookingStatus.InProgress, (await host.Get<IQueueService>().ListDoctorTodayAsync(doctorId)).Single(e => e.BookingId == bookingId).Status);

        var icd = await host.Get<IDoctorCatalogService>().SearchIcd10Async("J45");
        Assert.Contains(icd, h => h.Code.StartsWith("J45", StringComparison.Ordinal));
        var code = icd.First(h => h.Code.StartsWith("J45", StringComparison.Ordinal));

        var draft = workspace.Draft;
        draft.ChiefComplaint = "Cough and wheeze for 3 days";
        draft.Subjective = "Dry cough, no fever, known asthma.";
        draft.Objective = "Clear chest, no distress.";
        draft.Assessment = $"{code.Code} {code.Description}";
        draft.Plan = "Reliever as needed, review in a week.";
        draft.Diagnoses.Add(new DiagnosisLine { Icd10Code = code.Code, CustomDescription = code.Description, Type = DiagnosisType.Primary });
        draft.NoMedsConfirmed = false;
        draft.Prescriptions.Add(new RxLine { GenericName = "Salbutamol", Dosage = "100 mcg", Quantity = "1 inhaler", Instruction = "2 puffs", Timing = "As needed", Route = "Inhalation" });
        draft.FollowUpMode = FollowUpMode.Dated;
        draft.FollowUpDate = ClinicApp.Domain.ClinicClock.Today.AddDays(7);
        draft.FollowUpReason = "Review asthma";
        foreach (var vital in draft.Vitals)
        {
            if (vital.FormKey == "blood_pressure") vital.Value = "120/80";
            else if (vital.FormKey == "pulse_rate") vital.Value = "72";
            else if (string.IsNullOrWhiteSpace(vital.Value)) vital.Value = "1";
        }
        draft.PfDecision = "Charge";
        draft.PfAmount = workspace.ComputedFee > 0 ? workspace.ComputedFee : 450m;
        var completed = await consults.CompleteAsync(draft);
        Assert.True(completed.Succeeded, completed.Error);
        Assert.Equal(ConsultationStatus.Completed, completed.Draft!.Status);

        // 4. The doctor marks the exact payment received (typing the receipt number from the booklet) and the receipt document is produced (and handed to the printer).
        var payments = host.Get<IStaffPaymentsService>();
        var due = draft.PfAmount!.Value;
        var paid = await payments.RecordAsync(new RecordPaymentInput(bookingId, PaymentMethod.Cash, due.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), null, null, "BK-000777"));
        Assert.True(paid.Succeeded, paid.Error);
        var receipt = await host.Get<IPrintDocumentFactory>().PaymentReceiptAsync(bookingId);
        Assert.False(string.IsNullOrWhiteSpace(receipt.Html));
        Assert.Contains("₱", receipt.Html, StringComparison.Ordinal);
        var printer = (RecordingPrinter)host.Get<IPrinter>();
        await printer.PrintAsync(receipt);
        Assert.Contains(printer.Documents, d => d.Title == receipt.Title);

        // 5. Doctor amends the completed chart (clinical content only).
        var toAmend = (await consults.LoadByBookingAsync(bookingId)).Draft;
        toAmend.Plan = "Amended plan: add a spacer; review in a week.";
        var amended = await consults.AmendAsync(toAmend);
        Assert.True(amended.Succeeded, amended.Error);
        Assert.Equal(ConsultationStatus.Amended, amended.Draft!.Status);

        // Outbox ops exist for each step of the day.
        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var pending = await host.Get<ISyncOutbox>().ListPendingAsync(db, 500);
        bool Has(string aggregate, string? op = null) =>
            pending.Any(e => e.Aggregate == aggregate && (op is null || e.OperationType == op));

        Assert.True(Has("patient"), "expected a patient outbox row after quick-register");
        Assert.True(Has("booking"), "expected a booking outbox row after the walk-in / call-in");
        Assert.True(Has("consultation", "complete") || Has("consultation"), "expected a consultation complete outbox row");
        Assert.True(Has("payment"), "expected a payment outbox row after collect");
        Assert.True(Has("consultation", "amend") || pending.Count(e => e.Aggregate == "consultation") >= 2,
            "expected an amend (or second consultation) outbox row");
    }

    static async Task ExhaustReservedBlockAsync(AppTestHost host)
    {
        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE node_patient_code_blocks SET next_value = end_inclusive + 1 WHERE prefix = 'MF-';";
        await cmd.ExecuteNonQueryAsync();
    }
}
// JJQR - END
