//JJQR - AI (Nov 21, 2026) - line-for-line port of Project02-fe src/lib/print-forms-html.ts (prescription A5, medical certificate A5, lab request 4.3x6.4in).
// Output is byte-identical to the web builders for the same inputs (ClinicNode.Tests/Printing/golden proves it). CSS lives in Generated/FormsCss.g.cs.
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClinicNode.Core.Printing;

public static class ClinicFormsHtml
{
    /// <summary>Pre-printed stationery letterhead (LETTERHEAD in the web module); not taken from clinic settings.</summary>
    public static class Letterhead
    {
        public const string Name = "ALLYN GRACE T. ESPAÑA-GAVINO, MD";
        public const string Line2 = "FAMILY AND COMMUNITY MEDICINE";
        public const string Line3 = "ADULT AND PEDIA";
        public const string Clinic = "GRACE MEDICAL CLINIC";
        public const string Street = "3ML Quezon National Highway";
        public const string City = "Buaya, Lapu Lapu City";
        public const string Contact = "Contact # 09285612976";
        public const string WeekdayDay = "Monday to Friday";
        public const string WeekdayHours = "10:00am - 5:00pm";
        public const string Evening = "7:00pm - 8:00pm";
        public const string Saturday = "Saturday";
        public const string SaturdayHours = "10:00pm- 5:00pm";
    }

    //JJQR - AI (Nov 24, 2026) - owner: the clinic's lab does these tests (the pad's printed list plus the hand-written TSH, FT3, FT4, HbA1c). This is the DEFAULT; the Admin can add and remove tests (same list as the web).
    public static readonly IReadOnlyList<string> LabDefaultItems =
    [
        "CBC", "URINALYSIS", "LIPID PROFILE", "SGPT", "CREA", "FBS", "BUA",
        "TSH", "FT3", "FT4", "HbA1c", "DENGUE NS1, IgG, IgM", "ECG 12L",
    ];
    public const int LabMaxItems = 26;
    public const int LabMaxItemName = 40;

    /// <summary>The tests to print: the Admin's list (trimmed, no blanks or repeats, at most <see cref="LabMaxItems"/>) or the default.</summary>
    public static IReadOnlyList<string> NormalizeLabItems(IReadOnlyList<string>? items)
    {
        var outList = new List<string>();
        var seen = new HashSet<string>();
        foreach (var raw in items ?? [])
        {
            var t = Regex.Replace(raw ?? "", @"\s+", " ").Trim();
            if (t.Length == 0 || !seen.Add(t.ToLowerInvariant())) continue;
            outList.Add(t);
            if (outList.Count >= LabMaxItems) break;
        }
        return outList.Count > 0 ? outList : LabDefaultItems;
    }

    static string LabTop(int hundredths) => (hundredths / 100) + "." + (hundredths % 100).ToString("00", CultureInfo.InvariantCulture) + "in";
    //JJQR - END

    const RegexOptions Js = RegexOptions.ECMAScript | RegexOptions.CultureInvariant;
    const RegexOptions JsI = Js | RegexOptions.IgnoreCase;

