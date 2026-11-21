// JJQR - AI (Nov 21, 2026)
using ClinicApp.Domain.Enums;
using ClinicNode.Core;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Patients;
using ClinicNode.Core.Visits;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Tests;

public class ConsultationServiceTests
{
    [Fact]
    public async Task Complete_requires_the_same_gates_as_the_web_page()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var consults = host.Get<IConsultationService>();
        var patients = host.Get<IPatientService>();
        var visits = host.Get<IVisitStarter>();

        var patient = await patients.CreateAsync(new NewPatientRequest("Ana", "Cruz", new DateOnly(1985, 3, 1), SexType.Female));
        var bookingId = await visits.StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var workspace = await consults.LoadByBookingAsync(bookingId);

        var missing = consults.MissingRequired(workspace.Draft);
        Assert.Contains("Clinical notes", missing);
        Assert.Contains("Vital Signs", missing);
        Assert.Contains("Prescription", missing);
        Assert.Contains("Follow-up", missing);
        Assert.Contains("Professional Fee Decision", missing);
        Assert.DoesNotContain("Diagnosis", missing);

        var failed = await consults.CompleteAsync(workspace.Draft);
        Assert.False(failed.Succeeded);
        Assert.NotEmpty(failed.MissingRequired);
    }

    [Fact]
    public async Task Save_and_complete_a_consultation_offline_and_auto_waive_payment()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var consults = host.Get<IConsultationService>();
        var patients = host.Get<IPatientService>();
        var visits = host.Get<IVisitStarter>();

        var patient = await patients.CreateAsync(new NewPatientRequest("Ben", "Santos", new DateOnly(1970, 6, 15), SexType.Male));
        var bookingId = await visits.StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var workspace = await consults.LoadByBookingAsync(bookingId);
        var draft = FillRequired(workspace, waive: true, reason: "Indigent patient");

        var saved = await consults.SaveDraftAsync(draft);
        Assert.True(saved.Succeeded, saved.Error);
        Assert.Equal(ConsultationStatus.Draft, saved.Draft!.Status);
        Assert.NotNull(saved.Draft.ConsultationId);

        var completed = await consults.CompleteAsync(saved.Draft);
        Assert.True(completed.Succeeded, completed.Error);
        Assert.Equal(ConsultationStatus.Completed, completed.Draft!.Status);
        Assert.NotNull(completed.Draft.CompletedAt);

        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var consult = await db.Consultations.SingleAsync(c => c.BookingId == bookingId);
        Assert.Equal(ConsultationStatus.Completed, consult.Status);
        Assert.Equal("Waive", consult.PfDecision);
        var payment = await db.Payments.SingleAsync(p => p.BookingId == bookingId);
        Assert.Equal(PaymentStatus.Waived, payment.Status);
        Assert.Equal("Indigent patient", payment.WaivedReason);
        Assert.Null(payment.ConfirmedAt); // staff must Confirm waiver

        var followUp = await db.FollowUps.SingleOrDefaultAsync(f => f.ConsultationId == consult.ConsultationId);
        Assert.Null(followUp); // "no follow-up" deletes the row

        var vitals = await db.PatientVitalReadings.Where(v => v.BookingId == bookingId).ToListAsync();
        Assert.True(vitals.Count >= 2);
        var keys = await db.VitalFieldTemplates.Where(t => vitals.Select(v => v.TemplateId).Contains(t.TemplateId)).Select(t => t.FormKey).ToListAsync();
        Assert.Contains("blood_pressure", keys);
        Assert.Contains("pulse_rate", keys);

        var pending = await host.Get<ISyncOutbox>().CountPendingAsync(db);
        Assert.True(pending >= 2, $"expected patient + booking + consultation outbox rows, got {pending}");
    }

    [Fact]
    public async Task Finished_consultation_is_immutable_after_the_completion_window()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var consults = host.Get<IConsultationService>();
        var patients = host.Get<IPatientService>();
        var visits = host.Get<IVisitStarter>();

        var patient = await patients.CreateAsync(new NewPatientRequest("Cara", "Reyes", new DateOnly(1992, 1, 9), SexType.Female));
        var bookingId = await visits.StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var workspace = await consults.LoadByBookingAsync(bookingId);
        var completed = await consults.CompleteAsync(FillRequired(workspace, waive: false, reason: null));
        Assert.True(completed.Succeeded, completed.Error);

        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using (var db = await factory.CreateDbContextAsync())
        {
            var row = await db.Consultations.SingleAsync(c => c.BookingId == bookingId);
            row.CompletedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);
            await db.SaveChangesAsync();
        }

        var again = await consults.LoadByBookingAsync(bookingId);
        again.Draft.ChiefComplaint = "Changed after seal";
        var blocked = await consults.SaveDraftAsync(again.Draft);
        Assert.False(blocked.Succeeded);
        Assert.Contains("immutable", blocked.Error, StringComparison.OrdinalIgnoreCase);
    }

    // JJQR - AI (Nov 21, 2026)
    [Fact]
    public async Task Amend_changes_clinical_content_only_and_marks_the_visit_Amended()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var consults = host.Get<IConsultationService>();
        var patients = host.Get<IPatientService>();
        var visits = host.Get<IVisitStarter>();

        var patient = await patients.CreateAsync(new NewPatientRequest("Cara", "Reyes", new DateOnly(1990, 1, 1), SexType.Female));
        var bookingId = await visits.StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var workspace = await consults.LoadByBookingAsync(bookingId);

        var early = await consults.AmendAsync(workspace.Draft);
        Assert.False(early.Succeeded); // a draft cannot be amended

        var completed = await consults.CompleteAsync(FillRequired(workspace, waive: true, reason: "Indigent patient"));
        Assert.True(completed.Succeeded, completed.Error);

        var draft = completed.Draft!;
        draft.Plan = "Amended plan: rest and fluids";
        draft.PfDecision = "Charge"; // must be ignored by an amendment
        draft.PfAmount = 999m;
        var amended = await consults.AmendAsync(draft);
        Assert.True(amended.Succeeded, amended.Error);
        Assert.Equal(ConsultationStatus.Amended, amended.Draft!.Status);

        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var consult = await db.Consultations.SingleAsync(c => c.BookingId == bookingId);
        Assert.Equal(ConsultationStatus.Amended, consult.Status);
        Assert.Equal("Amended plan: rest and fluids", consult.Plan);
        Assert.Equal("Waive", consult.PfDecision);
        Assert.Equal(PaymentStatus.Waived, (await db.Payments.SingleAsync(p => p.BookingId == bookingId)).Status);
    }

    //JJQR - AI (Nov 21, 2026) - amend must not upsert vitals (cloud §5.5 append-only)
    [Fact]
    public async Task Amend_does_not_change_recorded_vitals()
    {
        await using var host = await TestHost.StartAsync();
        var session = await host.LoginDoctorAsync();
        var consults = host.Get<IConsultationService>();
        var patients = host.Get<IPatientService>();
        var visits = host.Get<IVisitStarter>();

        var patient = await patients.CreateAsync(new NewPatientRequest("Diego", "Cruz", new DateOnly(1988, 4, 4), SexType.Male));
        var bookingId = await visits.StartWalkInConsultAsync(patient.PatientId, session.DoctorId!.Value, null);
        var workspace = await consults.LoadByBookingAsync(bookingId);
        var completed = await consults.CompleteAsync(FillRequired(workspace, waive: true, reason: "Indigent patient"));
        Assert.True(completed.Succeeded, completed.Error);

        var factory = host.Get<IDbContextFactory<ClinicAppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        var before = await db.PatientVitalReadings.AsNoTracking()
            .Where(v => v.BookingId == bookingId)
            .OrderBy(v => v.TemplateId)
            .Select(v => new { v.Id, v.TemplateId, v.Value })
            .ToListAsync();
        Assert.NotEmpty(before);

        var draft = completed.Draft!;
        draft.Plan = "Amended plan: rest";
        foreach (var vital in draft.Vitals) vital.Value = "999";
        var amended = await consults.AmendAsync(draft);
        Assert.True(amended.Succeeded, amended.Error);

        var after = await db.PatientVitalReadings.AsNoTracking()
            .Where(v => v.BookingId == bookingId)
            .OrderBy(v => v.TemplateId)
            .Select(v => new { v.Id, v.TemplateId, v.Value })
            .ToListAsync();
        Assert.Equal(before, after);
    }
    //JJQR - END
    // JJQR - END

    private static ConsultationDraft FillRequired(ConsultationWorkspace workspace, bool waive, string? reason)
    {
        var draft = workspace.Draft;
        draft.ChiefComplaint = "Cough for 3 days";
        draft.Subjective = "Dry cough, no fever.";
        draft.NoMedsConfirmed = true;
        draft.FollowUpMode = FollowUpMode.None;
        foreach (var vital in draft.Vitals)
        {
            if (vital.FormKey == "blood_pressure") vital.Value = "120/80";
            else if (vital.FormKey == "pulse_rate") vital.Value = "72";
            else if (string.IsNullOrWhiteSpace(vital.Value)) vital.Value = "1";
        }
        if (waive)
        {
            draft.PfDecision = "Waive";
            draft.PfWaiveReason = reason;
            draft.PfAmount = null;
        }
        else
        {
            draft.PfDecision = "Charge";
            draft.PfAmount = workspace.ComputedFee > 0 ? workspace.ComputedFee : 450m;
        }
        return draft;
    }
}
// JJQR - END
