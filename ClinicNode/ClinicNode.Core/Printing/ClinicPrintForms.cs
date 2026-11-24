//JJQR - AI (Nov 21, 2026) - port of Project02-fe src/lib/print-forms.ts: formats dates/age, then builds the full printable document for each form.
using System.Globalization;
using System.Text.RegularExpressions;

namespace ClinicNode.Core.Printing;

public sealed record PrintClinic(string ClinicName, string Address, string? ContactNumber);

public static class ClinicPrintForms
{
    public static readonly TimeZoneInfo Manila = FindManila();

    static TimeZoneInfo FindManila()
    {
        foreach (var id in new[] { "Asia/Manila", "Singapore Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("Asia/Manila", TimeSpan.FromHours(8), "Asia/Manila", "PHT");
    }

    /// <summary>clock.ts todayManila().</summary>
    public static DateOnly TodayManila(TimeProvider? clock = null) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime((clock ?? TimeProvider.System).GetUtcNow(), Manila).DateTime);

    /// <summary>
    /// print-forms.ts fmtDate: <c>toLocaleDateString("en-PH", { year: "numeric", month: "long", day: "numeric", timeZone: "Asia/Manila" })</c>
    /// → "November 21, 2026". Date-only strings are UTC midnight in JS, which is the same calendar day in Manila.
    /// </summary>
    public static string FormatDate(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        DateOnly day;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            day = d;
        else if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(dto, Manila).DateTime);
        else
            return "Invalid Date";
        return FormatDate(day);
    }

    public static string FormatDate(DateOnly day) => day.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
    public static string FormatDate(DateOnly? day) => day is null ? "" : FormatDate(day.Value);

    public static string Iso(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static string? Iso(DateOnly? day) => day is null ? null : Iso(day.Value);

    /// <summary>print-forms.ts ageFromDob (age as of today in Manila).</summary>
    public static string AgeFromDob(string? dob, DateOnly todayManila)
    {
        if (string.IsNullOrEmpty(dob)) return "";
        var parts = dob.Split('-');
        int Part(int i) => i < parts.Length && int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        var by = Part(0);
        if (by == 0) return "";
        int bm = Part(1), bd = Part(2);
        var age = todayManila.Year - by;
        if (todayManila.Month < bm || (todayManila.Month == bm && todayManila.Day < bd)) age--;
        return age >= 0 ? age.ToString(CultureInfo.InvariantCulture) : "";
    }

    /// <summary>med-cert-fields.ts cleanMedCertAddress.</summary>
    public static string CleanMedCertAddress(string? addr)
    {
        var s = (addr ?? "").Trim();
        s = Regex.Replace(s, @"^(residing at\s+)+", "", RegexOptions.IgnoreCase).Trim();
        s = Regex.Replace(s, @"[,\s]+$", "").Trim();
        s = Regex.Replace(s, @",\s*,+", ", ");
        return s;
    }

    /// <summary>clinical.ts formatConsultationDiagnosisLabel.</summary>
    public static string DiagnosisLabel(string? icd10Code, string? customDescription)
    {
        var code = icd10Code?.Trim();
        var desc = customDescription?.Trim();
        if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(desc)) return $"{code} — {desc}";
        if (!string.IsNullOrEmpty(code)) return code;
        return desc ?? "";
    }

    /// <summary>printPrescription(): A5 prescription pad.</summary>
    public static PrintDocument Prescription(PrintClinic clinic, PrintDoctor doctor, PrintPatient patient, IReadOnlyList<RxLine> items,
        string? date = null, string? nextAppointment = null, TimeProvider? clock = null)
    {
        var today = TodayManila(clock);
        var body = ClinicFormsHtml.BuildPrescriptionHtml(new RxHtmlOptions(
            Origin: "",
            PageSize: PrintPaper.WebPageA5,
            Doctor: doctor,
            Patient: patient,
            Age: AgeFromDob(patient.DateOfBirth, today),
            Date: FormatDate(date ?? Iso(today)),
            Items: items,
            NextAppointment: nextAppointment ?? ""));
        return Form("Prescription", PrintPaper.A5, body);
    }

    /// <summary>printMedicalCertificate(): A5 medical certificate.</summary>
    public static PrintDocument MedicalCertificate(PrintClinic clinic, PrintDoctor doctor, PrintPatient patient, MedCertData cert, TimeProvider? clock = null)
    {
        var addr = CleanMedCertAddress(cert.PatientAddressSnapshot ?? patient.Address ?? "");
        var body = ClinicFormsHtml.BuildMedicalCertificateHtml(new MedCertHtmlOptions(
            Origin: "",
            PageSize: PrintPaper.WebPageA5,
            Doctor: doctor,
            Patient: patient,
            IssueDate: FormatDate(cert.IssueDate ?? Iso(TodayManila(clock))),
            Address: addr,
            ExaminedAt: cert.ExaminedAt ?? clinic.ClinicName,
            ExamFrom: FormatDate(cert.ExaminationDateFrom),
            ExamTo: FormatDate(cert.ExaminationDateTo),
            Diagnosis: cert.DiagnosisText ?? "",
            Recommendations: cert.Recommendations ?? "",
            PurposeException: cert.PurposeException ?? "",
            ComeBackOn: FormatDate(cert.ComeBackOn)));
        return Form("Medical Certificate", PrintPaper.A5, body);
    }

    /// <summary>printLabRequest(): A5 (148 x 210 mm), the same sheet as the prescription and the medical certificate.</summary>
    public static PrintDocument LabRequest(PrintClinic clinic, PrintDoctor doctor, PrintPatient patient, IReadOnlyList<string> tests,
        string? date = null, IReadOnlyList<string>? diagnoses = null, string? clinicalNotes = null, TimeProvider? clock = null)
    {
        var today = TodayManila(clock);
        var body = ClinicFormsHtml.BuildLabRequestHtml(new LabHtmlOptions(
            Origin: "",
            PageSize: PrintPaper.WebPageA5, //JJQR - AI (Nov 24, 2026) - the lab request is the same A5 sheet as the prescription and the medical certificate //JJQR - END
            Doctor: doctor,
            Patient: patient,
            Age: AgeFromDob(patient.DateOfBirth, today),
            Date: FormatDate(date ?? Iso(today)),
            Tests: tests,
            Diagnoses: diagnoses,
            ClinicalNotes: clinicalNotes));
        return Form("Laboratory Request", PrintPaper.A5, body);
    }

    static PrintDocument Form(string title, PrintPageSize page, string body) =>
        new(title, page, FormAssets.InlineAssets(PrintHtmlDocument.WrapForm(title, body)));
}
//JJQR - END
