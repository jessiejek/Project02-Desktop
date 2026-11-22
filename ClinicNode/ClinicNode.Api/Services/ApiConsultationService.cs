//JJQR - AI (21 Nov 2026) - Api implementation of IConsultationService, composed from the SAME endpoints the web /doctor/consultation2 page uses:
//   GET  /api/bookings/{id} · GET /api/consultations/by-booking/{id} · GET /api/vital-field-templates · GET /api/vitals?bookingId= · GET /api/settings
//   GET  /api/prescription-groups?bookingId= · GET /api/lab-orders/by-booking/{id} · GET /api/patient-vaccinations/by-consultation/{cid}
//   GET  /api/follow-ups?consultationId= · GET /api/medical-certificates/by-consultation/{cid}
//   PUT  /api/consultations/by-booking/{id} · PUT /api/consultations/{cid}/diagnoses · PUT /api/vitals/by-booking/{id}
//   PUT  /api/prescription-groups/by-booking/{id} (DELETE /api/prescription-groups/{gid} when emptied) · PUT|DELETE /api/follow-ups/by-consultation/{cid}
//   PUT  /api/lab-orders/by-consultation/{cid} · PUT /api/patient-vaccinations/by-consultation/{cid} · PUT|DELETE /api/medical-certificates/by-consultation/{cid}
//   PUT  /api/queue/{bookingId}/complete (after Completed, like the web)
// The server does the fee maths and the doctor-Waive → payment Waived on Completed/Amended. Only a TYPED Charge amount is sent (web pfAmountEdited).
// Real server answers (400/403/404/409) come back as ConsultSaveResult.Fail; network errors throw so the router falls back to the local service.
using ClinicApp.Domain;
using ClinicApp.Domain.Entities;
using ClinicApp.Domain.Enums;
using ClinicNode.Api.Http;
using ClinicNode.Core;
using ClinicNode.Core.Clinical;
using ClinicNode.Core.Doctor;

namespace ClinicNode.Api.Services;

public sealed class ApiConsultationService(IApiClient api) : IConsultationService
{
    private static readonly Guid NilMedicineId = Guid.Empty; // web NIL_MEDICINE_ID for a typed (non-catalogue) medicine

    public IReadOnlyList<string> MissingRequired(ConsultationDraft draft)
    {
        decimal? shown = draft.PfDecision == "Charge" && draft.PfAmount is null && !draft.FeeState().PfAmountEdited ? 1m : draft.PfAmount;
        return ConsultRules.MissingRequired(draft, shown).Select(i => ConsultSteps.Sections[i]).ToList();
    }

    private async Task<T?> TryGetAsync<T>(string path, IReadOnlyDictionary<string, string?>? query, CancellationToken ct) where T : class
    {
        try { return await api.GetAsync<T>(path, query, ct); }
        catch (ApiNotFoundException) { return null; }
    }

    private async Task TryDeleteAsync(string path, CancellationToken ct)
    {
        try { await api.SendAsync(HttpMethod.Delete, path, null, null, ct); }
        catch (ApiNotFoundException) { /* already gone: the goal state */ }
    }

    public async Task<ConsultationWorkspace> LoadByBookingAsync(Guid bookingId, CancellationToken ct = default)
    {
        VisitVersions.Begin(bookingId); //JJQR - AI (Nov 22, 2026) - opening a visit starts a fresh stale-form session; the reads below record the version this screen is based on //JJQR - END
        //JJQR - AI (Nov 22, 2026) - owner: slow open. Round 1: the five independent reads go out AT THE SAME TIME; round 2 (below) does the same for what belongs to the consultation.
        var tBooking = api.GetAsync<Booking>($"/api/bookings/{bookingId:D}", null, ct);
        var tConsult = TryGetAsync<Consultation>($"/api/consultations/by-booking/{bookingId:D}", null, ct);
        var tTemplates = api.GetAsync<List<VitalFieldTemplate>>("/api/vital-field-templates", null, ct);
        var tReadings = api.GetAsync<List<PatientVitalReading>>("/api/vitals", new Dictionary<string, string?> { ["bookingId"] = bookingId.ToString("D") }, ct);
        var tSettings = TryGetAsync<ClinicSetting>("/api/settings", null, ct);
        var booking = await tBooking ?? throw new InvalidOperationException("Booking not found.");
        var consult = await tConsult;
        var templates = (await tTemplates ?? [])
            .Where(t => t.IsDefault).OrderBy(t => t.Description).ToList();
        var readings = await tReadings ?? [];
        var settings = await tSettings ?? new ClinicSetting();
        //JJQR - END
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
            PfAmount = consult?.PfAmount,
            PfWaiveReason = consult?.PfWaiveReason,
            CompletedAt = consult?.CompletedAt,
        };
        draft.FeeState().PfAmountEdited = consult?.PfDecision == "Charge" && consult.PfAmount is not null; // web: a stored amount is the doctor's own

