// JJQR - AI (Nov 21, 2026) - node-issued session secret; never the cloud JWT secret.
using System.Security.Cryptography;

namespace ClinicNode.Data;

public static class NodeSecret
{
    public static byte[] GetOrCreate(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(path))
        {
            var existing = File.ReadAllBytes(path);
            if (existing.Length >= 32) return existing;
        }

        var bytes = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, bytes);
        try { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        catch (PlatformNotSupportedException) { /* Windows */ }
        return bytes;
    }
}
// JJQR - END
