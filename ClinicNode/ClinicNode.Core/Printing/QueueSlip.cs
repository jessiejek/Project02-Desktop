//JJQR - AI (Nov 21, 2026) - queue slip: (1) the web WalkInWizard slip exactly, (2) an 80/58 mm thermal roll layout, (3) raw ESC/POS bytes.
using System.Globalization;
using System.Text;

namespace ClinicNode.Core.Printing;

public static class QueueSlip
{
    static string E(string? s) => ClinicFormsHtml.EscapeHtml(s ?? "");

    /// <summary>JS <c>`${number}`</c> for a peso amount: 450 → "450", 450.5 → "450.5".</summary>
    public static string JsAmount(decimal fee) => fee.ToString("0.############################", CultureInfo.InvariantCulture);

    /// <summary>JS <c>new Date(issued_at).toLocaleString()</c> in an en-US / Asia/Manila webview: "11/21/2026, 2:15:00 PM".</summary>
    public static string IssuedAtText(DateTimeOffset issuedAt) =>
        TimeZoneInfo.ConvertTime(issuedAt, ClinicPrintForms.Manila).ToString("M/d/yyyy, h:mm:ss tt", CultureInfo.InvariantCulture);

    public static string VisitLabel(string visitType) => visitType == "FollowUp" ? "Follow-up" : "New";

    public const string PriorityText = "PRIORITY LANE · Senior / PWD";

    /// <summary>Body HTML of the WalkInWizard "Print slip" button (verbatim markup).</summary>
    public static string WebBodyHtml(QueueSlipData t) =>
        $"""
        <div class="slip">
                            <div class="brand">{E(t.ClinicName)}</div>
                            <p class="muted">{E(t.ClinicAddress)}</p>
                            <div class="queue">{E(t.QueueNumber)}</div>
                            {(t.IsPriority ? $"<p><strong>{PriorityText}</strong></p>" : "")}
                            <p><strong>{E(t.PatientName)}</strong> ({E(t.PatientCode)})</p>
                            <p class="muted">{E(t.DoctorName)}</p>
                            <p class="muted">{E(VisitLabel(t.VisitType))} · provisional ₱{JsAmount(t.ProvisionalFee)}</p>
                            <p class="muted">{E(IssuedAtText(t.IssuedAt))}</p>
                           </div>
        """;

    /// <summary>The slip exactly as the web prints it: <c>printHtml(`Queue ${n}`, body)</c> with the default chrome and brand line.</summary>
    public static PrintDocument Web(QueueSlipData t) =>
        new($"Queue {t.QueueNumber}", PrintPaper.PrinterDefault, PrintHtmlDocument.Wrap($"Queue {t.QueueNumber}", WebBodyHtml(t), bare: false));

    /// <summary>Same content laid out for a thermal roll: no dashed card, black only, width = printable head width.</summary>
    public static PrintDocument Thermal(QueueSlipData t, PrintPageSize roll)
    {
        if (PrintPaper.KindOf(roll) != PaperKind.Roll) throw new ArgumentException("Thermal slips need a roll paper size.", nameof(roll));
        var paperMm = roll.Unit == "mm" ? roll.Width : roll.Width * 25.4;
        var printable = PrintPaper.PrintableWidthMm(roll);
        var side = (paperMm - printable) / 2;
        var small = printable < 60;
        var css = string.Create(CultureInfo.InvariantCulture, $$"""
@page { margin: 0; }
html, body { margin: 0; padding: 0; background: #fff; }
body { width: {{paperMm}}mm; font-family: Arial, Helvetica, sans-serif; color: #000; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
.roll { box-sizing: border-box; width: {{paperMm}}mm; padding: 3mm {{side}}mm 2mm; text-align: center; }
.roll p { margin: 0 0 1.2mm; font-size: {{(small ? 8 : 9.5)}}pt; line-height: 1.25; }
.roll .clinic { font-size: {{(small ? 9 : 11)}}pt; font-weight: 700; text-transform: uppercase; letter-spacing: 0.04em; }
.roll .queue { font-size: {{(small ? 30 : 40)}}pt; font-weight: 700; line-height: 1.05; margin: 2mm 0; }
.roll .prio { font-weight: 700; border: 1px solid #000; display: inline-block; padding: 0.4mm 1.5mm; }
.roll .who { font-weight: 700; font-size: {{(small ? 9 : 11)}}pt; }
.roll hr { border: 0; border-top: 1px dashed #000; margin: 2mm 0; }
""");
        var body =
            "<div class=\"roll\">" +
            "<p class=\"clinic\">" + E(t.ClinicName) + "</p>" +
            (string.IsNullOrWhiteSpace(t.ClinicAddress) ? "" : "<p>" + E(t.ClinicAddress) + "</p>") +
            "<hr />" +
            "<div class=\"queue\">" + E(t.QueueNumber) + "</div>" +
            (t.IsPriority ? "<p><span class=\"prio\">" + PriorityText + "</span></p>" : "") +
            "<p class=\"who\">" + E(t.PatientName) + "</p>" +
            "<p>" + E(t.PatientCode) + "</p>" +
            "<p>" + E(t.DoctorName) + "</p>" +
            "<p>" + E(VisitLabel(t.VisitType)) + " · provisional ₱" + JsAmount(t.ProvisionalFee) + "</p>" +
            "<hr />" +
            "<p>" + E(IssuedAtText(t.IssuedAt)) + "</p>" +
            "</div>";
        var title = $"Queue {t.QueueNumber}";
        var html = "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n  <meta charset=\"utf-8\" />\n  <title>" + E(title) + "</title>\n  <style>\n" + css + "\n  </style>\n</head>\n<body>\n  " + body + "\n</body>\n</html>";
        return new PrintDocument(title, roll, html);
    }