    /// <summary>print.ts / print-forms-html.ts escapeHtml: &amp; &lt; &gt; &quot; only (not apostrophes).</summary>
    public static string EscapeHtml(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    static string E(string? v) => EscapeHtml(v ?? "");

    static string Asset(string origin, string file) => E(origin) + "/forms/" + file;

    static string Flat(string? v) => E(Regex.Replace(v ?? "", @"\s+", " ").Trim());

    internal static string RxMcSigName(PrintDoctor doctor)
    {
        var n = (doctor.FullName ?? "").Trim();
        if (n.Length == 0) return Letterhead.Name;
        var u = n.ToUpperInvariant();
        return Regex.IsMatch(u, @",\s*MD$", Js) ? u : u + ", MD";
    }

    internal static string LabSigName(PrintDoctor doctor)
    {
        var n = (doctor.FullName ?? "").Trim();
        if (n.Length == 0) return Regex.Replace(Letterhead.Name, @", MD$", "", Js);
        return Regex.Replace(n, @",?\s*MD\s*$", "", JsI).Trim();
    }

    internal static string LabSigFontSize(string name)
    {
        const double availIn = 1.48;
        var est = name.Length * 0.5 * (6.0 / 72.0);
        if (est <= availIn) return "6pt";
        var pt = 6 * (availIn / est);
        // JS Math.round rounds halves up; then Number→string is the shortest round-trip form.
        var v = pt < 4.2 ? 4.2 : Math.Floor(pt * 100 + 0.5) / 100;
        return JsNumber(v) + "pt";
    }

    internal static string JsNumber(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    static string LetterheadHtml(string origin) =>
        "<div class=\"lh\">" +
          "<img class=\"lh-logo\" src=\"" + Asset(origin, "logo.png") + "\" alt=\"\" />" +
          "<div class=\"lh-doc\">" + Letterhead.Name + "</div>" +
          "<div class=\"lh-spec lh-fam\">" + Letterhead.Line2 + "</div>" +
          "<div class=\"lh-spec lh-adult\">" + Letterhead.Line3 + "</div>" +
          "<img class=\"lh-seal\" src=\"" + Asset(origin, "seal.png") + "\" alt=\"\" />" +
          "<div class=\"lh-c1\">" + Letterhead.Clinic + "</div>" +
          "<div class=\"lh-c2\">" + Letterhead.Street + "</div>" +
          "<div class=\"lh-c3\">" + Letterhead.City + "</div>" +
          "<div class=\"lh-c4\">" + Letterhead.Contact + "</div>" +
          "<div class=\"lh-r1d\"><span class=\"h-day\">" + Letterhead.WeekdayDay + " " + Letterhead.WeekdayHours + "</span></div>" +
          "<div class=\"lh-r2t\"><span class=\"h-time\">" + Letterhead.Evening + "</span></div>" +
          "<div class=\"lh-r3d\"><span class=\"h-day\">" + Letterhead.Saturday + "</span></div>" +
          "<div class=\"lh-r3t\"><span class=\"h-time\">" + Letterhead.SaturdayHours + "</span></div>" +
        "</div>";

    static string CredLine(string label, string value) =>
        "<div class=\"sig-row\"><span class=\"sig-lbl\">" + label + "</span><span class=\"u sig-u\">" + E(value) + "</span></div>";

    static string SignatureBlock(PrintDoctor doctor)
    {
        var lines = new List<string>
        {
            "<div class=\"sig-name\">" + E(RxMcSigName(doctor)) + "</div>",
            CredLine("License No:", doctor.LicenseNumber ?? ""), //JJQR - AI (Nov 24, 2026) - owner: what the doctor fills in under Profile prints on the prescription, medical certificate and lab request; the line stays blank when it is empty //JJQR - END
            CredLine("PTR No:", doctor.PtrNumber ?? ""),
        };
        if (!string.IsNullOrEmpty(doctor.S2Number)) lines.Add(CredLine("S2 No:", doctor.S2Number));
        return "<div class=\"sig\">" + string.Concat(lines) + "</div>";
    }

    static string TitleOpts(string? sex)
    {
        var s = (sex ?? "").ToLowerInvariant();
        static string Opt(string label, bool hit) => "<span class=\"t-opt" + (hit ? " t-hit" : "") + "\">" + label + "</span>";
        return Opt("Mr.", s == "male") +
               "<span class=\"t-sep\">/</span>" +
               Opt("Ms.", s == "female") +
               "<span class=\"t-sep\">/</span>" +
               Opt("Mrs.", false);
    }

    public static string BuildMedicalCertificateHtml(MedCertHtmlOptions opts)
    {
        var exceptionText = opts.PurposeException.Trim();
        var purposeBlock = exceptionText.Length > 0
            ? "<div class=\"mc-purpose\"><div class=\"write-lines\"><div class=\"wl\"></div><div class=\"wl\"></div><div class=\"wl\"></div></div>" +
              "<div class=\"write-text\">" + E(exceptionText) + "</div></div>"
            : "";
        var css = FormsCss.ShellCss(opts.PageSize, "148mm", "210mm") + FormsCss.A5LetterheadCss("1.62in") + FormsCss.MedicalCertificateCss();
        //JJQR - AI (Nov 21, 2026) - the form is a pre-printed A5 sheet with every line pinned at a fixed height. A long address used to wrap onto the next pinned line and overprint it.
        // Now a field that needs a second row pushes every line below it down by one row (0.16 in); a value that would need three rows is shrunk to fit two.
        static int ExtraRows(string text, int perRow) => text.Length <= perRow ? 0 : 1;
        var resLong = opts.Address.Trim().Length > 78;
        var examLong = opts.ExaminedAt.Trim().Length > 150;
        var rowsRes = ExtraRows(opts.Address.Trim(), 50);
        var rowsExam = ExtraRows(opts.ExaminedAt.Trim(), 44);
        var shiftRes = rowsRes * 0.16;
        var shiftAll = (rowsRes + rowsExam) * 0.16;
        string In(double n) => n.ToString("0.00", CultureInfo.InvariantCulture) + "in";
        // every pinned line used width:100% inside left/right margins, so its underline ran past the right margin to the page edge: let it fill only the margin box
        // (the ".form-mc .ln { width: auto; }" rule now comes from the generated MedicalCertificateCss, same as the web)
        if (shiftAll > 0 || resLong || examLong)
        {
            css += "\n.form-mc .mc-residing, .form-mc .mc-exam { align-items: baseline; }" +
                   "\n.form-mc .mc-residing .u, .form-mc .mc-exam .u { display: block; white-space: normal; overflow-wrap: anywhere; line-height: 1.15; }" +
                   (resLong ? "\n.form-mc .mc-residing { font-size: 7.5pt; }" : "") +
                   (examLong ? "\n.form-mc .mc-exam { font-size: 7.5pt; }" : "") +
                   $"\n.form-mc .mc-exam {{ top: calc(3.42in + {In(shiftRes)}); }}" +
                   $"\n.form-mc .mc-on {{ top: calc(3.66in + {In(shiftAll)}); }}" +
                   $"\n.form-mc .mc-diag {{ top: calc(4.17in + {In(shiftAll)}); }}" +
                   $"\n.form-mc .mc-rec {{ top: calc(5.10in + {In(shiftAll)}); }}" +
                   $"\n.form-mc .mc-disc {{ top: calc(5.90in + {In(shiftAll)}); }}";
        }
        //JJQR - END

        var inner =
            LetterheadHtml(opts.Origin) +
            "<div class=\"rule-double\"></div>" +
            "<div class=\"mc-title\">MEDICAL CERTIFICATE</div>" +
            "<div class=\"mc-date\">Date: <span class=\"u\">" + E(opts.IssueDate) + "</span></div>" +
            "<div class=\"mc-to\">To whom it may concern,</div>" +
            "<div class=\"mc-certify ln\"><span>This is to certify that " +
            TitleOpts(opts.Patient.Sex) +
            "</span><span class=\"u grow\">" + E(opts.Patient.FullName) + "</span></div>" +
            "<div class=\"mc-residing ln\"><span>residing at</span><span class=\"u grow\">" + E(opts.Address) + "</span></div>" +
            "<div class=\"mc-exam ln\"><span>has been examined in</span><span class=\"u grow\">" + E(opts.ExaminedAt) + "</span></div>" +
            "<div class=\"mc-on ln\"><span>on</span><span class=\"u on-u\">" + E(opts.ExamFrom) +
            "</span><span>until</span><span class=\"u grow\">" + E(opts.ExamTo) + "</span><span>.</span></div>" +
            DiagnosisBlock(opts.Diagnosis, opts.DiagnosisParagraph ? DiagnosisLayout.Paragraph : DiagnosisLayout.Lines) + //JJQR - AI (2026-11-27) - why: owner: "SO paragraph | List" ("One line" removed) //JJQR - END
            //JJQR - AI (2026-11-27) - why: one diagnosis per ruled line (was Flat(opts.Diagnosis), which ran "1) ...;2) ..." together) //JJQR - END
            "<div class=\"write mc-rec\"><div class=\"write-lines\"><div class=\"wl\"></div><div class=\"wl\"></div><div class=\"wl\"></div></div>" +
            "<div class=\"write-text\">" +
            "<span class=\"write-lbl\">Recommendations:</span>" + Flat(opts.Recommendations) +
            "</div></div>" +
            "<div class=\"mc-disc\">" +
            "<div class=\"mc-boiler\">This certificate is issued upon the request for whatever purpose it may serve except</div>" +
            purposeBlock +
            "<div class=\"mc-boiler2\">and may not be used for medico-legal purposes.</div>" +
            "</div>" +
            "<div class=\"mc-back\"><div>Please come back on:</div><span class=\"u back-u\">" + E(opts.ComeBackOn) + "</span></div>" +
            "<div class=\"mc-sig\">" + SignatureBlock(opts.Doctor) + "</div>";

        return "<style>" + css + "</style><div class=\"form form-mc\">" + inner + "</div>";
    }

    static string SlotSpan(string label, string kind)
    {
        var cls = kind == "pill" ? "slot pill" : kind == "faint" ? "slot faint" : "slot";
        return "<span class=\"" + cls + "\">" + label + "</span>";
    }

    static string SlotLine(IReadOnlyList<string> slots, bool asNeeded, bool asDirected)
    {
        if ((asNeeded || asDirected) && slots.Count == 0)
            return "<span class=\"slot-note\">" + (asNeeded ? "as needed" : "as directed") + "</span>";
        var chosen = new HashSet<string>(slots);
        var mark = slots.Count > 0;
        string[] labels = ["BREAKFAST", "LUNCH", "DINNER", "BEDTIME"];
        var sb = new StringBuilder();
        for (var i = 0; i < labels.Length; i++)
        {
            if (i > 0) sb.Append(" - ");
            var kind = mark ? (chosen.Contains(labels[i]) ? "pill" : "faint") : "plain";
            sb.Append(SlotSpan(labels[i], kind));
        }
        return sb.ToString();
    }

    static string MealLine(string? meal)
    {
        var m = meal is "before" or "after" ? meal : null;
        var beforeKind = m is not null ? (m == "before" ? "pill" : "faint") : "plain";
        var afterKind = m is not null ? (m == "after" ? "pill" : "faint") : "plain";
        return SlotSpan("before/", beforeKind) + " " + SlotSpan("after", afterKind);
    }

    static string DurFill(string? kind, string want, int? value) =>
        kind == want && value is not null ? E(value.Value.ToString(CultureInfo.InvariantCulture)) : "";

    static bool IsWordChar(char c) => c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';

    static bool StartsWithWord(string text, string word)
    {
        if (word.Length == 0 || text.Length < word.Length) return false;
        if (text[..word.Length].ToLowerInvariant() != word.ToLowerInvariant()) return false;
        if (text.Length == word.Length) return true;
        return !IsWordChar(text[word.Length]);
    }

    static string CollapseDupFirstWord(string text)
    {
        var t = Regex.Replace(text, @"\s+", " ").Trim();
        var sp = t.IndexOf(' ');
        if (sp < 0) return t;
        var first = t[..sp];
        var rest = t[(sp + 1)..];
        var sp2 = rest.IndexOf(' ');
        var second = sp2 < 0 ? rest : rest[..sp2];
        if (first.ToLowerInvariant() != second.ToLowerInvariant()) return t;
        return first + (sp2 < 0 ? "" : rest[sp2..]);
    }

    internal static string SigFreeText(RxLine item)
    {
        var route = (item.Route ?? "").Trim();
        var instruction = (item.Instruction ?? "").Trim();
        string text;
        if (route.Length == 0) text = instruction;
        else if (instruction.Length == 0) text = route;
        else if (StartsWithWord(instruction, route)) text = instruction;
        else text = route + " " + instruction;
        return CollapseDupFirstWord(text);
    }

    static string? SavedMeal(string? mealRelation)
    {
        var m = (mealRelation ?? "").Trim().ToLowerInvariant();
        return m is "before" or "after" ? m : null;
    }

    static bool HasSavedStructured(RxLine item) =>
        (item.MealRelation ?? "").Trim().Length > 0 || (item.Timing ?? "").Trim().Length > 0 || item.AsNeeded.HasValue;

    static string RxRow(RxLine item)
    {
        string? meal;
        IReadOnlyList<string> slots;
        bool asNeeded, asDirected;
        if (HasSavedStructured(item))
        {
            meal = SavedMeal(item.MealRelation);
            slots = RxSig.TimingToSlots(item.Timing).Select(s => s.ToUpperInvariant()).ToList();
            asNeeded = item.AsNeeded == true;
            asDirected = false;
        }
        else
        {
            var parsed = RxSig.Parse(item.Instruction, item.Route, item.Timing, item.MealRelation);
            meal = parsed.Meal;
            slots = parsed.Slots;
            asNeeded = parsed.AsNeeded;
            asDirected = parsed.AsDirected;
        }
        var name = RxSig.DisplayName(item.GenericName, item.Dosage);
        var qty = item.Quantity;
        var sig = SigFreeText(item);
        var sigClass = "sig-free" + (sig.Length > 70 ? " sig-long" : "");
        var indication = item.Indication ?? "";
        var kind = item.DurationKind;
        var val = item.DurationValue;
        var maintain = kind == "Maintain" ? "✓" : "";
        return
            "<tr class=\"rx-row\">" +
              "<td class=\"rx-med\">" +
                "<div class=\"rx-med-top\">" +
                  "<div class=\"rx-med-name\">" + E(name) + "</div>" +
                  "<div class=\"rx-qty\"># <span class=\"u\">" + E(qty) + "</span></div>" +
                "</div>" +
                "<div class=\"rx-sig\">" +
                  "<span class=\"sig-k\">Sig.</span>" +
                  "<span class=\"" + sigClass + "\">" + E(sig) + "</span>" +
                  "<span class=\"meal\">" + MealLine(meal) + "</span>" +
                "</div>" +
                "<div class=\"rx-slots\">" + SlotLine(slots, asNeeded, asDirected) + "</div>" +
              "</td>" +
              "<td class=\"rx-dur\">" +
                "<div class=\"dur-m\"><span class=\"cb\">" + maintain + "</span> Maintain</div>" +
                "<div><span class=\"u dur-u\">" + DurFill(kind, "Days", val) + "</span> Days</div>" +
                "<div><span class=\"u dur-u\">" + DurFill(kind, "Weeks", val) + "</span> Weeks</div>" +
              "</td>" +
              "<td class=\"rx-ind\">" + E(indication) + "</td>" +
            "</tr>";
    }

    //JJQR - AI (2026-11-26) - GAV-14 doctor: "make our prescription BIG / more readable". Line-for-line port of rxLayout / rxRowHeight in Project02-fe print-forms-html.ts:
    // a height guess (hundredths of an inch) per printed row from Arial's letter widths and the printer's word wrapping, so the bigger print never runs into the signature / Next appointment.
    const int RxRowsSpace = 464; // 2.86in (under the table header) down to 7.50in (just above the signature block at 7.54in)
    const int RxOnePageMax = 6;
    const int RxRoomyMax = 3;
    const int RxAiryMax = 4;
    const int RxAiryExtra = 25; // .rx-airy adds 0.25in of padding to each row
    const int RxSpare = 8; // spare, in case a printer wraps a line more than the guess

    // Arial / Helvetica widths (1/1000 em) for the characters space (32) to ~ (126)
    static readonly int[] RxAfmRegular =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556,
        556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556, 1015, 667, 667, 722, 722, 667,
        611, 778, 722, 278, 500, 667, 556, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667,
        667, 611, 278, 278, 278, 469, 556, 333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500,
        222, 833, 556, 556, 556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];
    static readonly int[] RxAfmBold =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278, 556, 556, 556,
        556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611, 975, 722, 722, 722, 722, 667,
        611, 778, 722, 278, 556, 722, 611, 833, 722, 778, 667, 778, 722, 667, 611, 722, 667, 944, 667,
        667, 611, 333, 278, 333, 584, 556, 333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556,
        278, 889, 611, 611, 611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    // widths are 1/1000 em x font size in tenths of a pt = 1/10000 pt
    static long RxTextW(string text, bool bold, int size10)
    {
        long w = 0;
        foreach (var ch in text)
        {
            int c = ch;
            w += c >= 32 && c <= 126 ? (bold ? RxAfmBold : RxAfmRegular)[c - 32] : 1000;
        }
        return w * size10;
    }

    static int RxWrapLines(string text, bool bold, int size10, long avail)
    {
        var words = text.Split(' ');
        var space = RxTextW(" ", bold, size10);
        var lines = 1;
        long cur = 0;
        var any = false;
        foreach (var word in words)
        {
            if (word.Length == 0) continue;
            var w = RxTextW(word, bold, size10);
            if (!any)
            {
                cur = w;
                any = true;
            }
            else if (cur + space + w <= avail)
            {
                cur += space + w;
            }
            else
            {
                lines++;
                cur = w;
            }
            while (cur > avail)
            {
                lines++;
                cur -= avail;
            }
        }
        return lines;
    }

    //JJQR - AI (2026-11-27) - why: Dr. Allyn wants each diagnosis on its own ruled line of the medical certificate ("mu newline igka 2"). Split at print time on new lines, ";" and before every number marker after the first ("2)" / "2."), then give each item its own line; more lines than the 3 printed ones get extra ruled lines with tighter spacing so the certificate stays one page. Exact port of print-forms-html.ts splitDiagnosisLines / diagnosisBlock (golden tests compare the HTML).
    static readonly Regex DxMarker = new(@"(^|[ \t,])(?:([0-9]{1,2})[.)](?=[ \t]*[A-Za-z])|•(?=[ \t]*\S))", Js); //JJQR - AI (2026-11-27) - why: "Bullets •" list style: a "•" starts a new line too //JJQR - END
    const int DxLines = 3; // ruled lines printed on the pad
    const int DxLh = 230; // their spacing, thousandths of an inch (0.23in)
    const int DxSpace = 910; // 4.17in (top of the block) down to 5.08in (just above Recommendations at 5.10in)
    const long DxW = 3352320; // 4.80in writing width (A5 less the 0.50in / 0.53in margins) in 1/10000 pt, less 3% spare
    const int DxSize = 90; // 9pt
    const long DxLbl = 57600; // the label's 0.08in right padding, 1/10000 pt

    public static IReadOnlyList<string> SplitDiagnosisLines(string? text)
    {
        var outList = new List<string>();
        void Push(string s)
        {
            var t = Regex.Replace(Regex.Replace(s, @"\s+", " ").Trim(), "[ ,]+$", "", Js);
            if (t.Length > 0) outList.Add(t);
        }
        foreach (var part in Regex.Split(text ?? "", @"\r\n|\r|\n|;", Js))
        {
            var from = 0;
            foreach (Match m in DxMarker.Matches(part))
            {
                var at = m.Index + m.Groups[1].Length;
                if (part[..at].Trim().Length == 0) continue; // never before the first item
                Push(part[from..at]);
                from = at;
            }
            Push(part[from..]);
        }
        return outList;
    }

    //JJQR - AI (2026-11-27) - why: owner: "Each on its own line" = a bulleted list of single-line fields (5 at least, "+ Add line" for more). Rows <-> the saved diagnosis text:
    // rows join as "1) ...\n2) ..." (her numbering style, blanks dropped, a number she typed herself is not doubled); text splits back into rows without the "N)" markers. Exact port of print-forms-html.ts.
    public const int DxMinRows = 5;
    static readonly Regex DxLead = new(@"^[ \t]*(?:[0-9]{1,2}[.)](?![0-9])|•)[ \t]*", Js); //JJQR - AI (2026-11-27) - why: strip a "•" as well as "N)" //JJQR - END

    //JJQR - AI (2026-11-27) - why: owner: "make me choose bullets or numbers": rows are saved as "1) ..." (numbers, default) or "• ..." (bullets); the style is read back from the text (a leading "•" = bullets)
    public static bool DiagnosisIsBullets(string? text)
    {
        foreach (var line in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var t = line.Trim();
            if (t.Length > 0) return t[0] == '•';
        }
        return false;
    }
    //JJQR - END

    public static IReadOnlyList<string> DiagnosisRowsFromText(string? text)
    {
        var outList = new List<string>();
        foreach (var item in SplitDiagnosisLines(text))
        {
            var t = DxLead.Replace(item, "", 1).Trim();
            if (t.Length > 0) outList.Add(t);
        }
        return outList;
    }

    public static string DiagnosisTextFromRows(IEnumerable<string?> rows, bool bullets = false)
    {
        var outList = new List<string>();
        foreach (var r in rows)
        {
            var t = DxLead.Replace(Regex.Replace(r ?? "", @"\s+", " ").Trim(), "", 1).Trim();
            if (t.Length > 0) outList.Add((bullets ? "•" : (outList.Count + 1) + ")") + " " + t); //JJQR - AI (2026-11-27) - why: bullets or numbers //JJQR - END
        }
        return string.Join("\n", outList);
    }

    //JJQR - AI (2026-11-27) - why: owner: "give me paragraph style diagnosis". "Paragraph" prints her text as typed: her own new lines kept (runs of spaces / blank lines squeezed to one), wrapped over the ruled lines, no "; " joining, numbering or bullets. Exact port of print-forms-html.ts diagnosisParagraphLines / diagnosisLayoutFromText.
    public static IReadOnlyList<string> DiagnosisParagraphLines(string? text)
    {
        var outList = new List<string>();
        foreach (var line in Regex.Split(text ?? "", @"\r\n|\r|\n", Js))
        {
            var t = Regex.Replace(line, @"\s+", " ").Trim();
            if (t.Length > 0) outList.Add(t);
            else if (outList.Count > 0 && outList[^1].Length > 0) outList.Add(""); // one blank line between paragraphs
        }
        while (outList.Count > 0 && outList[^1].Length == 0) outList.RemoveAt(outList.Count - 1);
        return outList;
    }

    /// <summary>Which "Print as" a saved diagnosis was written in (nothing is stored, so it is read back from the text):
    /// empty = Lines (List, the default); any line starting with "1)" / "1." / "•" = Lines; anything else (plain text, ";" too) = Paragraph.</summary>
    public static DiagnosisLayout DiagnosisLayoutFromText(string? text)
    {
        var any = false;
        foreach (var line in Regex.Split(text ?? "", @"\r\n|\r|\n", Js))
        {
            if (line.Trim().Length == 0) continue;
            any = true;
            if (DxLead.IsMatch(line)) return DiagnosisLayout.Lines;
        }
        return any ? DiagnosisLayout.Paragraph : DiagnosisLayout.Lines; //JJQR - AI (2026-11-27) - why: no "One line" any more; ";" stays in her paragraph //JJQR - END
    }
    //JJQR - END

    /// <summary>The rows to show: the text's items, padded with empty rows to at least <see cref="DxMinRows"/>.</summary>
    public static IReadOnlyList<string> DiagnosisRowsForEdit(string? text)
    {
        var rows = DiagnosisRowsFromText(text).ToList();
        while (rows.Count < DxMinRows) rows.Add("");
        return rows;
    }
    //JJQR - END

    static int DxWrapLines(string text, long firstAvail, long avail)
    {
        var words = text.Split(' ');
        var space = RxTextW(" ", false, DxSize);
        var lines = 1;
        var room = firstAvail;
        long cur = 0;
        var any = false;
        foreach (var word in words)
        {
            if (word.Length == 0) continue;
            var w = RxTextW(word, false, DxSize);
            if (!any)
            {
                cur = w;
                any = true;
            }
            else if (cur + space + w <= room)
            {
                cur += space + w;
            }
            else
            {
                lines++;
                room = avail;
                cur = w;
            }
            while (cur > room)
            {
                lines++;
                cur -= room;
                room = avail;
            }
        }
        return lines;
    }

    static string DxInches(int mils) => (mils / 1000) + "." + (mils % 1000).ToString("000", CultureInfo.InvariantCulture) + "in";

    static string DiagnosisBlock(string? text, DiagnosisLayout layout)
    {
        var split = layout == DiagnosisLayout.Paragraph ? DiagnosisParagraphLines(text) : SplitDiagnosisLines(text); //JJQR - AI (2026-11-27) - why: "Paragraph": her own lines, wrapped like sentences //JJQR - END
        var items = split; //JJQR - AI (2026-11-27) - why: "One line" (joined with "; ") removed //JJQR - END
        var lines = 0;
        var first = DxW - RxTextW("Diagnosis/ Impressions:", false, DxSize) - DxLbl;
        for (var i = 0; i < items.Count; i++) lines += DxWrapLines(items[i], i == 0 ? first : DxW, DxW);
        var n = lines > DxLines ? lines : DxLines;
        var style = "";
        if (n > DxLines)
        {
            var fit = DxSpace / n;
            var lh = fit < DxLh ? fit : DxLh;
            var fs = lh >= 170 ? "9pt" : lh >= 140 ? "8pt" : lh >= 115 ? "7pt" : "6pt";
            style = " style=\"--dx-lh:" + DxInches(lh) + ";--dx-h:" + DxInches(lh * n) + ";--dx-fs:" + fs + "\"";
        }
        var rules = new StringBuilder();
        for (var i = 0; i < n; i++) rules.Append("<div class=\"wl\"></div>");
        return "<div class=\"write mc-diag\"" + style + "><div class=\"write-lines\">" + rules + "</div>" +
            "<div class=\"write-text\">" +
            "<span class=\"write-lbl\">Diagnosis/ Impressions:</span>" +
            string.Join("<br>", items.Select(E)) +
            "</div></div>";
    }
    //JJQR - END

    static int RxRowHeight(RxLine item, bool roomy)
    {
        var name = RxSig.DisplayName(item.GenericName, item.Dosage);
        var qty = item.Quantity ?? "";
        var sig = SigFreeText(item);
        var isLong = sig.Length > 70;
        var nameSize = roomy ? 130 : 105;
        var sigSize = roomy ? (isLong ? 95 : 105) : (isLong ? 80 : 85);
        var labelSize = roomy ? 105 : 85;
        var mealSize = roomy ? 95 : 80;
        var indSize = roomy ? 95 : 85;
        long inner = roomy ? 1976000 : 1992000; // the medicine cell inside its padding (2.745in / 2.766in)
        var qtyU = RxTextW(qty, true, nameSize) + 15000;
        var nameAvail = inner - 45000 - RxTextW("# ", true, nameSize) - (qtyU > 288000 ? qtyU : 288000);
        var mealW = RxTextW("before/", true, mealSize) + RxTextW(" ", false, mealSize) + RxTextW("after", true, mealSize) + 52500;
        var sigAvail = inner - RxTextW("Sig.", true, labelSize) - 90000 - mealW;
        long indAvail = roomy ? 572000 : 587000;
        var nameLines = RxWrapLines(name, true, nameSize, nameAvail);
        var sigLines = RxWrapLines(sig, false, sigSize, sigAvail);
        var indLines = RxWrapLines(item.Indication ?? "", false, indSize, indAvail);
        // line heights from the CSS; the fixed parts were fitted on 800 rendered rows per size
        var med = roomy ? 58 + nameLines * 21 + sigLines * 17 : 30 + nameLines * 17 + sigLines * 14;
        var ind = roomy ? 20 + indLines * 16 : 10 + indLines * 15;
        return med > ind ? med : ind;
    }

    /// <summary>Biggest size that still fits one A5 page: roomy (1-3 medicines) &gt; airy (up to 4) &gt; normal (up to 6); otherwise normal size in the flowing "form-tall" layout (same as the web).</summary>
    public static (bool Roomy, bool Airy, bool Tall) RxLayout(IReadOnlyList<RxLine> items)
    {
        bool Fits(bool roomy, int extra)
        {
            var total = RxSpare;
            foreach (var it in items) total += RxRowHeight(it, roomy) + extra;
            return total <= RxRowsSpace;
        }
        if (items.Count <= RxRoomyMax && Fits(true, 0)) return (true, false, false);
        if (items.Count <= RxAiryMax && Fits(false, RxAiryExtra)) return (false, true, false);
        if (items.Count <= RxOnePageMax && Fits(false, 0)) return (false, false, false);
        return (false, false, true);
    }

    /// <summary>True when the prescription needs the flowing "form-tall" layout (more than six lines, or rows too tall for one page).</summary>
    public static bool IsTallPrescription(IReadOnlyList<RxLine> items) => RxLayout(items).Tall;
    //JJQR - END

    public static string BuildPrescriptionHtml(RxHtmlOptions opts)
    {
        var rows = string.Concat(opts.Items.Select(RxRow));
        var layout = RxLayout(opts.Items); //JJQR - AI (2026-11-26) - GAV-14 bigger print: size + one page or flowing, same as the web //JJQR - END
        var tall = layout.Tall;
        var css = FormsCss.ShellCss(opts.PageSize, "148mm", "210mm") + FormsCss.A5LetterheadCss("1.58in") + FormsCss.PrescriptionCss();

        var next = opts.NextAppointment ?? "";
        var inner =
            LetterheadHtml(opts.Origin) +
            "<div class=\"rule-double\"></div>" +
            "<div class=\"rx-name\"><span>Patient's Name:</span><span class=\"u grow\">" + E(opts.Patient.FullName) + "</span></div>" +
            "<div class=\"rx-age\"><span>Age:</span><span class=\"u age\">" + E(opts.Age) + "</span></div>" +
            "<div class=\"rx-sex\"><span>Sex:</span><span class=\"u sex\">" + E(opts.Patient.Sex) + "</span></div>" +
            "<div class=\"rx-addr\"><span>Address:</span><span class=\"u grow\">" + E(opts.Patient.Address) + "</span></div>" +
            "<div class=\"rx-date\"><span>Date:</span><span class=\"u date\">" + E(opts.Date) + "</span></div>" +
            "<img class=\"rx-img\" src=\"" + Asset(opts.Origin, "rx.png") + "\" alt=\"Rx\" />" +
            "<table class=\"rx-tbl\">" +
              "<colgroup><col class=\"col-med\" /><col class=\"col-dur\" /><col class=\"col-ind\" /></colgroup>" +
              "<thead><tr><td class=\"h-empty\"></td><th>DURATION</th><th>INDICATION</th></tr></thead>" +
              "<tbody>" + rows + "</tbody>" +
            "</table>" +
            "<div class=\"rx-next\"><span>Next appointment:</span><span class=\"u next-u\">" + E(next) + "</span></div>" +
            "<div class=\"rx-sig-block\">" + SignatureBlock(opts.Doctor) + "</div>";

        return "<style>" + css + "</style>" +
               "<div class=\"form form-rx" + (tall ? " form-tall" : "") + (layout.Roomy ? " rx-roomy" : layout.Airy ? " rx-airy" : "") + "\">" + inner + "</div>"; //JJQR - AI (2026-11-26) - GAV-14 roomy/airy size class //JJQR - END
    }

    static string NormLab(string s) => Regex.Replace(s.ToLowerInvariant(), "[^a-z0-9]+", " ", Js).Trim();

    internal static bool MatchLabItem(string ordered, string item)
    {
        var o = ordered.ToLowerInvariant();
        var n = NormLab(ordered);
        var i = NormLab(item);
        if (n == i) return true;
        if (i.Contains("dengue") && o.Contains("dengue")) return true;
        if (i.Contains("ecg") && o.Contains("ecg")) return true;
        if (item == "CREA" && (n == "crea" || o.Contains("creatinine"))) return true;
        if (i.Length > 0 && (" " + n + " ").Contains(" " + i + " ")) return true; //JJQR - AI (Nov 24, 2026) - the whole test name appears in what was ordered //JJQR - END
        if (item == "LIPID PROFILE" && o.Contains("lipid")) return true;
        //JJQR - AI (Nov 24, 2026) - thyroid and HbA1c names as a doctor might type them
        if (item == "TSH" && (Regex.IsMatch(" " + n + " ", "(^| )tsh( |$)", Js) || o.Contains("thyroid stimulating"))) return true;
        if (item == "FT3" && (Regex.IsMatch(" " + n + " ", "(^| )ft3( |$)", Js) || Regex.IsMatch(o, "free t ?3", Js))) return true;
        if (item == "FT4" && (Regex.IsMatch(" " + n + " ", "(^| )ft4( |$)", Js) || Regex.IsMatch(o, "free t ?4", Js))) return true;
        if (item == "HbA1c" && (Regex.IsMatch(o, "hb ?a1c", Js) || o.Contains("glycated") || o.Contains("glycosylated") || Regex.IsMatch(" " + n + " ", "(^| )a1c( |$)", Js))) return true;
        //JJQR - END
        if (item == "URINALYSIS" && (o.Contains("urinalysis") || Regex.IsMatch(" " + n + " ", "(^| )urine( |$)", Js))) return true;
        return false;
    }

    /// <summary>Which pre-printed checklist rows get a tick, and which tests are hand-written in the margin.</summary>
    public static (IReadOnlySet<string> Checked, IReadOnlyList<string> Extras) LabMatch(IEnumerable<string> tests, IReadOnlyList<string> items)
    {
        var chk = new HashSet<string>();
        var extras = new List<string>();
        foreach (var test in tests)
        {
            var hit = "";
            foreach (var name in items)
                if (MatchLabItem(test, name)) { hit = name; break; }
            if (hit.Length > 0) chk.Add(hit);
            else extras.Add(test);
        }
        return (chk, extras);
    }

    static string LabItemHtml(string name, bool on, int c, string top)
    {
        var tick = on ? "<span class=\"tick\">✓</span>" : "";
        return "<div class=\"lab-item lab-c" + c + "\" style=\"top:" + top + "\"><span class=\"lab-blank\">" + tick + "___</span><span class=\"lab-name\">" + E(name) + "</span></div>";
    }

    //JJQR - AI (Nov 24, 2026) - owner: the lab request is re-laid out on the SAME A5 sheet as the prescription and the medical certificate (same letterhead, same patient lines, same signature block). Mirrors the web builder.
    public static string BuildLabRequestHtml(LabHtmlOptions opts)
    {
        var items = NormalizeLabItems(opts.Items);
        var (chk, extrasList) = LabMatch(opts.Tests, items);
        // two columns, filled top to bottom; with more than 14 tests the rows move closer so everything still fits above "Others:"
        var perCol = (items.Count + 1) / 2;
        var step = perCol <= 7 ? 40 : 240 / (perCol - 1);
        var list = string.Concat(items.Select((name, i) =>
        {
            var col = i < perCol ? 0 : 1;
            var row = col == 0 ? i : i - perCol;
            return LabItemHtml(name, chk.Contains(name), col, LabTop(295 + row * step));
        }));

        var extras = extrasList.Count > 0
            ? "<div class=\"lab-others\"><div class=\"lab-others-lbl\">Others:</div>" + string.Concat(extrasList.Select(x => "<div class=\"lab-extra\">" + E(x) + "</div>")) + "</div>"
            : "";

        var dxBits = new List<string>();
        if (opts.Diagnoses is { Count: > 0 }) dxBits.Add(string.Join("; ", opts.Diagnoses));
        if (!string.IsNullOrWhiteSpace(opts.ClinicalNotes) && opts.ClinicalNotes.Trim().Length > 0) dxBits.Add(opts.ClinicalNotes.Trim());
        var dx = dxBits.Count > 0
            ? "<div class=\"lab-dx\"><span class=\"lab-dx-lbl\">Dx:</span> " + E(string.Join(" — ", dxBits)) + "</div>"
            : "";

        var css = FormsCss.ShellCss(opts.PageSize, "148mm", "210mm") + FormsCss.A5LetterheadCss("1.58in") + FormsCss.LabRequestCss();

        var inner =
            LetterheadHtml(opts.Origin) +
            "<div class=\"rule-double\"></div>" +
            "<div class=\"rx-name\"><span>Patient's Name:</span><span class=\"u grow\">" + E(opts.Patient.FullName) + "</span></div>" +
            "<div class=\"rx-age\"><span>Age:</span><span class=\"u age\">" + E(opts.Age) + "</span></div>" +
            "<div class=\"rx-sex\"><span>Sex:</span><span class=\"u sex\">" + E(opts.Patient.Sex) + "</span></div>" +
            "<div class=\"rx-addr\"><span>Address:</span><span class=\"u grow\">" + E(opts.Patient.Address) + "</span></div>" +
            "<div class=\"rx-date\"><span>Date:</span><span class=\"u date\">" + E(opts.Date) + "</span></div>" +
            "<div class=\"lab-rx\">R<span class=\"lab-rx-x\">x</span></div>" +
            "<div class=\"lab-list\">" + list + "</div>" +
            extras +
            dx +
            "<img class=\"lab-cartoon\" src=\"" + Asset(opts.Origin, "cartoon.png") + "\" alt=\"\" />" +
            "<div class=\"rx-sig-block\">" + SignatureBlock(opts.Doctor) + "</div>";

        return "<style>" + css + "</style><div class=\"form form-lab\">" + inner + "</div>";
    }
    //JJQR - END
}
//JJQR - END
