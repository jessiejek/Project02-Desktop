//JJQR - AI (Nov 22, 2026) - OTA phase 1: find out whether a newer version exists. Nothing is downloaded or installed here.
// The feed is a small file (latest.json) plus its signature (latest.json.sig). The signature is ECDSA P-256 / SHA-256 over the EXACT bytes of the file, made with a private key only the owner holds;
// the app carries the matching public key, so a tampered or fake feed is refused. See docs/DISTRIBUTION-AND-OTA-PLAN.md.
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClinicNode.Core.Update;

public sealed record UpdatePackage(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("size")] long Size = 0);

public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("min_version")] string? MinVersion,
    [property: JsonPropertyName("released_at")] DateTimeOffset? ReleasedAt,
    [property: JsonPropertyName("notes")] List<string>? Notes,
    [property: JsonPropertyName("mac_arm64")] UpdatePackage? MacArm64,
    [property: JsonPropertyName("mac_x64")] UpdatePackage? MacX64,
    [property: JsonPropertyName("win_x64")] UpdatePackage? WinX64,
    [property: JsonPropertyName("mac_arm64_dmg")] UpdatePackage? MacArm64Dmg = null,
    [property: JsonPropertyName("mac_x64_dmg")] UpdatePackage? MacX64Dmg = null,
    [property: JsonPropertyName("rollout_percent")] int RolloutPercent = 100);

public enum UpdateStatus { NotChecked, UpToDate, Available, Required, Unreachable, BadSignature, Error }

public sealed record UpdateInfo(UpdateStatus Status, Version Current, Version? Latest = null, Version? MinVersion = null,
    IReadOnlyList<string>? Notes = null, UpdatePackage? Package = null, DateTimeOffset? ReleasedAt = null, string? Message = null, UpdatePackage? Installer = null)
{
    public bool HasUpdate => Status is UpdateStatus.Available or UpdateStatus.Required;
}

public sealed class UpdateChecker
{
    private readonly Func<HttpClient> _http;
    private readonly string _feedUrl;
    private readonly byte[] _publicKeySpki;
    private readonly string _platformKey;
    private readonly Func<string> _computerId;

    /// <param name="platformKey">"mac_arm64", "mac_x64" or "win_x64".</param>
    /// <param name="computerId">Stable id of this computer; decides whether it is inside a staged rollout.</param>
    public UpdateChecker(Func<HttpClient> http, string feedUrl, byte[] publicKeySpki, string platformKey, Func<string>? computerId = null)
    {
        _http = http; _feedUrl = feedUrl; _publicKeySpki = publicKeySpki; _platformKey = platformKey;
        _computerId = computerId ?? (() => Environment.MachineName);
    }

    public static bool VerifySignature(byte[] data, byte[] signatureDer, byte[] publicKeySpki)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(publicKeySpki, out _);
            return ecdsa.VerifyData(data, signatureDer, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception) { return false; }
    }

    /// <summary>0..99: where this computer sits in a staged rollout (the same computer always gets the same number).</summary>
    public static int RolloutBucket(string computerId, string version)
    {
        var h = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(computerId + "|" + version));
        return (int)(BitConverter.ToUInt32(h, 0) % 100);
    }

    public async Task<UpdateInfo> CheckAsync(Version current, CancellationToken ct = default)
    {
        byte[] body; string sigText;
        try
        {
            var http = _http();
            body = await http.GetByteArrayAsync(_feedUrl, ct);
            sigText = (await http.GetStringAsync(_feedUrl + ".sig", ct)).Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException)
        {
            return new UpdateInfo(UpdateStatus.Unreachable, current, Message: "Could not reach the update server.");
        }

        byte[] sig;
        try { sig = Convert.FromBase64String(sigText); }
        catch (FormatException) { return new UpdateInfo(UpdateStatus.BadSignature, current, Message: "The update information is not signed correctly. It was ignored."); }
        if (!VerifySignature(body, sig, _publicKeySpki))
            return new UpdateInfo(UpdateStatus.BadSignature, current, Message: "The update information is not signed correctly. It was ignored.");

        UpdateManifest? m;
        try { m = JsonSerializer.Deserialize<UpdateManifest>(body); }
        catch (JsonException) { return new UpdateInfo(UpdateStatus.Error, current, Message: "The update information could not be read."); }
        if (m is null || !Version.TryParse(m.Version, out var latest))
            return new UpdateInfo(UpdateStatus.Error, current, Message: "The update information could not be read.");

        Version.TryParse(m.MinVersion, out var min);
        var pkg = _platformKey switch { "mac_arm64" => m.MacArm64, "mac_x64" => m.MacX64, "win_x64" => m.WinX64, _ => null };
        var installer = _platformKey switch { "mac_arm64" => m.MacArm64Dmg ?? m.MacArm64, "mac_x64" => m.MacX64Dmg ?? m.MacX64, _ => pkg }; // what a person opens by hand (a DMG on the Mac)
        if (latest <= current && (min is null || current >= min))
            return new UpdateInfo(UpdateStatus.UpToDate, current, latest, min, m.Notes, pkg, m.ReleasedAt);

        var required = min is not null && current < min;
        // a staged rollout holds a computer back from an OPTIONAL update; a required one always applies
        if (!required && m.RolloutPercent < 100 && RolloutBucket(_computerId(), m.Version) >= m.RolloutPercent)
            return new UpdateInfo(UpdateStatus.UpToDate, current, latest, min, m.Notes, pkg, m.ReleasedAt);
        if (pkg is null)
            return new UpdateInfo(UpdateStatus.UpToDate, current, latest, min, m.Notes, null, m.ReleasedAt, "A new version exists but not for this kind of computer yet.");
        return new UpdateInfo(required ? UpdateStatus.Required : UpdateStatus.Available, current, latest, min, m.Notes, pkg, m.ReleasedAt, Installer: installer);
    }
}
//JJQR - END