    /// <summary>Plain-text lines of the thermal slip (used by ESC/POS and for tests). ₱ is spelled "PHP" because thermal code pages lack it.</summary>
    public static IReadOnlyList<(string Text, EscPosStyle Style)> ThermalLines(QueueSlipData t)
    {
        var lines = new List<(string, EscPosStyle)>
        {
            (t.ClinicName.ToUpperInvariant(), EscPosStyle.Bold),
        };
        if (!string.IsNullOrWhiteSpace(t.ClinicAddress)) lines.Add((t.ClinicAddress, EscPosStyle.Normal));
        lines.Add(("", EscPosStyle.Rule));
        lines.Add((t.QueueNumber, EscPosStyle.Huge));
        if (t.IsPriority) lines.Add(("PRIORITY LANE - Senior / PWD", EscPosStyle.Bold));
        lines.Add((t.PatientName, EscPosStyle.Bold));
        lines.Add((t.PatientCode, EscPosStyle.Normal));
        lines.Add((t.DoctorName, EscPosStyle.Normal));
        lines.Add(($"{VisitLabel(t.VisitType)} - provisional PHP {JsAmount(t.ProvisionalFee)}", EscPosStyle.Normal));
        lines.Add(("", EscPosStyle.Rule));
        lines.Add((IssuedAtText(t.IssuedAt), EscPosStyle.Normal));
        return lines;
    }

    public static byte[] EscPos(QueueSlipData t, PrintPageSize roll, bool cut = true) =>
        new EscPosBuilder(PrintPaper.EscPosColumns(roll)).Slip(ThermalLines(t), cut).ToArray();
}

public enum EscPosStyle { Normal, Bold, Huge, Rule }

/// <summary>Minimal ESC/POS command builder (Epson TM / XPrinter / most 58/80 mm POS printers). Text is sent as ASCII (PC437 subset).</summary>
public sealed class EscPosBuilder
{
    readonly List<byte> _buf = [];
    public int Columns { get; }

    public EscPosBuilder(int columns) => Columns = columns > 0 ? columns : 48;

    public EscPosBuilder Raw(params byte[] bytes) { _buf.AddRange(bytes); return this; }
    public EscPosBuilder Init() => Raw(0x1B, 0x40);                       // ESC @
    public EscPosBuilder Align(int n) => Raw(0x1B, 0x61, (byte)n);         // ESC a n (0 left, 1 centre, 2 right)
    public EscPosBuilder Bold(bool on) => Raw(0x1B, 0x45, (byte)(on ? 1 : 0)); // ESC E n
    public EscPosBuilder Size(int w, int h) => Raw(0x1D, 0x21, (byte)(((w - 1) << 4) | (h - 1))); // GS ! n
    public EscPosBuilder Feed(int lines) => Raw(0x1B, 0x64, (byte)lines);  // ESC d n
    public EscPosBuilder Cut() => Raw(0x1D, 0x56, 0x42, 0x00);             // GS V 66 0 (feed + partial cut)

    public EscPosBuilder Text(string s)
    {
        foreach (var ch in s) _buf.Add(ToAscii(ch));
        return this;
    }

    public EscPosBuilder Line(string s = "") => Text(s).Raw(0x0A);

    static byte ToAscii(char ch) => ch switch
    {
        < (char)0x20 => (byte)'?',
        < (char)0x7F => (byte)ch,
        '·' or '•' => (byte)'-',
        '—' or '–' => (byte)'-',
        'ñ' => 0xA4,
        'Ñ' => 0xA5,
        _ => (byte)'?',
    };

    /// <summary>Greedy word wrap at <paramref name="width"/> columns (long words are hard-split).</summary>
    public static IReadOnlyList<string> Wrap(string text, int width)
    {
        var result = new List<string>();
        if (width <= 0) width = 1;
        foreach (var para in (text ?? "").Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var w = word;
                while (w.Length > width)
                {
                    if (line.Length > 0) { result.Add(line.ToString()); line.Clear(); }
                    result.Add(w[..width]);
                    w = w[width..];
                }
                if (line.Length == 0) line.Append(w);
                else if (line.Length + 1 + w.Length <= width) line.Append(' ').Append(w);
                else { result.Add(line.ToString()); line.Clear().Append(w); }
            }
            result.Add(line.ToString());
        }
        return result;
    }

    public EscPosBuilder Slip(IEnumerable<(string Text, EscPosStyle Style)> lines, bool cut)
    {
        Init().Align(1);
        foreach (var (text, style) in lines)
        {
            switch (style)
            {
                case EscPosStyle.Rule:
                    Line(new string('-', Columns));
                    break;
                case EscPosStyle.Huge:
                    Size(3, 3).Bold(true);
                    foreach (var l in Wrap(text, Math.Max(1, Columns / 3))) Line(l);
                    Bold(false).Size(1, 1);
                    break;
                case EscPosStyle.Bold:
                    Bold(true);
                    foreach (var l in Wrap(text, Columns)) Line(l);
                    Bold(false);
                    break;
                default:
                    foreach (var l in Wrap(text, Columns)) Line(l);
                    break;
            }
        }
        Feed(3);
        if (cut) Cut();
        return this;
    }

    public byte[] ToArray() => [.. _buf];
}
//JJQR - END
