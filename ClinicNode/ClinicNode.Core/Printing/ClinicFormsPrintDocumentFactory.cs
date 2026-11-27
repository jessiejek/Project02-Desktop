//JJQR - AI (Nov 21, 2026) - IPrintDocumentFactory that prints the WEB forms (letterhead, A5 pad grid, lab checklist) instead of the
// simplified layouts in Core/Print/PrintDocumentFactory. Same data rules as Project02-fe consult-print.ts / consultation page.
// Register it AFTER AddClinicNodeCore (last registration wins) — see packaging/INTEGRATION.md.
using ClinicApp.Infrastructure;
using ClinicNode.Core.Print;
using ClinicApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Printing;

public sealed class ClinicFormsPrintDocumentFactory : IPrintDocumentFactory
{
    readonly IDbContextFactory<ClinicAppDbContext> _factory;
    readonly PrintingOptions _options;
    readonly TimeProvider _clock;
    readonly ClinicNode.Core.Staff.IStaffPaymentsService? _payments;
    readonly IClinicPrintPolicy? _policy; //JJQR - AI (Nov 24, 2026) - the Admin's list of lab tests //JJQR - END
    readonly IPtrPrintChoice? _ptr; //JJQR - AI (Nov 24, 2026) - "Include PTR in print" (off unless the doctor ticked it for this visit and document) //JJQR - END

    public ClinicFormsPrintDocumentFactory(IDbContextFactory<ClinicAppDbContext> factory, PrintingOptions? options = null, TimeProvider? clock = null,
        ClinicNode.Core.Staff.IStaffPaymentsService? payments = null, IClinicPrintPolicy? policy = null, IPtrPrintChoice? ptr = null)
    {
        _policy = policy;
        _ptr = ptr;
        _factory = factory;
        _payments = payments;
        _options = options ?? new PrintingOptions();
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The PTR number prints only when the doctor ticked "Include PTR in print" for this visit and document.</summary>
    PrintDoctor PtrFor(Guid bookingId, ClinicPrintKind kind, PrintDoctor doctor) =>
        _ptr?.IsOn(bookingId, kind) == true ? doctor : doctor with { PtrNumber = null };

    //JJQR - AI (Nov 25, 2026) - code review: print one chosen prescription group of the visit (the chart's per-group "Print prescription" and Reprint)
    public Task<PrintDocument> PrescriptionGroupAsync(Guid bookingId, Guid groupId, CancellationToken ct = default) => PrescriptionCoreAsync(bookingId, groupId, ct);

    public Task<PrintDocument> PrescriptionAsync(Guid bookingId, CancellationToken ct = default) => PrescriptionCoreAsync(bookingId, null, ct);

    private async Task<PrintDocument> PrescriptionCoreAsync(Guid bookingId, Guid? groupId, CancellationToken ct)
    //JJQR - END
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await LoadBookingAsync(db, bookingId, ct);
        // SQLite cannot ORDER BY DateTimeOffset server-side, so newest-first is applied in memory.
        var group = (await db.PrescriptionGroups.AsNoTracking().Include(g => g.LineItems)
                .Where(g => g.BookingId == bookingId && (groupId == null || g.GroupId == groupId)).ToListAsync(ct)) //JJQR - AI (Nov 25, 2026) - + chosen group //JJQR - END
            .OrderByDescending(g => g.CreatedAt)
            .FirstOrDefault();
        var items = group?.LineItems.OrderBy(i => i.CreatedAt).Select(i => new RxLine
        {
            GenericName = i.GenericName,
            Dosage = i.Dosage,
            Quantity = i.Quantity,
            Instruction = i.Instruction,
            Timing = i.Timing,
            MealRelation = i.MealRelation,
            AsNeeded = i.AsNeeded,
            DurationKind = i.DurationKind,
            DurationValue = i.DurationValue,
            Indication = i.Indication,
            IsControlledSubstance = i.IsControlledSubstance,
            Route = i.Route,
        }).ToList() ?? [];
        if (items.Count == 0)
            throw new InvalidOperationException("No medicines have been prescribed for this visit.");
        var doc = ClinicPrintForms.Prescription(await ClinicAsync(db, ct), PtrFor(bookingId, ClinicPrintKind.Prescription, Doctor(b, includeS2: true)), Patient(b), items,
            date: ClinicPrintForms.Iso(b.AppointmentDate), clock: _clock);
        return doc.WithInfo(new PrintJobInfo(ClinicPrintKind.Prescription));
    }

