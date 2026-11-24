//JJQR - AI (Nov 21, 2026) - byte-for-byte parity of ClinicFormsHtml / RxSig with the real web builders. Fixtures come from golden/generate-golden.mjs
// (node, Project02-fe/src/lib). Found by walking up from the test binary because the test csproj is not ours to edit.
using System.Runtime.CompilerServices;
using System.Text.Json;
using ClinicNode.Core.Printing;

namespace ClinicNode.Tests.Printing;

public class GoldenParityTests
{
    // JJQR - AI (Nov 21, 2026) - CallerFilePath finds fixtures even when --artifacts-path moves AppContext.BaseDirectory
    static string GoldenDir([CallerFilePath] string? file = null)
    {
        if (!string.IsNullOrEmpty(file))
        {
            var nextToTest = Path.Combine(Path.GetDirectoryName(file)!, "golden");
            if (File.Exists(Path.Combine(nextToTest, "forms.json"))) return nextToTest;
        }
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "Printing", "golden");
            if (File.Exists(Path.Combine(p, "forms.json"))) return p;
            p = Path.Combine(d.FullName, "ClinicNode.Tests", "Printing", "golden");
            if (File.Exists(Path.Combine(p, "forms.json"))) return p;
        }
        throw new FileNotFoundException("golden/forms.json not found; run node golden/generate-golden.mjs");
    }
    // JJQR - END

    static JsonElement Load(string name) => JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenDir(), name))).RootElement.Clone();

    static string? S(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static string Str(JsonElement e, string n) => S(e, n) ?? "";

    static PrintDoctor Doctor(JsonElement e) => new(Str(e, "full_name"), S(e, "license_number"), S(e, "ptr_number"), S(e, "s2_number"));
    static PrintPatient Patient(JsonElement e) => new(Str(e, "full_name"), S(e, "patient_code"), S(e, "date_of_birth"), S(e, "sex"), S(e, "address"));

    static RxLine Line(JsonElement e) => new()
    {
        GenericName = Str(e, "generic_name"), Dosage = Str(e, "dosage"), Quantity = Str(e, "quantity"), Instruction = S(e, "instruction"),
        Timing = S(e, "timing"), MealRelation = S(e, "meal_relation"), Route = S(e, "route"), Indication = S(e, "indication"),
        AsNeeded = e.TryGetProperty("as_needed", out var an) && an.ValueKind is JsonValueKind.True or JsonValueKind.False ? an.GetBoolean() : null,
        DurationKind = S(e, "duration_kind"),
        DurationValue = e.TryGetProperty("duration_value", out var dv) && dv.ValueKind == JsonValueKind.Number ? dv.GetInt32() : null,
    };

    static List<string> Strings(JsonElement e, string n) =>
        e.TryGetProperty(n, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString()!).ToList() : [];

    public static IEnumerable<object[]> FormCases() => Enumerable.Range(0, Load("forms.json").GetArrayLength()).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(FormCases))]
    public void Form_html_is_identical_to_the_web_builder(int index)
    {
        var c = Load("forms.json")[index];
        var i = c.GetProperty("input");
        var kind = Str(c, "kind");
        var expected = Str(c, "html");
        string actual = kind switch
        {
            "rx" => ClinicFormsHtml.BuildPrescriptionHtml(new RxHtmlOptions(Str(i, "origin"), Str(i, "pageSize"), Doctor(i.GetProperty("doctor")),
                Patient(i.GetProperty("patient")), Str(i, "age"), Str(i, "date"), i.GetProperty("items").EnumerateArray().Select(Line).ToList(), S(i, "nextAppointment"))),
            "mc" => ClinicFormsHtml.BuildMedicalCertificateHtml(new MedCertHtmlOptions(Str(i, "origin"), Str(i, "pageSize"), Doctor(i.GetProperty("doctor")),
                Patient(i.GetProperty("patient")), Str(i, "issueDate"), Str(i, "address"), Str(i, "examinedAt"), Str(i, "examFrom"), Str(i, "examTo"),
                Str(i, "diagnosis"), Str(i, "recommendations"), Str(i, "purposeException"), Str(i, "comeBackOn"))),
            "lab" => ClinicFormsHtml.BuildLabRequestHtml(new LabHtmlOptions(Str(i, "origin"), Str(i, "pageSize"), Doctor(i.GetProperty("doctor")),
                Patient(i.GetProperty("patient")), Str(i, "age"), Str(i, "date"), Strings(i, "tests"),
                i.TryGetProperty("diagnoses", out _) ? Strings(i, "diagnoses") : null, S(i, "clinicalNotes"),
                i.TryGetProperty("items", out _) ? Strings(i, "items") : null)),
            _ => throw new InvalidOperationException(kind),
        };
        if (expected != actual)
        {
            var at = 0;
            while (at < expected.Length && at < actual.Length && expected[at] == actual[at]) at++;
            Assert.Fail($"{kind} #{index} differs at char {at}:\nweb: …{expected.Substring(Math.Max(0, at - 60), Math.Min(160, expected.Length - Math.Max(0, at - 60)))}\ncs : …{actual.Substring(Math.Max(0, at - 60), Math.Min(160, actual.Length - Math.Max(0, at - 60)))}");
        }
    }

    public static IEnumerable<object[]> SigCases() => Enumerable.Range(0, Load("rxsig.json").GetProperty("sigCases").GetArrayLength()).Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(SigCases))]
    public void Sig_parse_matches_web(int index)
    {
        var c = Load("rxsig.json").GetProperty("sigCases")[index];
        var i = c.GetProperty("input");
        var r = RxSig.Parse(S(i, "instruction"), S(i, "route"), S(i, "timing"), S(i, "mealRelation"));
        var e = c.GetProperty("result");
        Assert.Equal(S(e, "meal"), r.Meal);
        Assert.Equal(e.GetProperty("slots").EnumerateArray().Select(x => x.GetString()!).ToList(), r.Slots.ToList());
        Assert.Equal(e.GetProperty("asNeeded").GetBoolean(), r.AsNeeded);
        Assert.Equal(e.GetProperty("asDirected").GetBoolean(), r.AsDirected);
    }

    [Fact]
    public void Display_names_match_web()
    {
        foreach (var c in Load("rxsig.json").GetProperty("nameCases").EnumerateArray())
            Assert.Equal(Str(c, "result"), RxSig.DisplayName(Str(c, "generic"), Str(c, "dosage")));
    }
}
//JJQR - END
