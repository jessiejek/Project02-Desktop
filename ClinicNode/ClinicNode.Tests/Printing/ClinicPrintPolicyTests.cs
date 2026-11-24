//JJQR - AI (Nov 24, 2026) - the clinic-wide print settings: backgrounds behind each document, this computer's copy, and the refresh from the server (fake server).
using System.Text.Json;
using ClinicNode.Api.Http;
using ClinicNode.Api.Services;
using ClinicNode.Core;
using ClinicNode.Core.Printing;
using Xunit;

namespace ClinicNode.Tests.Printing;

public class ClinicPrintPolicyTests : IDisposable
{
    // a real 1x1 PNG and a JPG header (only the first bytes matter: the type is read from them)
    static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
    static readonly byte[] Jpg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46];
    readonly string _dir = Path.Combine(Path.GetTempPath(), "cn-printpolicy-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    sealed class FakeApi : IApiClient
    {
        public string ConfigJson = """{"mode":"computer","backgrounds":[]}""";
        public Dictionary<string, byte[]> Files = new();
        public bool Offline;
        public int Downloads;
        public List<string> Sent = new();

        public Task<T?> GetAsync<T>(string path, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default)
        {
            if (Offline) throw new HttpRequestException("offline");
            return Task.FromResult(JsonSerializer.Deserialize<T>(ConfigJson));
        }
        public Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default) => Task.FromResult(default(T));
        public Task SendAsync(HttpMethod method, string path, object? body = null, IReadOnlyDictionary<string, string?>? query = null, CancellationToken ct = default)
        {
            if (Offline) throw new HttpRequestException("offline");
            Sent.Add(method + " " + path);
            return Task.CompletedTask;
        }
        public Task<ApiDownload> DownloadAsync(string path, CancellationToken ct = default)
        {
            Downloads++;
            var kind = path[(path.LastIndexOf('/') + 1)..];
            return Task.FromResult(new ApiDownload(Files[kind], "image/png", null));
        }
    }

    static PrintDocument Doc(string html, ClinicPrintKind kind) =>
        new PrintDocument("Test", PrintPageSize.A5, html).WithInfo(new PrintJobInfo(kind));

    [Fact]
    public void A_form_gets_the_picture_on_its_sheet_and_keeps_its_print_info()
    {
        var html = "<html><head><title>x</title></head><body><div class=\"form\">hello</div></body></html>";
        var doc = Doc(html, ClinicPrintKind.Prescription);
        var result = PrintBackground.Apply(doc, "data:image/png;base64,AAAA");
        Assert.Contains(".form{background:#fff url(data:image/png;base64,AAAA)", result.Html);
        Assert.DoesNotContain("position:fixed", result.Html);              // forms are drawn on their sheet, no extra layer
        Assert.Equal(ClinicPrintKind.Prescription, result.Info().Kind);     // the printer still knows what this is
        Assert.Equal(html, doc.Html);                                       // the original document is untouched
        Assert.Contains("hello", result.Html);
    }

    [Fact]
    public void A_slip_without_a_form_sheet_gets_a_full_page_layer_behind_the_content()
    {
        var html = "<html><head></head><body class=\"x\"><p>Queue 12</p></body></html>";
        var result = PrintBackground.Apply(Doc(html, ClinicPrintKind.QueueSlip), "data:image/jpeg;base64,BBBB");
        Assert.Contains("position:fixed", result.Html);
        Assert.Contains("z-index:-1", result.Html);
        Assert.True(result.Html.IndexOf("position:fixed", StringComparison.Ordinal) < result.Html.IndexOf("Queue 12", StringComparison.Ordinal), "the layer sits before the content");
        Assert.Equal(ClinicPrintKind.QueueSlip, result.Info().Kind);
    }

    [Fact]
    public void Modes_and_kinds_map_both_ways_and_unknown_means_each_computer_decides()
    {
        Assert.Equal(ClinicPrintMode.Pdf, PrintPolicyKinds.ParseMode("PDF"));
        Assert.Equal(ClinicPrintMode.Native, PrintPolicyKinds.ParseMode(" native "));
        Assert.Equal(ClinicPrintMode.Off, PrintPolicyKinds.ParseMode("off"));
        Assert.Equal(ClinicPrintMode.Computer, PrintPolicyKinds.ParseMode("banana"));
        Assert.Equal(ClinicPrintMode.Computer, PrintPolicyKinds.ParseMode(null));
        foreach (var (kind, key, _) in PrintPolicyKinds.All) Assert.Equal(kind, PrintPolicyKinds.KindOf(key));
        Assert.Equal(5, PrintPolicyKinds.All.Count);
    }

    [Fact]
    public void The_computers_copy_survives_a_restart_and_a_cleared_picture_is_removed()
    {
        var c1 = new PrintPolicyCache(_dir);
        c1.Save(ClinicPrintMode.Off, new Dictionary<string, int> { ["prescription"] = 3, ["receipt"] = 1 },
            new Dictionary<string, byte[]> { ["prescription"] = Png, ["receipt"] = Jpg });
        var c2 = new PrintPolicyCache(_dir);                      // "restart"
        Assert.Equal(ClinicPrintMode.Off, c2.Mode);
        Assert.Equal(3, c2.Version("prescription"));
        Assert.Equal(Png, c2.Bytes("prescription"));
        Assert.Equal("image/jpeg", PrintPolicyCache.ContentTypeOf(c2.Bytes("receipt")!));
        c2.Save(ClinicPrintMode.Native, new Dictionary<string, int> { ["receipt"] = 1 }, new Dictionary<string, byte[]>());
        Assert.Null(new PrintPolicyCache(_dir).Bytes("prescription"));      // removed on the server -> gone here too
        Assert.NotNull(new PrintPolicyCache(_dir).Bytes("receipt"));        // unchanged picture kept without downloading again
        Assert.Null(PrintPolicyCache.ContentTypeOf([1, 2, 3, 4, 5, 6, 7, 8, 9]));
    }

    [Fact]
    public async Task Refresh_downloads_only_changed_pictures_and_offline_keeps_the_copy()
    {
        var api = new FakeApi { Files = { ["prescription"] = Png, ["receipt"] = Jpg },
            ConfigJson = """{"mode":"pdf","backgrounds":[{"kind":"prescription","version":1},{"kind":"receipt","version":4}]}""" };
        var policy = new ApiClinicPrintPolicy(api, _dir);
        await policy.RefreshAsync();
        Assert.Equal(ClinicPrintMode.Pdf, policy.Mode);
        Assert.StartsWith("data:image/png;base64,", policy.BackgroundDataUri(ClinicPrintKind.Prescription));
        Assert.StartsWith("data:image/jpeg;base64,", policy.BackgroundDataUri(ClinicPrintKind.Receipt));
        Assert.Null(policy.BackgroundDataUri(ClinicPrintKind.LabRequest));
        Assert.Equal(2, api.Downloads);

        await policy.RefreshAsync();                               // nothing changed on the server -> no new downloads
        Assert.Equal(2, api.Downloads);

        api.ConfigJson = """{"mode":"pdf","backgrounds":[{"kind":"prescription","version":2},{"kind":"receipt","version":4}]}""";
        await policy.RefreshAsync();                               // only the changed one is downloaded
        Assert.Equal(3, api.Downloads);

        api.Offline = true;                                        // no internet: the copy this computer has keeps working
        await policy.RefreshAsync();
        Assert.Equal(ClinicPrintMode.Pdf, policy.Mode);
        Assert.NotNull(policy.BackgroundDataUri(ClinicPrintKind.Prescription));

        api.Offline = false;
        api.ConfigJson = """{"mode":"off","backgrounds":[]}""";   // the Admin removed everything and turned printing off
        await policy.RefreshAsync();
        Assert.Equal(ClinicPrintMode.Off, policy.Mode);
        Assert.Null(policy.BackgroundDataUri(ClinicPrintKind.Prescription));
    }

    [Fact]
    public async Task A_new_computer_with_no_internet_prints_the_old_way()
    {
        var policy = new ApiClinicPrintPolicy(new FakeApi { Offline = true }, _dir);
        await policy.RefreshAsync();
        Assert.Equal(ClinicPrintMode.Computer, policy.Mode);       // default: nothing changes until the Admin chooses
        Assert.Null(policy.BackgroundDataUri(ClinicPrintKind.Prescription));
        Assert.Equal(ClinicPrintMode.Computer, new NoPrintPolicy().Mode);
    }

    [Fact]
    public async Task Admin_uploads_are_checked_here_before_anything_is_sent()
    {
        var api = new FakeApi { ConfigJson = """{"mode":"computer","backgrounds":[]}""" };
        var policy = new ApiClinicPrintPolicy(api, _dir);
        Assert.NotNull(await policy.UploadBackgroundAsync(ClinicPrintKind.Receipt, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));   // not a picture
        Assert.NotNull(await policy.UploadBackgroundAsync(ClinicPrintKind.Receipt, new byte[7 * 1024 * 1024].Select((_, i) => i < 4 ? new byte[] { 0x89, 0x50, 0x4E, 0x47 }[i] : (byte)0).ToArray())); // over 6 MB
        Assert.Empty(api.Sent);
        Assert.Null(await policy.UploadBackgroundAsync(ClinicPrintKind.Receipt, Png));
        Assert.Contains("PUT /api/print-config/backgrounds/receipt", api.Sent);
        Assert.Null(await policy.SetModeAsync(ClinicPrintMode.Native));
        Assert.Contains("PUT /api/print-config/mode", api.Sent);
        Assert.Null(await policy.ClearBackgroundAsync(ClinicPrintKind.Receipt));
        Assert.Contains("DELETE /api/print-config/backgrounds/receipt", api.Sent);
    }
}
//JJQR - END
