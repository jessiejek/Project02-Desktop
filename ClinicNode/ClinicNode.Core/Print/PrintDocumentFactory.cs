// JJQR - AI (Nov 21, 2026) - HTML builders for Rx (A5), lab (4.3x6.4in), medical certificate (A5), and queue slip.
using System.Globalization;
using System.Net;
using System.Text;
using ClinicApp.Domain;
using ClinicApp.Infrastructure;
using ClinicNode.Core.Queue;
using Microsoft.EntityFrameworkCore;

namespace ClinicNode.Core.Print;

public interface IPrintDocumentFactory
{
    Task<PrintDocument> PrescriptionAsync(Guid bookingId, CancellationToken ct = default);
    Task<PrintDocument> LabRequestAsync(Guid bookingId, CancellationToken ct = default);
    Task<PrintDocument> MedicalCertificateAsync(Guid bookingId, CancellationToken ct = default);
    Task<PrintDocument> QueueSlipAsync(Guid bookingId, CancellationToken ct = default);
    // JJQR - AI (Nov 21, 2026) - official receipt / OR (implemented by ClinicFormsPrintDocumentFactory)
    Task<PrintDocument> PaymentReceiptAsync(Guid bookingId, CancellationToken ct = default);
    // JJQR - END
}

public sealed class PrintDocumentFactory : IPrintDocumentFactory
{
    private readonly IDbContextFactory<ClinicAppDbContext> _factory;

    public PrintDocumentFactory(IDbContextFactory<ClinicAppDbContext> factory) => _factory = factory;

