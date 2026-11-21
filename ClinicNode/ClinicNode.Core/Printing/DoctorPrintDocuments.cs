//JJQR - AI (21 Nov 2026) - doctor-portal printouts in the web printHtml() chrome (PrintHtmlDocument.Wrap, non-bare: brand line, h1, .meta, .card).
// AppointmentSummaryDocument = verbatim body of the web "Appointment Summary" print (Project02-fe app/admin/bookings/[id]/page.tsx).
// PatientCardDocument = the patient ID card from GET /api/patients/{id}/card. The web has no printout for it yet, so it uses the same chrome and
// shows only the card fields (identity, blood type, allergies, emergency contact, clinic). No clinical notes.
using System.Globalization;
using System.Text;

namespace ClinicNode.Core.Printing;

/// <summary>GET /api/patients/{id}/card (snake_case on the wire). BirthDate is the server's "yyyy-MM-dd".</summary>
public sealed record PatientCardData(
    Guid PatientId, string PatientCode, string FullName, string BirthDate, string Sex, string? BloodType, IReadOnlyList<string> Allergies,
    string? EmergencyName, string? EmergencyPhone, string? EmergencyRelationship, string ClinicName, string? ClinicPhone, DateTimeOffset IssuedAt);

/// <summary>The fields the web Appointment Summary prints (raw booking values, like the web).</summary>
public sealed record AppointmentSummaryData(
    Guid BookingId, DateOnly AppointmentDate, string Status, string PatientName, string PatientCode, string DoctorName, string DoctorSpecialization,
    decimal AmountDue, string PaymentStatus, string? OrNumber);

public static class DoctorPrintPages
{
    public static readonly PrintPageSize A4 = new("A4", 210, 297, "mm");
}

public static class AppointmentSummaryDocument
{
    static string E(string? s) => ClinicFormsHtml.EscapeHtml(s ?? "");

    /// <summary>JS String(number): "500", "500.5" (no forced decimals).</summary>
    static string JsNumber(decimal v) => v.ToString("0.############################", CultureInfo.InvariantCulture);

    public static string Body(AppointmentSummaryData b) =>
        "<h1>Appointment Summary</h1>\n" +
        $"                     <p class=\"meta\">{E(b.AppointmentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))} · {E(b.Status)}</p>\n" +
        "                     <div class=\"card\">\n" +
        $"                       <p><strong>Patient:</strong> {E(b.PatientName)} ({E(b.PatientCode)})</p>\n" +
        $"                       <p><strong>Doctor:</strong> {E(b.DoctorName)} — {E(b.DoctorSpecialization)}</p>\n" +
        $"                       <p><strong>Amount due:</strong> ₱{E(JsNumber(b.AmountDue))}</p>\n" +
        $"                       <p><strong>Payment:</strong> {E(b.PaymentStatus)}{(string.IsNullOrEmpty(b.OrNumber) ? "" : " · " + E(b.OrNumber))}</p>\n" +
        "                     </div>";

    public static PrintDocument Build(AppointmentSummaryData b)
    {
        var title = $"Appointment Summary {b.BookingId:D}";
        return new PrintDocument(title, DoctorPrintPages.A4, PrintHtmlDocument.Wrap(title, Body(b), bare: false));
    }
}

public static class PatientCardDocument
{
    static string E(string? s) => ClinicFormsHtml.EscapeHtml(s ?? "");

    const string CardCss = ".idcard { max-width: 360px; } .idcard p { margin: 2px 0; } .idcard .code { font-size: 15px; font-weight: 700; color: #00685f; }";

    static string Born(string birthDate) =>
        DateOnly.TryParseExact(birthDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("MMM d, yyyy", CultureInfo.InvariantCulture) : birthDate;

    public static string Body(PatientCardData c)
    {
        var sb = new StringBuilder();
        sb.Append("<h1>Patient Card</h1>\n");
        sb.Append($"<p class=\"meta\">{E(c.ClinicName)}{(string.IsNullOrWhiteSpace(c.ClinicPhone) ? "" : " · " + E(c.ClinicPhone))}</p>\n");
        sb.Append("<div class=\"card idcard\">\n");
        sb.Append($"  <p class=\"code\">{E(c.PatientCode)}</p>\n");
        sb.Append($"  <p><strong>{E(c.FullName)}</strong></p>\n");
        sb.Append($"  <p><strong>Born:</strong> {E(Born(c.BirthDate))} · <strong>Sex:</strong> {E(c.Sex)}</p>\n");
        sb.Append($"  <p><strong>Blood type:</strong> {E(string.IsNullOrWhiteSpace(c.BloodType) ? "—" : c.BloodType)}</p>\n");
        sb.Append($"  <p><strong>Allergies:</strong> {E(c.Allergies.Count == 0 ? "—" : string.Join(", ", c.Allergies))}</p>\n");
        if (!string.IsNullOrWhiteSpace(c.EmergencyName) || !string.IsNullOrWhiteSpace(c.EmergencyPhone))
        {
            var who = string.Join(" ", new[] { c.EmergencyName, string.IsNullOrWhiteSpace(c.EmergencyRelationship) ? null : $"({c.EmergencyRelationship})" }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            sb.Append($"  <p><strong>Emergency contact:</strong> {E(who)}{(string.IsNullOrWhiteSpace(c.EmergencyPhone) ? "" : " · " + E(c.EmergencyPhone))}</p>\n");
        }
        sb.Append("</div>\n");
        sb.Append($"<p class=\"muted\">Issued {E(c.IssuedAt.ToOffset(TimeSpan.FromHours(8)).ToString("MMM d, yyyy", CultureInfo.InvariantCulture))}</p>");
        return sb.ToString();
    }

    public static PrintDocument Build(PatientCardData c)
    {
        var title = $"Patient Card {c.PatientCode}";
        return new PrintDocument(title, DoctorPrintPages.A4, PrintHtmlDocument.Wrap(title, Body(c), bare: false, extraCss: CardCss));
    }
}

//JJQR - AI (21 Nov 2026) - Rx form Print (web PrescriptionForm handlePrint -> printPrescription): the A5 pad built from the lines ON SCREEN (saved or not),
// with the clinic / doctor / patient header the web loads (queryClinicSettings, queryDoctorById incl. S2, queryPatientById). Same builder as the booking print.
public sealed record RxPrintHeader(PrintClinic Clinic, PrintDoctor Doctor, PrintPatient Patient);

public static class DoctorRxPrintDocument
{
    public static PrintDocument Build(RxPrintHeader header, IReadOnlyList<RxLine> items) =>
        ClinicPrintForms.Prescription(header.Clinic, header.Doctor, header.Patient, items).WithInfo(new PrintJobInfo(ClinicPrintKind.Prescription));
}
//JJQR - END
//JJQR - END