        if (consult is not null)
        {
            var cid = consult.ConsultationId;
            //JJQR - AI (Nov 22, 2026) - round 2, all at once
            var tGroups = api.GetAsync<List<PrescriptionGroup>>("/api/prescription-groups", new Dictionary<string, string?> { ["bookingId"] = bookingId.ToString("D") }, ct);
            var tLabs = TryGetAsync<List<LabOrder>>($"/api/lab-orders/by-booking/{bookingId:D}", null, ct);
            var tVax = TryGetAsync<List<PatientVaccination>>($"/api/patient-vaccinations/by-consultation/{cid:D}", null, ct);
            var tFu = consult.FollowUp is null ? api.GetAsync<List<FollowUp>>("/api/follow-ups", new Dictionary<string, string?> { ["consultationId"] = cid.ToString("D") }, ct) : null;
            var tCert = TryGetAsync<MedicalCertificate>($"/api/medical-certificates/by-consultation/{cid:D}", null, ct);
            //JJQR - END
            draft.Diagnoses = consult.ConsultationDiagnoses.Select(d => new DiagnosisLine { Id = d.Id, Icd10Code = d.Icd10Code, CustomDescription = d.CustomDescription, Type = d.Type }).ToList();

            var groups = await tGroups ?? [];
            if (groups.FirstOrDefault() is { } g)
                draft.Prescriptions = g.LineItems.OrderBy(i => i.CreatedAt).Select(i => new RxLine
                {
                    Id = i.Id, MedicineId = i.MedicineId == Guid.Empty ? null : i.MedicineId, GenericName = i.GenericName, Dosage = i.Dosage, Quantity = i.Quantity,
                    Instruction = i.Instruction, Timing = i.Timing, MealRelation = i.MealRelation, DurationKind = i.DurationKind, DurationValue = i.DurationValue,
                    Indication = i.Indication, Route = i.Route, AsNeeded = i.AsNeeded, IsControlledSubstance = i.IsControlledSubstance,
                }).ToList();

            var labs = await tLabs ?? [];
            draft.LabOrders = labs.Where(l => l.ConsultationId == cid).Select(l => new LabLine
            {
                LabOrderId = l.LabOrderId, LabTestId = l.LabTestId, TestName = l.TestName, Reason = l.Reason,
                ClinicalIndication = l.ClinicalIndication, SpecimenType = l.SpecimenType, Notes = l.Notes,
            }).ToList();

            var vax = await tVax ?? [];
            draft.Vaccinations = vax.Select(v =>
            {
                var line = new VaccinationLine { Id = v.Id, VaccineName = v.VaccineName, DoseNumber = v.DoseNumber, Notes = v.Notes };
                var x = line.Details();
                x.Manufacturer = v.Manufacturer; x.Route = v.Route; x.Site = v.Site; x.LotNumber = v.LotNumber; x.ExpiryDate = v.ExpiryDate;
                VaccineExtras[v.Id] = new VaccineExtra(v.ReactionNotes, v.AdministeredDate, v.Status, v.NextDoseDate); //JJQR - AI (21 Nov 2026) - TASK VACCINE OUTBOX FIELDS (+ next_dose_date kept) //JJQR - END
                return line;
            }).ToList();

            var fu = consult.FollowUp ?? (tFu is null ? null : (await tFu ?? []).FirstOrDefault());
            if (fu is not null)
            {
                draft.FollowUpMode = ConsultRules.IsPrnFollowUp(fu.Reason, fu.Instructions) ? FollowUpMode.Prn : FollowUpMode.Dated;
                draft.FollowUpDate = fu.FollowUpDate;
                draft.FollowUpReason = fu.Reason;
                draft.FollowUpInstructions = fu.Instructions;
                draft.FollowUpReminder = fu.ReminderEnabled;
            }
            else draft.FollowUpMode = FollowUpMode.None;

            var cert = await tCert;
            if (cert is not null)
            {
                draft.CertificateExists = true;
                draft.FeeState().CertificateOnServer = true;
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
            TemplateId = t.TemplateId, FormKey = t.FormKey, Description = t.Description, Unit = t.Unit,
            Value = readings.FirstOrDefault(r => r.TemplateId == t.TemplateId)?.Value,
        }).ToList();

