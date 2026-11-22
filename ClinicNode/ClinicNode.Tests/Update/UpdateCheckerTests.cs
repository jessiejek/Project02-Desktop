//JJQR - AI (Nov 22, 2026) - OTA phase 1 tests: a genuine, signed feed is accepted; a tampered or unsigned one never is.
using System.Security.Cryptography;
using System.Text;
using ClinicNode.Core.Update;

namespace ClinicNode.Tests.Update;

public class UpdateCheckerTests
{
    sealed class FakeFeed : HttpMessageHandler
    {
        public byte[] Body = [];
        public string Sig = "";
        public bool Down;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (Down) throw new HttpRequestException("down");
            var text = r.RequestUri!.AbsolutePath.EndsWith(".sig") ? new ByteArrayContent(Encoding.UTF8.GetBytes(Sig)) : new ByteArrayContent(Body);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = text });
        }
    }

    static (ECDsa key, byte[] spki) NewKey() { var k = ECDsa.Create(ECCurve.NamedCurves.nistP256); return (k, k.ExportSubjectPublicKeyInfo()); }
    static string Sign(ECDsa k, byte[] body) => Convert.ToBase64String(k.SignData(body, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));
    static byte[] Manifest(string version, string? min = null, int rollout = 100) => Encoding.UTF8.GetBytes(
        $$"""{"version":"{{version}}","min_version":{{(min is null ? "null" : $"\"{min}\"")}},"notes":["Faster"],"mac_arm64":{"url":"https://x/y.zip","sha256":"abc","size":1},"rollout_percent":{{rollout}}}""");

    static UpdateChecker Checker(FakeFeed feed, byte[] spki, string platform = "mac_arm64") =>
        new(() => new HttpClient(feed), "https://feed.example/latest.json", spki, platform, () => "computer-1");

    [Fact]
    public async Task A_newer_signed_version_is_offered()
    {
        var (k, spki) = NewKey(); var body = Manifest("0.2.0");
        var feed = new FakeFeed { Body = body, Sig = Sign(k, body) };
        var r = await Checker(feed, spki).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateStatus.Available, r.Status);
        Assert.Equal(new Version(0, 2, 0), r.Latest);
        Assert.Equal("https://x/y.zip", r.Package!.Url);
    }

    [Fact]
    public async Task Same_or_older_version_is_up_to_date()
    {
        var (k, spki) = NewKey(); var body = Manifest("0.1.0");
        var r = await Checker(new FakeFeed { Body = body, Sig = Sign(k, body) }, spki).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateStatus.UpToDate, r.Status);
    }

    [Fact]
    public async Task A_tampered_feed_is_refused()
    {
        var (k, spki) = NewKey(); var body = Manifest("0.2.0"); var sig = Sign(k, body);
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace("y.zip", "evil.zip"));
        var r = await Checker(new FakeFeed { Body = tampered, Sig = sig }, spki).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateStatus.BadSignature, r.Status);
        Assert.False(r.HasUpdate);
    }

    [Fact]
    public async Task A_feed_signed_with_another_key_is_refused()
    {
        var (k, _) = NewKey(); var (_, otherSpki) = NewKey(); var body = Manifest("0.2.0");
        var r = await Checker(new FakeFeed { Body = body, Sig = Sign(k, body) }, otherSpki).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateStatus.BadSignature, r.Status);
    }

    [Fact]
    public async Task A_missing_or_garbage_signature_is_refused()
    {
        var (_, spki) = NewKey(); var body = Manifest("0.2.0");
        Assert.Equal(UpdateStatus.BadSignature, (await Checker(new FakeFeed { Body = body, Sig = "not base64!!" }, spki).CheckAsync(new Version(0, 1, 0))).Status);
        Assert.Equal(UpdateStatus.BadSignature, (await Checker(new FakeFeed { Body = body, Sig = "" }, spki).CheckAsync(new Version(0, 1, 0))).Status);
    }

    [Fact]
    public async Task Below_the_minimum_version_the_update_is_required()
    {
        var (k, spki) = NewKey(); var body = Manifest("0.3.0", min: "0.2.0");
        var r = await Checker(new FakeFeed { Body = body, Sig = Sign(k, body) }, spki).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateStatus.Required, r.Status);
    }

    [Fact]
    public async Task No_internet_is_reported_quietly()
    {
        var (_, spki) = NewKey();
        var r = await Checker(new FakeFeed { Down = true }, spki).CheckAsync(new Version(0, 1, 0));
        Assert.Equal(UpdateStatus.Unreachable, r.Status);
    }

    [Fact]
    public async Task A_version_for_another_kind_of_computer_is_not_offered()
    {
        var (k, spki) = NewKey(); var body = Manifest("0.2.0");
        var r = await Checker(new FakeFeed { Body = body, Sig = Sign(k, body) }, spki, platform: "win_x64").CheckAsync(new Version(0, 1, 0));
        Assert.False(r.HasUpdate);
    }

    [Fact]
    public async Task A_staged_rollout_holds_back_computers_outside_the_percentage_but_never_a_required_update()
    {
        var (k, spki) = NewKey();
        var bodyZero = Manifest("0.2.0", rollout: 0);
        Assert.Equal(UpdateStatus.UpToDate, (await Checker(new FakeFeed { Body = bodyZero, Sig = Sign(k, bodyZero) }, spki).CheckAsync(new Version(0, 1, 0))).Status);
        var bodyReq = Manifest("0.2.0", min: "0.2.0", rollout: 0);
        Assert.Equal(UpdateStatus.Required, (await Checker(new FakeFeed { Body = bodyReq, Sig = Sign(k, bodyReq) }, spki).CheckAsync(new Version(0, 1, 0))).Status);
    }
}
//JJQR - END