    public async Task<PrintDocument> LabRequestAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await LoadBookingAsync(db, bookingId, ct);
        var consult = await db.Consultations.AsNoTracking().FirstOrDefaultAsync(c => c.BookingId == bookingId, ct);
        var tests = consult is null
            ? []
            : (await db.LabOrders.AsNoTracking().Where(l => l.ConsultationId == consult.ConsultationId)
                    .Select(l => new { l.TestName, l.CreatedAt }).ToListAsync(ct))
                .OrderBy(l => l.CreatedAt).Select(l => l.TestName.Trim()).Where(t => t.Length > 0).ToList();
        if (tests.Count == 0)
            throw new InvalidOperationException("No lab orders have been entered for this visit.");
        List<string>? diagnoses = null;
        string? notes = null;
        if (consult is not null)
        {
            diagnoses = (await db.ConsultationDiagnoses.AsNoTracking().Where(d => d.ConsultationId == consult.ConsultationId)
                    .Select(d => new { d.Icd10Code, d.CustomDescription, d.CreatedAt }).ToListAsync(ct))
                .OrderBy(d => d.CreatedAt)
                .Select(d => ClinicPrintForms.DiagnosisLabel(d.Icd10Code, d.CustomDescription))
                .Where(s => s.Length > 0).ToList();
            notes = (consult.Assessment ?? "").Trim();
        }
        var doc = ClinicPrintForms.LabRequest(await ClinicAsync(db, ct), PtrFor(bookingId, ClinicPrintKind.LabRequest, Doctor(b, includeS2: true)), Patient(b), tests,
            date: ClinicPrintForms.Iso(b.AppointmentDate), diagnoses: diagnoses, clinicalNotes: notes, clock: _clock, items: _policy?.LabTests);
        return doc.WithInfo(new PrintJobInfo(ClinicPrintKind.LabRequest));
    }

    public async Task<PrintDocument> MedicalCertificateAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await LoadBookingAsync(db, bookingId, ct);
        var consult = await db.Consultations.AsNoTracking().FirstOrDefaultAsync(c => c.BookingId == bookingId, ct);
        var c = consult is null
            ? null
            : await db.MedicalCertificates.AsNoTracking().SingleOrDefaultAsync(x => x.ConsultationId == consult.ConsultationId, ct);
        if (c is null)
            throw new InvalidOperationException("No medical certificate has been issued for this visit.");
        var cert = new MedCertData
        {
            IssueDate = ClinicPrintForms.Iso(c.IssueDate),
            PatientAddressSnapshot = c.PatientAddressSnapshot,
            ExaminedAt = c.ExaminedAt,
            ExaminationDateFrom = ClinicPrintForms.Iso(c.ExaminationDateFrom),
            ExaminationDateTo = ClinicPrintForms.Iso(c.ExaminationDateTo),
            DiagnosisText = c.DiagnosisText,
            Recommendations = c.Recommendations,
            PurposeException = c.PurposeException,
            ComeBackOn = ClinicPrintForms.Iso(c.ComeBackOn),
            //JJQR - AI (2026-11-27) - why: the doctor's "Print as" choice for this visit; not chosen = read from the text (numbered/bulleted = List, anything else = Paragraph)
            DiagnosisParagraph = MedCertPrintLayout.For(bookingId, c.DiagnosisText) == DiagnosisLayout.Paragraph,
            //JJQR - END
        };
        // The consultation page prints the certificate without the S2 line (doctor: name, license, PTR only).
        var doc = ClinicPrintForms.MedicalCertificate(await ClinicAsync(db, ct), PtrFor(bookingId, ClinicPrintKind.MedicalCertificate, Doctor(b, includeS2: false)), Patient(b), cert, _clock);
        return doc.WithInfo(new PrintJobInfo(ClinicPrintKind.MedicalCertificate));
    }

    public async Task<PrintDocument> QueueSlipAsync(Guid bookingId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await LoadBookingAsync(db, bookingId, ct);
        //JJQR - AI (Nov 22, 2026) - offline-mode plan step 2.3/2.5: a paper-queue booking has no number yet; its slip says PAPER QUEUE (it used to refuse to print)
        var clinic = await ClinicAsync(db, ct);
        var ticket = new QueueSlipData
        {
            ClinicName = clinic.ClinicName,
            ClinicAddress = clinic.Address,
            QueueNumber = b.QueueNumber ?? "",
            IsPriority = QueueLane.IsPriority(b.QueueNumber),
            PatientName = $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
            PatientCode = b.Patient.PatientCode,
            DoctorName = b.Doctor.StaffAccount?.FullName ?? "",
            VisitType = b.VisitType.ToString(),
            ProvisionalFee = b.AmountDue > 0 ? b.AmountDue : b.TotalFee,
            IssuedAt = _clock.GetUtcNow(),
        };
        return QueueSlipDocument(ticket, _options);
    }

    /// <summary>Official Receipt for the booking's payment (web template) with a PENDING SYNC mark while the payment is unacked in the outbox.</summary>
    public async Task<PrintDocument> PaymentReceiptAsync(Guid bookingId, CancellationToken ct = default)
    {
        if (_payments is null) throw new InvalidOperationException("Payments service is not available for receipts.");
        var view = await _payments.GetReceiptAsync(bookingId, ct)
            ?? throw new InvalidOperationException("No payment has been recorded for this visit yet.");
        return ReceiptDocument.Build(ReceiptDocument.FromView(view));
    }

    /// <summary>Picks the web slip or the thermal layout from <see cref="PrintingOptions.QueueSlipStyle"/>.</summary>
    public static PrintDocument QueueSlipDocument(QueueSlipData ticket, PrintingOptions options)
    {
        var doc = options.QueueSlipStyle == QueueSlipStyle.Web ? QueueSlip.Web(ticket) : QueueSlip.Thermal(ticket, options.QueueSlipPaper);
        return doc.WithInfo(new PrintJobInfo(ClinicPrintKind.QueueSlip, ticket));
    }

    static async Task<ClinicApp.Domain.Entities.Booking> LoadBookingAsync(ClinicAppDbContext db, Guid bookingId, CancellationToken ct) =>
        await db.Bookings.AsNoTracking()
            .Include(x => x.Patient)
            .Include(x => x.Doctor).ThenInclude(d => d!.StaffAccount)
            .SingleOrDefaultAsync(x => x.BookingId == bookingId, ct)
        ?? throw new InvalidOperationException("Booking not found.");

    static async Task<PrintClinic> ClinicAsync(ClinicAppDbContext db, CancellationToken ct)
    {
        var s = await db.ClinicSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
        return s is null ? new PrintClinic("Grace Medical Clinic", "", null) : new PrintClinic(s.ClinicName, s.Address, s.ContactNumber);
    }

    static PrintDoctor Doctor(ClinicApp.Domain.Entities.Booking b, bool includeS2) =>
        new(b.Doctor.StaffAccount?.FullName ?? "", b.Doctor.LicenseNumber, b.Doctor.PtrNumber, includeS2 ? b.Doctor.S2Number : null);

    static PrintPatient Patient(ClinicApp.Domain.Entities.Booking b) =>
        new($"{b.Patient.FirstName} {b.Patient.LastName}".Trim(), b.Patient.PatientCode, ClinicPrintForms.Iso(b.Patient.DateOfBirth),
            b.Patient.Sex.ToString(), b.Patient.Address);
}
//JJQR - END
