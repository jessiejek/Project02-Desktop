// JJQR - AI (Nov 21, 2026) - in-process consultation load/save/complete against local SQLite,
// ported from ConsultationsController + child controllers + the web completion gates.
using System.Text.Json;
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Auth;
using ClinicNode.Data;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Clinical;

public sealed class ConsultationService : IConsultationService
{
    /// <summary>Matches the API: a just-completed consult can still accept diagnoses for a short window.</summary>
    public static readonly TimeSpan CompletionWindow = TimeSpan.FromMinutes(2);

    private readonly IDbContextFactory<ClinicAppDbContext> _factory;
    private readonly ISyncOutbox _outbox;
    private readonly IAuthService _auth;
    //JJQR - AI (Nov 21, 2026)
    private readonly ClinicNode.Core.Sync.ISyncBridge _sync;
    //JJQR - END

    public ConsultationService(IDbContextFactory<ClinicAppDbContext> factory, ISyncOutbox outbox, IAuthService auth, ClinicNode.Core.Sync.ISyncBridge? sync = null)
    {
        //JJQR - AI (Nov 21, 2026)
        _sync = sync ?? new ClinicNode.Core.Sync.NoOpSyncBridge();
        //JJQR - END
        _factory = factory;
        _outbox = outbox;
        _auth = auth;
    }

    public IReadOnlyList<string> MissingRequired(ConsultationDraft draft)
    {
        var missing = new List<string>();
        foreach (var i in ConsultSteps.EffectiveRequiredSections)
        {
            var ok = i switch
            {
                0 => ConsultSteps.ClinicalNotesSatisfied(draft.ChiefComplaint),
                1 => ConsultSteps.VitalsSatisfied(draft.Vitals.Select(v => (v.FormKey, v.Value))),
                2 => draft.Diagnoses.Count > 0,
                3 => ConsultSteps.PrescriptionSatisfied(draft.Prescriptions.Count, draft.NoMedsConfirmed),
                6 => ConsultSteps.FollowUpSatisfied(draft.FollowUpMode, draft.FollowUpDate),
                8 => ConsultSteps.FeeSatisfied(draft.PfDecision, draft.PfAmount, draft.PfWaiveReason),
                _ => true,
            };
            if (!ok) missing.Add(ConsultSteps.Sections[i]);
        }
        return missing;
    }

    public async Task<ConsultationWorkspace> LoadByBookingAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var booking = await db.Bookings
            .Include(b => b.Patient)
            .Include(b => b.Doctor)!.ThenInclude(d => d.StaffAccount)
            .SingleOrDefaultAsync(b => b.BookingId == bookingId, ct)
            ?? throw new InvalidOperationException("Booking not found.");

        var consult = await db.Consultations
            .Include(c => c.ConsultationDiagnoses)
            .Include(c => c.FollowUp)
            .SingleOrDefaultAsync(c => c.BookingId == bookingId, ct);

        var templates = await db.VitalFieldTemplates.Where(t => t.IsDefault).OrderBy(t => t.Description).ToListAsync(ct);
        var readings = await db.PatientVitalReadings.Where(v => v.BookingId == bookingId).ToListAsync(ct);