        var p = booking.Patient;
        return new ConsultationWorkspace
        {
            Draft = draft,
            PatientName = p is null ? "Patient" : $"{p.FirstName} {p.LastName}".Trim(),
            PatientCode = p?.PatientCode ?? "",
            PatientDateOfBirth = p?.DateOfBirth,
            PatientSex = p?.Sex.ToString(),
            DoctorName = booking.Doctor?.StaffAccount?.FullName ?? "Doctor",
            AppointmentDate = booking.AppointmentDate,
            QueueNumber = booking.QueueNumber,
            IsWalkIn = booking.IsWalkIn,
            ComputedFee = fee.Total,
            FeeSummary = fee.Summary,
            VitalTemplates = draft.Vitals,
        };
    }

    public Task<ConsultSaveResult> SaveDraftAsync(ConsultationDraft draft, CancellationToken ct = default) => PersistAsync(draft, ConsultationStatus.Draft, ct);

    public Task<ConsultSaveResult> CompleteAsync(ConsultationDraft draft, CancellationToken ct = default)
    {
        var missing = MissingRequired(draft);
        return missing.Count > 0
            ? Task.FromResult(ConsultSaveResult.Fail("Finish " + string.Join(", ", missing) + " to complete.", missing))
            : PersistAsync(draft, ConsultationStatus.Completed, ct);
    }

    public Task<ConsultSaveResult> AmendAsync(ConsultationDraft draft, CancellationToken ct = default)
    {
        var missing = MissingRequired(draft);
        return missing.Count > 0
            ? Task.FromResult(ConsultSaveResult.Fail("Finish " + string.Join(", ", missing) + " to save the amendment.", missing))
            : PersistAsync(draft, ConsultationStatus.Amended, ct);
    }

    //JJQR - AI (21 Nov 2026) - TASK VACCINE OUTBOX FIELDS: per-vaccination values the draft model has no slot for (Core VaccinationLine is not mine)
    //JJQR - AI (21 Nov 2026) - + NextDoseDate: the PUT is replace-all, so sending null wiped a saved next-dose date
    private sealed record VaccineExtra(string? ReactionNotes, DateOnly? AdministeredDate, ClinicApp.Domain.Enums.VaccinationStatus? Status, DateOnly? NextDoseDate = null);
    //JJQR - END
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, VaccineExtra> VaccineExtras = new();
    //JJQR - END

    private static string? Nb(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task<ConsultSaveResult> PersistAsync(ConsultationDraft d, ConsultationStatus status, CancellationToken ct)
    {
        try
        {
            var bid = d.BookingId;
            var typedAmount = d.PfDecision == "Charge" && d.FeeState().PfAmountEdited ? d.PfAmount : null;
            var saved = await api.SendAsync<Consultation>(HttpMethod.Put, $"/api/consultations/by-booking/{bid:D}", new
            {
                PatientId = d.PatientId,
                DoctorId = d.DoctorId,
                Status = status,
                ChiefComplaint = Nb(d.ChiefComplaint),
                Subjective = Nb(d.Subjective),
                Objective = Nb(d.Objective),
                Assessment = Nb(d.Assessment),
                Plan = Nb(d.Plan),
                DoctorNotes = Nb(d.DoctorNotes),
                VisitType = d.VisitType,
                MedCertRequested = d.MedCertRequested,
                DiscountCategory = Nb(d.DiscountCategory),
                PfDecision = d.PfDecision is "Charge" or "Waive" ? d.PfDecision : null,
                PfAmount = typedAmount,
                PfWaiveReason = d.PfDecision == "Waive" ? Nb(d.PfWaiveReason) : null,
                ConsultationId = d.ConsultationId,
            }, null, ct) ?? throw new ApiServerException(500, "The server did not return the saved consultation.");
            var cid = saved.ConsultationId;

            //JJQR - AI (Nov 22, 2026) - owner: "Save & next" was very slow. After the consultation row exists, the diagnoses, vitals, prescription, follow-up, labs, vaccinations and certificate are independent of each other,
            // so they are sent AT THE SAME TIME (one round trip of waiting instead of about eight). The queue completion still goes last, after all of them succeeded.
            var parts = new List<Task>();
            parts.Add(api.SendAsync(HttpMethod.Put, $"/api/consultations/{cid:D}/diagnoses",
                d.Diagnoses.Where(x => !string.IsNullOrWhiteSpace(x.Icd10Code) || !string.IsNullOrWhiteSpace(x.CustomDescription))
                    .Select(x => new { Icd10Code = Nb(x.Icd10Code), CustomDescription = Nb(x.CustomDescription), Type = x.Type, Id = x.Id }).ToList(), null, ct));

            // Vitals are append-only once the visit is finalised (§5.5): never re-sent from an amendment.
            if (status != ConsultationStatus.Amended)
            {
                var vitals = d.Vitals.Where(v => !string.IsNullOrWhiteSpace(v.Value)).Select(v => new { TemplateId = v.TemplateId, Value = v.Value!.Trim() }).ToList();
                if (vitals.Count > 0) parts.Add(api.SendAsync(HttpMethod.Put, $"/api/vitals/by-booking/{bid:D}", vitals, null, ct));
            }

            var items = d.Prescriptions.Where(r => !string.IsNullOrWhiteSpace(r.GenericName)).Select(r => new
            {
                MedicineId = r.MedicineId ?? NilMedicineId, GenericName = r.GenericName.Trim(), Dosage = r.Dosage ?? "", Quantity = r.Quantity ?? "",
                Instruction = Nb(r.Instruction), IsControlledSubstance = r.IsControlledSubstance, Timing = Nb(r.Timing), MealRelation = Nb(r.MealRelation),
                DurationKind = Nb(r.DurationKind), DurationValue = r.DurationValue, Indication = Nb(r.Indication), Route = Nb(r.Route), AsNeeded = r.AsNeeded, Id = r.Id,
            }).ToList();
            if (items.Count > 0)
                parts.Add(api.SendAsync(HttpMethod.Put, $"/api/prescription-groups/by-booking/{bid:D}", new { PatientId = d.PatientId, DoctorId = d.DoctorId, BookingId = bid, Items = items }, null, ct));
            else
                parts.Add(Task.Run(async () =>
                {
                    var groups = await api.GetAsync<List<PrescriptionGroup>>("/api/prescription-groups", new Dictionary<string, string?> { ["bookingId"] = bid.ToString("D") }, ct) ?? [];
                    foreach (var g in groups) await TryDeleteAsync($"/api/prescription-groups/{g.GroupId:D}", ct);
                }));

            if (d.FollowUpMode == FollowUpMode.None) parts.Add(TryDeleteAsync($"/api/follow-ups/by-consultation/{cid:D}", ct));
            else
            {
                var prn = d.FollowUpMode == FollowUpMode.Prn;
                var date = prn ? d.FollowUpDate ?? ClinicClock.Today : d.FollowUpDate;
                if (date is { } fd)
                    parts.Add(api.SendAsync(HttpMethod.Put, $"/api/follow-ups/by-consultation/{cid:D}", new
                    {
                        PatientId = d.PatientId, DoctorId = d.DoctorId, FollowUpDate = fd,
                        Reason = Nb(d.FollowUpReason) ?? (prn ? ConsultRules.PrnReason : null),
                        Instructions = Nb(d.FollowUpInstructions) ?? (prn ? ConsultRules.PrnInstructions : null),
                        ReminderEnabled = !prn && d.FollowUpReminder,
                        Status = FollowUpStatus.Pending,
                    }, null, ct));
                else parts.Add(TryDeleteAsync($"/api/follow-ups/by-consultation/{cid:D}", ct));
            }

            parts.Add(api.SendAsync(HttpMethod.Put, $"/api/lab-orders/by-consultation/{cid:D}",
                d.LabOrders.Where(l => !string.IsNullOrWhiteSpace(l.TestName)).Select(l => new
                {
                    LabTestId = l.LabTestId, TestName = l.TestName.Trim(), ClinicalIndication = Nb(l.ClinicalIndication) ?? Nb(l.Reason),
                    SpecimenType = Nb(l.SpecimenType), Notes = Nb(l.Notes), LabOrderId = l.LabOrderId,
                }).ToList(), null, ct));

            parts.Add(api.SendAsync(HttpMethod.Put, $"/api/patient-vaccinations/by-consultation/{cid:D}",
                d.Vaccinations.Where(v => !string.IsNullOrWhiteSpace(v.VaccineName)).Select(v =>
                {
                    var x = v.Details();
                    //JJQR - AI (21 Nov 2026) - TASK VACCINE OUTBOX FIELDS: the PUT now also carries reaction_notes, administered_date and status
                    // (real optional VaccinationInput fields). Values are the row's own (loaded with the draft); a new line sends null = server default
                    // (administered_date today, status Administered). No invented columns.
                    var extra = v.Id is { } vid && VaccineExtras.TryGetValue(vid, out var e) ? e : null;
                    //JJQR - AI (21 Nov 2026) - a new staged line gets its client id now (backend accepts it: ClientIds.FreshAsync), so later saves,
                    // the write-through and the extras lookup all see the same row instead of a fresh server id each time
                    v.Id ??= Guid.NewGuid();
                    //JJQR - END
                    return new
                    {
                        VaccineName = v.VaccineName.Trim(), Manufacturer = Nb(x.Manufacturer), DoseNumber = v.DoseNumber, Route = Nb(x.Route), Site = Nb(x.Site),
                        LotNumber = Nb(x.LotNumber), ExpiryDate = x.ExpiryDate, NextDoseDate = extra?.NextDoseDate /*JJQR - AI (21 Nov 2026) was always null - JJQR - END*/, Notes = Nb(v.Notes), Id = v.Id,
                        ReactionNotes = Nb(extra?.ReactionNotes), AdministeredDate = extra?.AdministeredDate, Status = extra?.Status,
                    };
                    //JJQR - END
                }).ToList(), null, ct));

            if (d.CertificateExists)
            {
                var today = ClinicClock.Today;
                var from = d.McExamFrom ?? today;
                parts.Add(Task.Run(async () =>
                {
                await api.SendAsync(HttpMethod.Put, $"/api/medical-certificates/by-consultation/{cid:D}", new
                {
                    PatientId = d.PatientId, DoctorId = d.DoctorId, IssueDate = today,
                    PatientAddressSnapshot = Nb(d.McAddress),
                    ExaminedAt = Nb(d.McExaminedAt) ?? ConsultRules.McExaminedAtDefault,
                    ExaminationDateFrom = from, ExaminationDateTo = d.McExamTo ?? from,
                    DiagnosisText = Nb(d.McDiagnosis), Recommendations = Nb(d.McRecommendations) ?? Nb(d.Plan),
                    PurposeException = Nb(d.McPurposeException), ComeBackOn = d.McComeBackOn ?? (d.FollowUpMode == FollowUpMode.Dated ? d.FollowUpDate : null),
                }, null, ct);
                d.FeeState().CertificateOnServer = true;
                }));
            }
            else if (d.FeeState().CertificateOnServer)
            {
                parts.Add(Task.Run(async () =>
                {
                    await TryDeleteAsync($"/api/medical-certificates/by-consultation/{cid:D}", ct);
                    d.FeeState().CertificateOnServer = false;
                }));
            }

            await Task.WhenAll(parts);
            //JJQR - END

            if (status == ConsultationStatus.Completed)
                await api.SendAsync(HttpMethod.Put, $"/api/queue/{bid:D}/complete", new { }, null, ct);

            d.ConsultationId = cid;
            d.Status = saved.Status;
            d.CompletedAt = saved.CompletedAt;
            if (saved.PfDecision == "Charge" && saved.PfAmount is not null) { d.PfAmount = saved.PfAmount; d.FeeState().PfAmountEdited = true; }
            return ConsultSaveResult.Ok(d);
        }
        catch (ApiValidationException ex) { return ConsultSaveResult.Fail(ex.Message); }
        catch (ApiForbiddenException ex) { return ConsultSaveResult.Fail(ex.Message); }
        catch (ApiNotFoundException ex) { return ConsultSaveResult.Fail(ex.Message); }
        catch (ApiConflictException ex) { return ConsultSaveResult.Fail(ex.Message); }
    }
}
//JJQR - END
