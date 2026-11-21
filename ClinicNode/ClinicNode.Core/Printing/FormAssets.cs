//JJQR - AI (Nov 21, 2026) - the web prints /forms/logo.png, seal.png, rx.png, cartoon.png from its own origin. Natively there is no origin,
// so the builders run with origin "" and InlineAssets() swaps src="/forms/x.png" for data: URIs (same bytes, see Generated/FormAssets.g.cs).
namespace ClinicNode.Core.Printing;

public static partial class FormAssets
{
    public static IReadOnlyCollection<string> Names => (IReadOnlyCollection<string>)Base64.Keys;

    public static string DataUri(string name) =>
        Base64.TryGetValue(name, out var b64) ? "data:image/png;base64," + b64 : throw new KeyNotFoundException(name);

    public static byte[] Bytes(string name) => Convert.FromBase64String(Base64[name]);

    /// <summary>Replace <c>src="{origin}/forms/{name}"</c> with an inline data URI for each known form image.</summary>
    public static string InlineAssets(string html, string origin = "")
    {
        foreach (var name in Base64.Keys)
            html = html.Replace("src=\"" + ClinicFormsHtml.EscapeHtml(origin) + "/forms/" + name + "\"", "src=\"" + DataUri(name) + "\"");
        return html;
    }
}
//JJQR - END