        var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct) ?? new ClinicSetting();
        var fee = FeeCalculator.Compute(settings, booking.VisitType, booking.DiscountCategory, booking.MedCertRequested);

        var draft = new ConsultationDraft
        {
            ConsultationId = consult?.ConsultationId,
            BookingId = booking.BookingId,
            PatientId = booking.PatientId,
            DoctorId = booking.DoctorId,
            Status = consult?.Status ?? ConsultationStatus.Draft,
            ChiefComplaint = consult?.ChiefComplaint,
            Subjective = consult?.Subjective,
            Objective = consult?.Objective,
            Assessment = consult?.Assessment,
            Plan = consult?.Plan,
            DoctorNotes = consult?.DoctorNotes,
            VisitType = booking.VisitType,
            DiscountCategory = booking.DiscountCategory,
            MedCertRequested = booking.MedCertRequested,
            PfDecision = consult?.PfDecision,
            PfAmount = consult?.PfAmount ?? (consult?.PfDecision == "Charge" ? fee.Total : null),
            PfWaiveReason = consult?.PfWaiveReason,
            CompletedAt = consult?.CompletedAt,
        };

        if (consult is not null)
        {
            draft.Diagnoses = consult.ConsultationDiagnoses.Select(d => new DiagnosisLine
            {
                Id = d.Id,
                Icd10Code = d.Icd10Code,
                CustomDescription = d.CustomDescription,
                Type = d.Type,
            }).ToList();

            var group = await db.PrescriptionGroups.Include(g => g.LineItems).FirstOrDefaultAsync(g => g.BookingId == bookingId, ct);
            if (group is not null)
            {
                draft.Prescriptions = group.LineItems.OrderBy(i => i.CreatedAt).Select(i => new RxLine
                {
                    Id = i.Id,
                    MedicineId = i.MedicineId,
                    GenericName = i.GenericName,
                    Dosage = i.Dosage,
                    Quantity = i.Quantity,
                    Instruction = i.Instruction,
                    Timing = i.Timing,
                    MealRelation = i.MealRelation,
                    DurationKind = i.DurationKind,
                    DurationValue = i.DurationValue,
                    Indication = i.Indication,
                    Route = i.Route,
                    AsNeeded = i.AsNeeded,
                    IsControlledSubstance = i.IsControlledSubstance,
                }).ToList();
            }

            var labs = await db.LabOrders.Where(l => l.ConsultationId == consult.ConsultationId).ToListAsync(ct);
            draft.LabOrders = labs.Select(l => new LabLine
            {
                LabOrderId = l.LabOrderId,
                LabTestId = l.LabTestId,
                TestName = l.TestName,
                Reason = l.Reason,
                ClinicalIndication = l.ClinicalIndication,
                SpecimenType = l.SpecimenType,
                Notes = l.Notes,
            }).ToList();

            var vax = await db.PatientVaccinations.Where(v => v.ConsultationId == consult.ConsultationId).ToListAsync(ct);
            draft.Vaccinations = vax.Select(v => new VaccinationLine
            {
                Id = v.Id,
                VaccineName = v.VaccineName,
                DoseNumber = v.DoseNumber,
                Notes = v.Notes,
            }).ToList();

            if (consult.FollowUp is { } fu)
            {
                var isPrn = (fu.Reason ?? "").StartsWith("PRN", StringComparison.OrdinalIgnoreCase)
                            || (fu.Instructions ?? "").StartsWith("PRN", StringComparison.OrdinalIgnoreCase);
                draft.FollowUpMode = isPrn ? FollowUpMode.Prn : FollowUpMode.Dated;
                draft.FollowUpDate = fu.FollowUpDate;
                draft.FollowUpReason = fu.Reason;
                draft.FollowUpInstructions = fu.Instructions;
                draft.FollowUpReminder = fu.ReminderEnabled;
            }
            else
            {
                draft.FollowUpMode = FollowUpMode.None;
            }

            var cert = await db.MedicalCertificates.SingleOrDefaultAsync(c => c.ConsultationId == consult.ConsultationId, ct);
            if (cert is not null)
            {
                draft.CertificateExists = true;
                draft.McAddress = cert.PatientAddressSnapshot;
                draft.McExaminedAt = cert.ExaminedAt;
                draft.McExamFrom = cert.ExaminationDateFrom;
                draft.McExamTo = cert.ExaminationDateTo;
                draft.McDiagnosis = cert.DiagnosisText;
                draft.McRecommendations = cert.Recommendations;
                draft.McPurposeException = cert.PurposeException;
                draft.McComeBackOn = cert.ComeBackOn;
            }
        }

        draft.Vitals = templates.Select(t => new VitalLine
        {
            TemplateId = t.TemplateId,
            FormKey = t.FormKey,
            Description = t.Description,
            Unit = t.Unit,
            Value = readings.FirstOrDefault(r => r.TemplateId == t.TemplateId)?.Value,
        }).ToList();

        var patient = booking.Patient;
        return new ConsultationWorkspace
        {
            Draft = draft,
            PatientName = $"{patient.FirstName} {patient.LastName}".Trim(),
            PatientCode = patient.PatientCode,
            PatientDateOfBirth = patient.DateOfBirth,
            PatientSex = patient.Sex.ToString(),
            DoctorName = booking.Doctor.StaffAccount?.FullName ?? "Doctor",
            AppointmentDate = booking.AppointmentDate,
            QueueNumber = booking.QueueNumber,
            IsWalkIn = booking.IsWalkIn,
            ComputedFee = fee.Total,
            FeeSummary = fee.Summary,
            VitalTemplates = draft.Vitals,
        };
    }

    public Task<ConsultSaveResult> SaveDraftAsync(ConsultationDraft draft, CancellationToken ct = default) =>
        PersistAsync(draft, complete: false, ct);

    // JJQR - AI (Nov 21, 2026) - amend: same required sections as Complete, status becomes Amended, money and booking untouched
    public async Task<ConsultSaveResult> AmendAsync(ConsultationDraft draft, CancellationToken ct = default)
    {
        var missing = MissingRequired(draft);
        if (missing.Count > 0)
            return ConsultSaveResult.Fail("Finish " + string.Join(", ", missing) + " to save the amendment.", missing);
        return await PersistAsync(draft, complete: false, ct, amend: true);
    }
    // JJQR - END

    public async Task<ConsultSaveResult> CompleteAsync(ConsultationDraft draft, CancellationToken ct = default)
    {
        var missing = MissingRequired(draft);
        if (missing.Count > 0)
            return ConsultSaveResult.Fail("Finish " + string.Join(", ", missing) + " to complete.", missing);
        return await PersistAsync(draft, complete: true, ct);
    }

    private async Task<ConsultSaveResult> PersistAsync(ConsultationDraft draft, bool complete, CancellationToken ct, bool amend = false)
    {
        if (!QueueLane.IsValidDiscount(draft.DiscountCategory))
            return ConsultSaveResult.Fail("Discount must be Senior or PWD.");
        if (string.Equals(draft.PfDecision, "Charge", StringComparison.OrdinalIgnoreCase)
            && draft.PfAmount is { } amt && !FeeCalculator.IsValidChargeAmount(amt))
            return ConsultSaveResult.Fail("Charge amount must be more than ₱0 and at most ₱100,000.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var booking = await db.Bookings.Include(b => b.Payment)
            .SingleOrDefaultAsync(b => b.BookingId == draft.BookingId, ct);
        if (booking is null) return ConsultSaveResult.Fail("Booking not found.");
        if (draft.PatientId != booking.PatientId || draft.DoctorId != booking.DoctorId)
            return ConsultSaveResult.Fail("Patient / doctor do not match the booking.");

        var session = _auth.Current;
        if (session?.IsDoctor == true && session.DoctorId is { } own && own != booking.DoctorId)
            return ConsultSaveResult.Fail("A doctor can only write their own visits.");

        var consult = await db.Consultations
            .Include(c => c.ConsultationDiagnoses)
            .Include(c => c.FollowUp)
            .SingleOrDefaultAsync(c => c.BookingId == draft.BookingId, ct);

        // JJQR - AI (Nov 21, 2026)
        if (amend && (consult is null || consult.Status == ConsultationStatus.Draft))
            return ConsultSaveResult.Fail("Only a completed consultation can be amended.");
        // JJQR - END
        if (!amend && consult is not null && consult.Status is ConsultationStatus.Completed or ConsultationStatus.Amended)
        {
            var sealedAlready = consult.Status == ConsultationStatus.Amended
                || consult.CompletedAt is { } done && DateTimeOffset.UtcNow - done > CompletionWindow;
            if (sealedAlready && complete is false && consult.Status == ConsultationStatus.Completed
                && DateTimeOffset.UtcNow - consult.CompletedAt!.Value > CompletionWindow)
            {
                return ConsultSaveResult.Fail("A completed consultation is immutable. Use amend (not yet in this node build) to change it.");
            }
            if (consult.Status == ConsultationStatus.Completed && complete)
                return ConsultSaveResult.Fail("This consultation is already completed.");
        }

        var now = DateTimeOffset.UtcNow;
        var userId = session?.UserId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (consult is null)
        {
            consult = new Consultation
            {
                ConsultationId = Guid.NewGuid(),
                BookingId = booking.BookingId,
                PatientId = booking.PatientId,
                DoctorId = booking.DoctorId,
                Status = ConsultationStatus.Draft,
                CreatedAt = now,
            };
            db.Consultations.Add(consult);
        }

        consult.ChiefComplaint = NullIfBlank(draft.ChiefComplaint);
        consult.Subjective = NullIfBlank(draft.Subjective);
        consult.Objective = NullIfBlank(draft.Objective);
        consult.Assessment = NullIfBlank(draft.Assessment);
        consult.Plan = NullIfBlank(draft.Plan);
        consult.DoctorNotes = NullIfBlank(draft.DoctorNotes);
        // JJQR - AI (Nov 21, 2026) - an amendment never changes the professional fee, visit type, discount or payment
        if (!amend)
        {
        consult.PfDecision = NullIfBlank(draft.PfDecision);
        consult.PfAmount = string.Equals(draft.PfDecision, "Charge", StringComparison.OrdinalIgnoreCase) ? draft.PfAmount : null;
        consult.PfWaiveReason = string.Equals(draft.PfDecision, "Waive", StringComparison.OrdinalIgnoreCase)
            ? NullIfBlank(draft.PfWaiveReason) : null;
        }
        else
        {
            draft.PfDecision = consult.PfDecision;
            draft.PfAmount = consult.PfAmount;
            draft.PfWaiveReason = consult.PfWaiveReason;
            draft.VisitType = booking.VisitType;
            draft.DiscountCategory = booking.DiscountCategory;
        }
        consult.UpdatedAt = now;
        if (amend) consult.Status = ConsultationStatus.Amended;

        if (!amend)
        {
        booking.VisitType = draft.VisitType;
        booking.DiscountCategory = QueueLane.NormalizeDiscount(draft.DiscountCategory);
        booking.MedCertRequested = draft.MedCertRequested;
        booking.UpdatedAt = now;

        var settings = await db.ClinicSettings.SingleOrDefaultAsync(s => s.Id == 1, ct) ?? new ClinicSetting();
        var fee = FeeCalculator.Compute(settings, booking.VisitType, booking.DiscountCategory, booking.MedCertRequested);
        if (string.Equals(consult.PfDecision, "Charge", StringComparison.OrdinalIgnoreCase) && consult.PfAmount is null)
            consult.PfAmount = fee.Total;
        booking.TotalFee = consult.PfDecision == "Waive" ? 0m : (consult.PfAmount ?? fee.Total);
        booking.AmountDue = booking.TotalFee;
        }
        // JJQR - END

        if (complete)
        {
            consult.Status = ConsultationStatus.Completed;
            consult.CompletedAt = now;
            consult.CompletedByUserId = userId;
            if (booking.Status != BookingStatus.Completed)
                booking.Status = BookingStatus.Completed;

            var payment = booking.Payment ?? await db.Payments.SingleOrDefaultAsync(p => p.BookingId == booking.BookingId, ct);
            if (payment is null)
            {
                payment = new Payment
                {
                    PaymentId = Guid.NewGuid(),
                    BookingId = booking.BookingId,
                    Amount = booking.AmountDue,
                    Status = PaymentStatus.Unpaid,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.Payments.Add(payment);
                booking.Payment = payment;
            }

            if (consult.PfDecision == "Waive")
            {
                if (payment.Status == PaymentStatus.Unpaid)
                {
                    payment.Status = PaymentStatus.Waived;
                    payment.WaivedReason = consult.PfWaiveReason;
                    payment.WaivedByUserId = userId;
                    payment.WaivedAt = now;
                    // Doctor-originated waiver waits for front-desk Confirm waiver (ConfirmedAt stays null).
                    payment.UpdatedAt = now;
                }
            }
            else if (consult.PfDecision == "Charge" && payment.Status == PaymentStatus.Unpaid)
            {
                payment.Amount = booking.AmountDue;
                payment.UpdatedAt = now;
            }
        }

        await ReplaceDiagnosesAsync(db, consult, draft.Diagnoses, now, ct);
        //JJQR - AI (Nov 21, 2026) - vitals are append-only on the cloud (§5.5). An amendment must not upsert them
        // (a changed value for an existing id would be rejected, and a new id for the same template would conflict).
        if (!amend)
            await UpsertVitalsAsync(db, booking, draft.Vitals, userId, ct);
        //JJQR - END
        await UpsertPrescriptionAsync(db, booking, draft, now, ct);
        await ReplaceLabsAsync(db, consult, draft.LabOrders, now, ct);
        await ReplaceVaccinationsAsync(db, consult, draft.Vaccinations, now, ct);
        await UpsertFollowUpAsync(db, consult, draft, now, ct);
        await UpsertCertificateAsync(db, consult, draft, userId, now, ct);

        draft.ConsultationId = consult.ConsultationId;
        draft.Status = consult.Status;
        draft.CompletedAt = consult.CompletedAt;

        //JJQR - AI (Nov 21, 2026) - v1.1: an amendment is an "update" op (the cloud knows no "amend" verb); the push-time snapshot carries status Amended and the stored base_row_version
        await EnqueueAsync(db, "consultation", consult.ConsultationId, complete ? "complete" : "update", draft, now, ct);
        //JJQR - END
        //JJQR - AI (Nov 21, 2026) - item 6: vital readings are append-only on the cloud (a changed value for an existing
        // id is rejected), so each reading is queued once, with its final value, when the consultation completes.
        // The booking (now Completed) and the payment created/waived at completion are queued too; the thin
        // outbox payload is replaced by a full snapshot at push time.
        if (complete)
        {
            foreach (var entry in db.ChangeTracker.Entries<PatientVitalReading>().Where(e => e.State != EntityState.Deleted && e.Entity.BookingId == booking.BookingId).ToList())
                await EnqueueAsync(db, "vital_reading", entry.Entity.Id, "create", new { id = entry.Entity.Id, bookingId = booking.BookingId }, now, ct);
            await EnqueueAsync(db, "booking", booking.BookingId, "status", new { bookingId = booking.BookingId, status = booking.Status.ToString() }, now, ct);
            var pay = db.ChangeTracker.Entries<Payment>().Select(e => e.Entity).FirstOrDefault(p => p.BookingId == booking.BookingId);
            if (pay is not null)
                await EnqueueAsync(db, "payment", pay.PaymentId, "upsert", new { paymentId = pay.PaymentId, bookingId = booking.BookingId }, now, ct);
        }
        //JJQR - END
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        //JJQR - AI (Nov 21, 2026)
        _sync.RequestSync();
        //JJQR - END
        return ConsultSaveResult.Ok(draft);
    }

    private static async Task ReplaceDiagnosesAsync(ClinicAppDbContext db, Consultation consult, List<DiagnosisLine> lines, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.ConsultationDiagnoses.Where(d => d.ConsultationId == consult.ConsultationId).ToListAsync(ct);
        db.ConsultationDiagnoses.RemoveRange(existing);
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Icd10Code) || !string.IsNullOrWhiteSpace(l.CustomDescription)))
        {
            db.ConsultationDiagnoses.Add(new ConsultationDiagnosis
            {
                Id = line.Id ?? Guid.NewGuid(),
                ConsultationId = consult.ConsultationId,
                Icd10Code = NullIfBlank(line.Icd10Code),
                CustomDescription = NullIfBlank(line.CustomDescription),
                Type = line.Type,
                CreatedAt = now,
            });
        }
    }

    private static async Task UpsertVitalsAsync(ClinicAppDbContext db, Booking booking, List<VitalLine> lines, Guid? userId, CancellationToken ct)
    {
        var existing = await db.PatientVitalReadings.Where(v => v.BookingId == booking.BookingId).ToListAsync(ct);
        var today = ClinicClock.Today;
        foreach (var line in lines)
        {
            var row = existing.FirstOrDefault(v => v.TemplateId == line.TemplateId);
            if (string.IsNullOrWhiteSpace(line.Value))
            {
                if (row is not null) db.PatientVitalReadings.Remove(row);
                continue;
            }
            if (row is null)
            {
                row = new PatientVitalReading
                {
                    Id = Guid.NewGuid(),
                    BookingId = booking.BookingId,
                    PatientId = booking.PatientId,
                    TemplateId = line.TemplateId,
                    RecordedAt = today,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.PatientVitalReadings.Add(row);
            }
            row.Value = line.Value.Trim();
            row.RecordedByUserId = userId;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }

    private static async Task UpsertPrescriptionAsync(ClinicAppDbContext db, Booking booking, ConsultationDraft draft, DateTimeOffset now, CancellationToken ct)
    {
        var group = await db.PrescriptionGroups.Include(g => g.LineItems).FirstOrDefaultAsync(g => g.BookingId == booking.BookingId, ct);
        if (draft.NoMedsConfirmed || draft.Prescriptions.Count == 0)
        {
            if (group is not null)
            {
                db.PrescriptionLineItems.RemoveRange(group.LineItems);
                if (draft.NoMedsConfirmed) { /* keep an empty group so "no meds" is explicit; drop lines only */ }
                else
                {
                    db.PrescriptionGroups.Remove(group);
                }
            }
            return;
        }

        if (group is null)
        {
            group = new PrescriptionGroup
            {
                GroupId = Guid.NewGuid(),
                BookingId = booking.BookingId,
                PatientId = booking.PatientId,
                DoctorId = booking.DoctorId,
                CreatedAt = now,
            };
            db.PrescriptionGroups.Add(group);
        }
        db.PrescriptionLineItems.RemoveRange(group.LineItems.ToList());
        // JJQR - AI (Nov 21, 2026) - unique CreatedAt so reload/print order matches entry order
        var rxIndex = 0;
        foreach (var line in draft.Prescriptions.Where(l => !string.IsNullOrWhiteSpace(l.GenericName)))
        {
            var medicineId = line.MedicineId ?? await FindOrCreateMedicineAsync(db, line.GenericName, ct);
            db.PrescriptionLineItems.Add(new PrescriptionLineItem
            {
                Id = line.Id ?? Guid.NewGuid(),
                GroupId = group.GroupId,
                MedicineId = medicineId,
                GenericName = line.GenericName.Trim(),
                Dosage = line.Dosage?.Trim() ?? "",
                Quantity = line.Quantity?.Trim() ?? "",
                Instruction = NullIfBlank(line.Instruction),
                Timing = NullIfBlank(line.Timing),
                MealRelation = NullIfBlank(line.MealRelation),
                DurationKind = NullIfBlank(line.DurationKind),
                DurationValue = line.DurationValue,
                Indication = NullIfBlank(line.Indication),
                Route = NullIfBlank(line.Route),
                AsNeeded = line.AsNeeded,
                IsControlledSubstance = line.IsControlledSubstance,
                CreatedAt = now.AddTicks(rxIndex++),
            });
        }
        group.UpdatedAt = now;
    }

    private static async Task<Guid> FindOrCreateMedicineAsync(ClinicAppDbContext db, string genericName, CancellationToken ct)
    {
        var name = genericName.Trim();
        var existing = await db.Medicines.FirstOrDefaultAsync(m => m.GenericName.ToLower() == name.ToLower(), ct);
        if (existing is not null) return existing.MedicineId;
        var created = new Medicine { MedicineId = Guid.NewGuid(), GenericName = name, CreatedAt = DateTimeOffset.UtcNow };
        db.Medicines.Add(created);
        return created.MedicineId;
    }

    private static async Task ReplaceLabsAsync(ClinicAppDbContext db, Consultation consult, List<LabLine> lines, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.LabOrders.Where(l => l.ConsultationId == consult.ConsultationId).ToListAsync(ct);
        db.LabOrders.RemoveRange(existing);
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.TestName)))
        {
            db.LabOrders.Add(new LabOrder
            {
                LabOrderId = line.LabOrderId ?? Guid.NewGuid(),
                ConsultationId = consult.ConsultationId,
                PatientId = consult.PatientId,
                DoctorId = consult.DoctorId,
                LabTestId = line.LabTestId,
                TestName = line.TestName.Trim(),
                Reason = NullIfBlank(line.Reason),
                ClinicalIndication = NullIfBlank(line.ClinicalIndication),
                SpecimenType = NullIfBlank(line.SpecimenType),
                Notes = NullIfBlank(line.Notes),
                Status = LabOrderStatus.Requested,
                RequestedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }

    private static async Task ReplaceVaccinationsAsync(ClinicAppDbContext db, Consultation consult, List<VaccinationLine> lines, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.PatientVaccinations.Where(v => v.ConsultationId == consult.ConsultationId).ToListAsync(ct);
        db.PatientVaccinations.RemoveRange(existing);
        foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.VaccineName)))
        {
            db.PatientVaccinations.Add(new PatientVaccination
            {
                Id = line.Id ?? Guid.NewGuid(),
                PatientId = consult.PatientId,
                ConsultationId = consult.ConsultationId,
                VaccineName = line.VaccineName.Trim(),
                DoseNumber = line.DoseNumber,
                Notes = NullIfBlank(line.Notes),
                Status = VaccinationStatus.Administered,
                Source = VaccinationSource.AdministeredInClinic,
                AdministeredDate = ClinicClock.Today,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
    }

    private static async Task UpsertFollowUpAsync(ClinicAppDbContext db, Consultation consult, ConsultationDraft draft, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.FollowUps.SingleOrDefaultAsync(f => f.ConsultationId == consult.ConsultationId, ct);
        if (draft.FollowUpMode == FollowUpMode.None)
        {
            if (existing is not null) db.FollowUps.Remove(existing);
            return;
        }

        var date = draft.FollowUpMode == FollowUpMode.Prn
            ? ClinicClock.Today
            : draft.FollowUpDate ?? ClinicClock.Today;
        var reason = draft.FollowUpMode == FollowUpMode.Prn
            ? (string.IsNullOrWhiteSpace(draft.FollowUpReason) ? "PRN / return if needed" : draft.FollowUpReason)
            : draft.FollowUpReason;
        var instructions = draft.FollowUpMode == FollowUpMode.Prn
            ? (string.IsNullOrWhiteSpace(draft.FollowUpInstructions) ? "PRN — return if symptoms recur" : draft.FollowUpInstructions)
            : draft.FollowUpInstructions;

        if (existing is null)
        {
            existing = new FollowUp
            {
                Id = Guid.NewGuid(),
                ConsultationId = consult.ConsultationId,
                PatientId = consult.PatientId,
                DoctorId = consult.DoctorId,
                CreatedAt = now,
            };
            db.FollowUps.Add(existing);
        }
        existing.FollowUpDate = date;
        existing.Reason = NullIfBlank(reason);
        existing.Instructions = NullIfBlank(instructions);
        existing.ReminderEnabled = draft.FollowUpReminder;
        existing.Status = FollowUpStatus.Pending;
        existing.UpdatedAt = now;
    }

    private static async Task UpsertCertificateAsync(ClinicAppDbContext db, Consultation consult, ConsultationDraft draft, Guid? userId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.MedicalCertificates.SingleOrDefaultAsync(c => c.ConsultationId == consult.ConsultationId, ct);
        if (!draft.CertificateExists)
        {
            if (existing is not null && string.IsNullOrWhiteSpace(draft.McDiagnosis) && string.IsNullOrWhiteSpace(draft.McRecommendations))
                db.MedicalCertificates.Remove(existing);
            return;
        }

        if (existing is null)
        {
            existing = new MedicalCertificate
            {
                CertificateId = Guid.NewGuid(),
                ConsultationId = consult.ConsultationId,
                PatientId = consult.PatientId,
                DoctorId = consult.DoctorId,
                IssueDate = ClinicClock.Today,
                CreatedAt = now,
            };
            db.MedicalCertificates.Add(existing);
        }
        existing.PatientAddressSnapshot = NullIfBlank(draft.McAddress);
        existing.ExaminedAt = NullIfBlank(draft.McExaminedAt);
        existing.ExaminationDateFrom = draft.McExamFrom;
        existing.ExaminationDateTo = draft.McExamTo;
        existing.DiagnosisText = NullIfBlank(draft.McDiagnosis);
        existing.Recommendations = NullIfBlank(draft.McRecommendations);
        existing.PurposeException = NullIfBlank(draft.McPurposeException);
        existing.ComeBackOn = draft.McComeBackOn;
        existing.IssuedByUserId = userId;
        existing.UpdatedAt = now;
        draft.CertificateExists = true;
    }

    private async Task EnqueueAsync(ClinicAppDbContext db, string aggregate, Guid entityId, string op, object payload, DateTimeOffset now, CancellationToken ct)
    {
        await _outbox.EnqueueAsync(db, new SyncOutboxEntry
        {
            OperationId = Guid.NewGuid(),
            Aggregate = aggregate,
            EntityId = entityId,
            OperationType = op,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAt = now,
        }, ct);
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
// JJQR - END
