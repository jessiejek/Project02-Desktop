//JJQR - AI (Nov 22, 2026) - the PUBLIC half of the owner's update-signing key (ECDSA P-256, SubjectPublicKeyInfo, base64). The private half is only in a GitHub secret and the owner's _secrets folder.
// A test (UpdateKeysTests) fails if this is empty or not a valid key: version 0.2.0 / 0.2.1 shipped with an EMPTY key by mistake and refused every feed.
namespace ClinicNode.Core.Update;

public static class UpdateKeys
{
    public const string PublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEQaz1jcISQrz+RPpqWkeE/uVVe+EISQ7Rq3/0PZMhVvHsJSROqleum2pfEIgUOPQqSDR+n3h48bgIKqb/OHtFLA==";
}
//JJQR - END