    public async Task<PrintDocument> PrescriptionAsync(Guid bookingId, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(bookingId, ct);
        var lines = ctx.Rx.Count == 0
            ? "<p class='muted'>No medicines this visit.</p>"
            : "<ol class='rx'>" + string.Concat(ctx.Rx.Select((l, i) =>
            {
                var sig = string.Join(" · ", new[] { l.Dosage, l.Instruction, l.Timing, l.MealRelation, l.Route }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var qty = string.IsNullOrWhiteSpace(l.Quantity) ? "" : $"Qty: {Esc(l.Quantity)}";
                return $"<li><div class='name'>{Esc(l.GenericName)}</div><div class='sig'>{Esc(sig)}</div><div class='qty'>{qty}</div></li>";
            })) + "</ol>";
        var follow = ctx.FollowUp is null ? "" : $"<p class='next'>Next appointment: {Esc(ctx.FollowUp)}</p>";
        var body = $"""
            {PatientBlock(ctx)}
            <h2>Prescription</h2>
            {lines}
            {follow}
            {DoctorSign(ctx)}
            """;
        return new PrintDocument("Prescription", PrintSpec.Prescription, Shell(PrintSpec.Prescription, "Prescription", ctx, body, letterheadGap: true));
    }

    public async Task<PrintDocument> LabRequestAsync(Guid bookingId, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(bookingId, ct);
        if (ctx.Labs.Count == 0)
            throw new InvalidOperationException("No lab orders have been entered for this visit.");
        var rows = string.Concat(ctx.Labs.Select(l =>
        {
            var extra = string.Join(" · ", new[] { l.Reason, l.SpecimenType, l.ClinicalIndication }.Where(s => !string.IsNullOrWhiteSpace(s)));
            return $"<li><div class='name'>{Esc(l.TestName)}</div><div class='sig'>{Esc(extra)}</div></li>";
        }));
        var body = $"""
            {PatientBlock(ctx)}
            <h2>Laboratory request</h2>
            <ol class='rx'>{rows}</ol>
            {DoctorSign(ctx)}
            """;
        return new PrintDocument("Laboratory request", PrintSpec.LabRequest, Shell(PrintSpec.LabRequest, "Laboratory request", ctx, body, letterheadGap: true));
    }

    public async Task<PrintDocument> MedicalCertificateAsync(Guid bookingId, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(bookingId, ct);
        if (ctx.Cert is null)
            throw new InvalidOperationException("No medical certificate has been issued for this visit.");
        var c = ctx.Cert;
        var exam = (c.ExaminationDateFrom, c.ExaminationDateTo) switch
        {
            (not null, not null) when c.ExaminationDateFrom != c.ExaminationDateTo =>
                $"{Fmt(c.ExaminationDateFrom)} to {Fmt(c.ExaminationDateTo)}",
            (not null, _) => Fmt(c.ExaminationDateFrom),
            _ => Fmt(c.IssueDate)
        };
        var body = $"""
            <h2>Medical certificate</h2>
            {PatientBlock(ctx)}
            <p>This is to certify that <strong>{Esc(ctx.PatientName)}</strong>
            {(string.IsNullOrWhiteSpace(c.PatientAddressSnapshot) ? "" : $" of {Esc(c.PatientAddressSnapshot)}")}
            was examined {Esc(string.IsNullOrWhiteSpace(c.ExaminedAt) ? exam : c.ExaminedAt)}.</p>
            {(string.IsNullOrWhiteSpace(c.DiagnosisText) ? "" : $"<p><strong>Impression:</strong> {Esc(c.DiagnosisText)}</p>")}
            {(string.IsNullOrWhiteSpace(c.Recommendations) ? "" : $"<p><strong>Recommendations:</strong> {Esc(c.Recommendations)}</p>")}
            {(string.IsNullOrWhiteSpace(c.PurposeException) ? "" : $"<p><strong>Purpose / remarks:</strong> {Esc(c.PurposeException)}</p>")}
            {(c.ComeBackOn is null ? "" : $"<p>Come back on {Fmt(c.ComeBackOn)}.</p>")}
            <p>Issued {Fmt(c.IssueDate)}.</p>
            {DoctorSign(ctx)}
            """;
        return new PrintDocument("Medical certificate", PrintSpec.MedicalCertificate, Shell(PrintSpec.MedicalCertificate, "Medical certificate", ctx, body, letterheadGap: true));
    }

    public async Task<PrintDocument> QueueSlipAsync(Guid bookingId, CancellationToken ct = default)
    {
        var ctx = await LoadAsync(bookingId, ct);
        if (string.IsNullOrWhiteSpace(ctx.QueueNumber))
            throw new InvalidOperationException("This booking has no queue number yet.");
        var lane = QueueLane.IsPriority(ctx.QueueNumber) ? "Priority lane (Senior / PWD)" : "Regular lane";
        var body = $"""
            <div class="slip">
              <div class="clinic">{Esc(ctx.ClinicName)}</div>
              <div class="qn">{Esc(ctx.QueueNumber)}</div>
              <div class="lane">{Esc(lane)}</div>
              <div class="who">{Esc(ctx.PatientName)}</div>
              <div class="meta">{Esc(ctx.PatientCode)} · {Esc(ctx.DoctorName)}</div>
              <div class="meta">{Fmt(ctx.AppointmentDate)} · {Esc(ctx.VisitType)}</div>
            </div>
            """;
        return new PrintDocument($"Queue slip {ctx.QueueNumber}", PrintSpec.QueueSlip, Shell(PrintSpec.QueueSlip, "Queue slip", ctx, body, letterheadGap: false));
    }

    // JJQR - AI (Nov 21, 2026) - stub; the App registers ClinicFormsPrintDocumentFactory for real receipts
    public Task<PrintDocument> PaymentReceiptAsync(Guid bookingId, CancellationToken ct = default) =>
        Task.FromException<PrintDocument>(new NotSupportedException("Payment receipts are produced by ClinicFormsPrintDocumentFactory."));
    // JJQR - END

    async Task<Ctx> LoadAsync(Guid bookingId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var b = await db.Bookings.AsNoTracking()
            .Include(x => x.Patient)
            .Include(x => x.Doctor).ThenInclude(d => d!.StaffAccount)
            .SingleOrDefaultAsync(x => x.BookingId == bookingId, ct)
            ?? throw new InvalidOperationException("Booking not found.");
        var settings = await db.ClinicSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == 1, ct);
        var consult = await db.Consultations.AsNoTracking().FirstOrDefaultAsync(c => c.BookingId == bookingId, ct);
        var group = await db.PrescriptionGroups.AsNoTracking().Include(g => g.LineItems)
            .FirstOrDefaultAsync(g => g.BookingId == bookingId, ct);
        var labs = consult is null
            ? []
            : await db.LabOrders.AsNoTracking().Where(l => l.ConsultationId == consult.ConsultationId).ToListAsync(ct);
        var cert = consult is null
            ? null
            : await db.MedicalCertificates.AsNoTracking().SingleOrDefaultAsync(c => c.ConsultationId == consult.ConsultationId, ct);
        var follow = consult is null
            ? null
            : await db.FollowUps.AsNoTracking().SingleOrDefaultAsync(f => f.ConsultationId == consult.ConsultationId, ct);

        return new Ctx(
            settings?.ClinicName ?? "Grace Medical Clinic",
            settings?.Address,
            settings?.ContactNumber,
            $"{b.Patient.FirstName} {b.Patient.LastName}".Trim(),
            b.Patient.PatientCode,
            b.Patient.DateOfBirth,
            b.Patient.Sex.ToString(),
            b.Patient.Address,
            b.Doctor.StaffAccount?.FullName ?? "",
            b.Doctor.LicenseNumber,
            b.Doctor.PtrNumber,
            b.Doctor.S2Number,
            b.AppointmentDate,
            b.QueueNumber,
            b.VisitType.ToString(),
            group?.LineItems.Select(i => (i.GenericName, i.Dosage, i.Quantity, i.Instruction, i.Timing, i.MealRelation, i.Route)).ToList() ?? [],
            labs.Select(l => (l.TestName, l.Reason, l.SpecimenType, l.ClinicalIndication)).ToList(),
            cert,
            follow is null ? null : follow.FollowUpDate.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("en-PH")) +
                (string.IsNullOrWhiteSpace(follow.Reason) ? "" : $" ({follow.Reason})"));
    }

    static string PatientBlock(Ctx ctx)
    {
        var age = Age(ctx.Dob, ctx.AppointmentDate);
        return $"""
            <div class="patient">
              <div><strong>{Esc(ctx.PatientName)}</strong> · {Esc(ctx.PatientCode)}</div>
              <div>{Esc(ctx.Sex)} · {age} y/o · DOB {Fmt(ctx.Dob)}</div>
              {(string.IsNullOrWhiteSpace(ctx.Address) ? "" : $"<div>{Esc(ctx.Address)}</div>")}
              <div>Date of visit: {Fmt(ctx.AppointmentDate)}</div>
            </div>
            """;
    }

    static string DoctorSign(Ctx ctx) => $"""
        <div class="sign">
          <div class="line"></div>
          <div>{Esc(ctx.DoctorName)}</div>
          {(string.IsNullOrWhiteSpace(ctx.License) ? "" : $"<div>Lic. no. {Esc(ctx.License)}</div>")}
          {(string.IsNullOrWhiteSpace(ctx.Ptr) ? "" : $"<div>PTR {Esc(ctx.Ptr)}</div>")}
        </div>
        """;

    static string Shell(PrintPageSize page, string title, Ctx ctx, string body, bool letterheadGap)
    {
        var gap = letterheadGap ? "28mm" : "4mm";
        var html = """
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <title>__TITLE__</title>
              <style>
                @page { size: __SIZE__; margin: 0; }
                html, body { margin: 0; padding: 0; width: __W__; height: __H__; }
                body { font-family: "Times New Roman", Times, serif; font-size: 11pt; color: #111; }
                .sheet { box-sizing: border-box; width: __W__; height: __H__; padding: __GAP__ 10mm 10mm 10mm; }
                h2 { font-size: 13pt; letter-spacing: 0.06em; text-transform: uppercase; margin: 0 0 8px; text-align: center; }
                .patient, .sign, .rx { margin: 8px 0; }
                .muted { color: #555; }
                ol.rx { padding-left: 1.2em; }
                ol.rx li { margin: 0 0 8px; }
                .name { font-weight: bold; }
                .sig, .qty, .meta, .lane { font-size: 10pt; }
                .sign { margin-top: 22px; text-align: right; }
                .sign .line { border-top: 1px solid #111; width: 46mm; margin-left: auto; margin-bottom: 4px; }
                .slip { text-align: center; padding-top: 4mm; }
                .slip .clinic { font-size: 11pt; font-weight: bold; }
                .slip .qn { font-size: 28pt; font-weight: bold; letter-spacing: 0.04em; margin: 6px 0; }
                .slip .who { font-size: 12pt; margin-top: 6px; }
              </style>
            </head>
            <body>
              <div class="sheet">__BODY__</div>
            </body>
            </html>
            """;
        return html
            .Replace("__TITLE__", Esc(title))
            .Replace("__SIZE__", page.CssSize)
            .Replace("__W__", page.CssWidth)
            .Replace("__H__", page.CssHeight)
            .Replace("__GAP__", gap)
            .Replace("__BODY__", body);
    }

    static string Fmt(DateOnly? d) => d is null ? "" : d.Value.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("en-PH"));
    static string Esc(string? s) => WebUtility.HtmlEncode(s ?? "");
    static string Age(DateOnly dob, DateOnly on)
    {
        var age = on.Year - dob.Year;
        if (on < dob.AddYears(age)) age--;
        return age < 0 ? "" : age.ToString(CultureInfo.InvariantCulture);
    }

    sealed record Ctx(
        string ClinicName,
        string? Address,
        string? Contact,
        string PatientName,
        string PatientCode,
        DateOnly Dob,
        string Sex,
        string? PatientAddress,
        string DoctorName,
        string? License,
        string? Ptr,
        string? S2,
        DateOnly AppointmentDate,
        string? QueueNumber,
        string VisitType,
        List<(string GenericName, string Dosage, string Quantity, string? Instruction, string? Timing, string? MealRelation, string? Route)> Rx,
        List<(string TestName, string? Reason, string? SpecimenType, string? ClinicalIndication)> Labs,
        ClinicApp.Domain.Entities.MedicalCertificate? Cert,
        string? FollowUp);
}
// JJQR - END
