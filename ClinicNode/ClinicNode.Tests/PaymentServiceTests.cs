// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain.Enums;
using ClinicNode.Core;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Payments;
using ClinicNode.Core.Queue;
using ClinicNode.Core.Visits;

namespace ClinicNode.Tests;

public class PaymentServiceTests
{
    [Fact]
    public async Task Confirm_accepts_exactly_the_amount_due_and_treats_a_blank_as_exact()
    {
        await using var host = await TestHost.StartAsync();
        await host.LoginStaffAsync();
        var (ticket, payments) = await FreshUnpaidAsync(host);

        var under = await payments.ConfirmAsync(ticket.PaymentId, new ConfirmPaymentRequest(PaymentMethod.Cash, AmountReceived: ticket.ProvisionalFee - 0.01m));
        Assert.False(under.Succeeded);

        var over = await payments.ConfirmAsync(ticket.PaymentId, new ConfirmPaymentRequest(PaymentMethod.Cash, AmountReceived: ticket.ProvisionalFee + 1m));
        Assert.False(over.Succeeded);

        var exact = await payments.ConfirmAsync(ticket.PaymentId, new ConfirmPaymentRequest(PaymentMethod.GCash, ReferenceNumber: "R1"));
        Assert.True(exact.Succeeded, exact.Error);
        var unpaid = await payments.ListUnpaidAsync();
        Assert.DoesNotContain(unpaid, p => p.PaymentId == ticket.PaymentId);
    }

    [Fact]
    public async Task Cannot_collect_a_waived_fee_and_staff_can_confirm_the_waiver()
    {
        await using var host = await TestHost.StartAsync();
        var doctor = await host.LoginDoctorAsync();
        var visits = host.Get<IVisitStarter>();
        var consults = host.Get<IConsultationService>();
        var patients = host.Get<IPatientService>();
        var patient = await patients.CreateAsync(new NewPatientRequest("Wai", "Ver", new DateOnly(1975, 7, 7), SexType.Female));
        var bookingId = await visits.StartWalkInConsultAsync(patient.PatientId, doctor.DoctorId!.Value, null);
        var workspace = await consults.LoadByBookingAsync(bookingId);
        FillRequired(workspace.Draft, workspace);
        workspace.Draft.PfDecision = "Waive";
        workspace.Draft.PfWaiveReason = "Indigent";
        var done = await consults.CompleteAsync(workspace.Draft);
        Assert.True(done.Succeeded, done.Error);

        await host.LoginStaffAsync();
        var payments = host.Get<IPaymentService>();
        var waiting = await payments.ListWaivedAwaitingConfirmationAsync();
        var row = Assert.Single(waiting);
        Assert.Equal("Indigent", row.WaivedReason);

        var collect = await payments.ConfirmAsync(row.PaymentId, new ConfirmPaymentRequest(PaymentMethod.Cash));
        Assert.False(collect.Succeeded);
        Assert.Contains("waived", collect.Error, StringComparison.OrdinalIgnoreCase);

        var confirm = await payments.ConfirmWaiverAsync(row.PaymentId);
        Assert.True(confirm.Succeeded, confirm.Error);
        Assert.Empty(await payments.ListWaivedAwaitingConfirmationAsync());
        var again = await payments.ConfirmWaiverAsync(row.PaymentId);
        Assert.True(again.Succeeded, "confirm waiver is idempotent");
    }

    static async Task<(WalkInTicket Ticket, IPaymentService Payments)> FreshUnpaidAsync(TestHost host)
    {
        var patients = host.Get<IPatientService>();
        var queue = host.Get<IQueueService>();
        var payments = host.Get<IPaymentService>();
        var doctorId = (await queue.ListDoctorsAsync())[0].DoctorId;
        var patient = await patients.CreateAsync(new NewPatientRequest("Pat", "Pay", new DateOnly(1984, 4, 4), SexType.Male));
        var result = await queue.CheckInWalkInAsync(new WalkInRequest(patient.PatientId, doctorId));
        Assert.True(result.Succeeded, result.Error);
        return (result.Ticket!, payments);
    }

    static void FillRequired(ConsultationDraft draft, ConsultationWorkspace workspace)
    {
        draft.ChiefComplaint = "Cough";
        draft.Subjective = "Cough for 3 days";
        foreach (var v in draft.Vitals)
        {
            if (v.FormKey is "blood_pressure") v.Value = "120/80";
            else if (v.FormKey is "pulse_rate") v.Value = "72";
            else if (string.IsNullOrWhiteSpace(v.Value)) v.Value = "1";
        }
        if (draft.Vitals.Count == 0)
        {
            foreach (var t in workspace.VitalTemplates)
            {
                draft.Vitals.Add(new VitalLine { TemplateId = t.TemplateId, FormKey = t.FormKey, Description = t.Description, Unit = t.Unit, Value = t.FormKey == "blood_pressure" ? "120/80" : "72" });
            }
        }
        draft.NoMedsConfirmed = true;
        draft.FollowUpMode = FollowUpMode.None;
        draft.PfDecision = "Charge";
        draft.PfAmount = workspace.ComputedFee > 0 ? workspace.ComputedFee : 450m;
    }
}
// JJQR - END
