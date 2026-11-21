//JJQR - AI (Nov 21, 2026) - port of the document wrapper in Project02-fe src/lib/print.ts printHtml() (the auto-print <script> is omitted; the native printer drives printing).
namespace ClinicNode.Core.Printing;

public static class PrintHtmlDocument
{
    /// <summary>Default brand line of printHtml when no brand option is passed.</summary>
    public const string DefaultBrand = "Dr. Grace Gavino Clinic";

    const string BareChromeCss = "html, body { margin: 0; padding: 0; }";

    // Verbatim copy of the non-bare chromeCss template literal in print.ts (indentation included).
    const string ChromeCss = """
@page { margin: 16mm; }
    body {
      font-family: ui-sans-serif, system-ui, -apple-system, Segoe UI, sans-serif;
      color: #0b1c30;
      font-size: 13px;
      line-height: 1.45;
      margin: 0;
      padding: 24px;
    }
    h1 { font-size: 20px; margin: 0 0 4px; }
    h2 { font-size: 15px; margin: 20px 0 8px; border-bottom: 1px solid #bcc9c6; padding-bottom: 4px; }
    .meta { color: #3d4947; font-size: 12px; margin-bottom: 16px; }
    .card { border: 1px solid #bcc9c6; border-radius: 8px; padding: 12px 14px; margin-bottom: 12px; }
    .muted { color: #3d4947; }
    table { width: 100%; border-collapse: collapse; }
    th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid #e5eeff; vertical-align: top; }
    th { font-size: 11px; text-transform: uppercase; letter-spacing: 0.04em; color: #3d4947; }
    .slip {
      border: 2px dashed #bcc9c6;
      border-radius: 12px;
      padding: 28px 20px;
      text-align: center;
      max-width: 360px;
      margin: 0 auto;
    }
    .slip .queue { font-size: 28px; font-weight: 700; color: #00685f; margin: 8px 0; }
    .brand { font-size: 11px; text-transform: uppercase; letter-spacing: 0.08em; color: #6d7a77; margin-bottom: 12px; }
""";

    /// <summary>
    /// Same document printHtml() writes. <paramref name="bare"/> = clinic forms (form CSS owns the page, no brand line).
    /// <paramref name="brand"/>: null = default brand, "" = omit (web <c>brand: null</c>).
    /// </summary>
    public static string Wrap(string title, string bodyHtml, bool bare, string? brand = null, string? extraCss = null)
    {
        var effectiveBrand = bare ? "" : brand ?? DefaultBrand;
        var chrome = bare ? BareChromeCss : ChromeCss;
        if (!string.IsNullOrEmpty(extraCss)) chrome += "\n    " + extraCss;
        return "<!DOCTYPE html>\n" +
               "<html lang=\"en\">\n" +
               "<head>\n" +
               "  <meta charset=\"utf-8\" />\n" +
               "  <title>" + ClinicFormsHtml.EscapeHtml(title) + "</title>\n" +
               "  <style>\n" +
               "    " + chrome + "\n" +
               "  </style>\n" +
               "</head>\n" +
               "<body>\n" +
               "  " + (effectiveBrand.Length > 0 ? "<div class=\"brand\">" + ClinicFormsHtml.EscapeHtml(effectiveBrand) + "</div>" : "") + "\n" +
               "  " + bodyHtml + "\n" +
               "</body>\n" +
               "</html>";
    }

    /// <summary>print-forms.ts printForm(): <c>printHtml(title, body, { brand: null, bare: true })</c>.</summary>
    public static string WrapForm(string title, string bodyHtml) => Wrap(title, bodyHtml, bare: true);
}
//JJQR - END
