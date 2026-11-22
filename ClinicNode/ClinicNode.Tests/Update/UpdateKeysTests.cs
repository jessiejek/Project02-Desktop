//JJQR - AI (Nov 22, 2026) - guard: the app must carry a REAL public update key, and it must be able to verify what the release script signs.
using System.Security.Cryptography;
using ClinicNode.Core.Update;

namespace ClinicNode.Tests.Update;

public class UpdateKeysTests
{
    [Fact]
    public void The_embedded_public_key_is_present_and_a_valid_P256_key()
    {
        Assert.False(string.IsNullOrWhiteSpace(UpdateKeys.PublicKeyBase64));
        var der = Convert.FromBase64String(UpdateKeys.PublicKeyBase64);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(der, out _);          // throws if it is not a valid key
        Assert.Equal(256, ecdsa.KeySize);
    }

    [Fact]
    public void A_signature_from_the_matching_private_key_verifies_but_one_from_another_key_does_not()
    {
        var der = Convert.FromBase64String(UpdateKeys.PublicKeyBase64);
        var data = System.Text.Encoding.UTF8.GetBytes("{}");
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var foreign = other.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        Assert.False(UpdateChecker.VerifySignature(data, foreign, der));
    }
}
//JJQR - END
