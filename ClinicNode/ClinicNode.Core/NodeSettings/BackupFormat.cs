//JJQR - AI (Nov 21, 2026) - .cnbackup file format: password-encrypted (PBKDF2-SHA256 → AES-256-GCM, 1 MiB chunks) so a backup can be restored
// on a NEW computer whose keychain does not hold the old DB key.
//   "CNBACKUP" (8) | int32 LE header length | header JSON (UTF-8, authenticated, not secret) | chunks…
//   chunk = uint32 LE (plain length | 0x80000000 on the last chunk) | ciphertext | 16-byte GCM tag
//   nonce = 8-byte random prefix (header) + uint32 BE chunk index; AAD = SHA-256(magic|header) | uint64 index | final flag
// Reordering, truncation, a changed header or a wrong password all fail the tag check.
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClinicNode.Core.NodeSettings;

public sealed record BackupHeader
{
    public string Format { get; init; } = BackupFormat.FormatName;
    public int Version { get; init; } = 1;
    public DateTimeOffset CreatedAt { get; init; }
    public string? AppVersion { get; init; }
    public string? Machine { get; init; }
    /// <summary>Size of the plaintext SQLite snapshot.</summary>
    public long DatabaseBytes { get; init; }
    /// <summary>Whether the source node had its DB encrypted at rest (informational).</summary>
    public bool SourceEncryptedAtRest { get; init; }
    public string Kdf { get; init; } = "PBKDF2-SHA256";
    public int Iterations { get; init; }
    public string Salt { get; init; } = "";
    public string NoncePrefix { get; init; } = "";
    public int ChunkSize { get; init; }
}

public sealed class BackupFormatException(string message, Exception? inner = null) : Exception(message, inner);

public static class BackupFormat
{
    public const string FormatName = "clinicnode-backup";
    public const string FileExtension = ".cnbackup";
    public const int DefaultIterations = 600_000;
    public const int DefaultChunkSize = 1 << 20;
    public const int MinPasswordLength = 8;
    const uint FinalFlag = 0x8000_0000;
    static readonly byte[] Magic = "CNBACKUP"u8.ToArray();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw new ArgumentException($"Backup password must be at least {MinPasswordLength} characters.", nameof(password));
    }

    /// <summary>Encrypts <paramref name="plaintext"/> (the SQLite snapshot) into <paramref name="output"/>. Returns the header written.</summary>
    public static async Task<BackupHeader> WriteAsync(Stream plaintext, Stream output, string password, BackupHeader info,
        int iterations = DefaultIterations, int chunkSize = DefaultChunkSize, CancellationToken ct = default)
    {
        ValidatePassword(password);
        var salt = RandomNumberGenerator.GetBytes(16);
        var prefix = RandomNumberGenerator.GetBytes(8);
        // the header states the plaintext size so truncation is caught even if a whole chunk were removed; derive it when the source is seekable
        var size = plaintext.CanSeek ? plaintext.Length - plaintext.Position : info.DatabaseBytes;
        var header = info with
        {
            DatabaseBytes = size,
            Format = FormatName, Version = 1, Kdf = "PBKDF2-SHA256", Iterations = iterations, ChunkSize = chunkSize,
            Salt = Convert.ToBase64String(salt), NoncePrefix = Convert.ToBase64String(prefix),
        };
        var headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, Json);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        var aadBase = AadBase(headerBytes);

        await output.WriteAsync(Magic, ct);
        var len = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, headerBytes.Length);
        await output.WriteAsync(len, ct);
        await output.WriteAsync(headerBytes, ct);

        using var gcm = new AesGcm(key, 16);
        var buf = new byte[chunkSize];
        var next = new byte[chunkSize];
        var have = await ReadFullAsync(plaintext, buf, ct);
        long total = 0;
        for (ulong index = 0; ; index++)
        {
            var nextHave = have == chunkSize ? await ReadFullAsync(plaintext, next, ct) : 0;
            var final = nextHave == 0;
            var cipher = new byte[have];
            var tag = new byte[16];
            gcm.Encrypt(Nonce(prefix, index), buf.AsSpan(0, have), cipher, tag, Aad(aadBase, index, final));
            BinaryPrimitives.WriteUInt32LittleEndian(len, (uint)have | (final ? FinalFlag : 0));
            await output.WriteAsync(len, ct);
            await output.WriteAsync(cipher, ct);
            await output.WriteAsync(tag, ct);
            total += have;
            if (final) break;
            (buf, next) = (next, buf);
            have = nextHave;
        }
        CryptographicOperations.ZeroMemory(key);
        if (info.DatabaseBytes != 0 && info.DatabaseBytes != total)
            throw new BackupFormatException($"Snapshot size changed while writing ({total} of {info.DatabaseBytes} bytes).");
        return header with { DatabaseBytes = total };
    }

    /// <summary>Reads only the (unencrypted, authenticated-later) header, e.g. to show the date in the restore confirmation.</summary>
    public static async Task<BackupHeader> ReadHeaderAsync(Stream input, CancellationToken ct = default) =>
        (await ReadHeaderCoreAsync(input, ct)).Header;

    /// <summary>Decrypts into <paramref name="plaintext"/>. Throws <see cref="BackupFormatException"/> for a wrong password or a damaged/truncated file.</summary>
    public static async Task<BackupHeader> ReadAsync(Stream input, Stream plaintext, string password, CancellationToken ct = default)
    {
        var (header, headerBytes) = await ReadHeaderCoreAsync(input, ct);
        if (header.Iterations < 10_000 || header.ChunkSize is <= 0 or > 64 << 20)
            throw new BackupFormatException("Backup header has invalid parameters.");
        byte[] salt, prefix;
        try { salt = Convert.FromBase64String(header.Salt); prefix = Convert.FromBase64String(header.NoncePrefix); }
        catch (FormatException ex) { throw new BackupFormatException("Backup header is damaged.", ex); }
        if (prefix.Length != 8) throw new BackupFormatException("Backup header is damaged.");

        var key = Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, header.Iterations, HashAlgorithmName.SHA256, 32);
        var aadBase = AadBase(headerBytes);
        using var gcm = new AesGcm(key, 16);
        var len = new byte[4];
        var tag = new byte[16];
        long total = 0;
        try
        {
            for (ulong index = 0; ; index++)
            {
                if (await ReadFullAsync(input, len, ct) != 4) throw new BackupFormatException("Backup file is truncated.");
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(len);
                var final = (raw & FinalFlag) != 0;
                var n = (int)(raw & ~FinalFlag);
                if (n > header.ChunkSize) throw new BackupFormatException("Backup file is damaged.");
                var cipher = new byte[n];
                if (await ReadFullAsync(input, cipher, ct) != n || await ReadFullAsync(input, tag, ct) != 16)
                    throw new BackupFormatException("Backup file is truncated.");
                var plain = new byte[n];
                try { gcm.Decrypt(Nonce(prefix, index), cipher, tag, plain, Aad(aadBase, index, final)); }
                catch (AuthenticationTagMismatchException ex)
                {
                    throw new BackupFormatException(index == 0 ? "Wrong backup password, or the file is damaged." : "Backup file is damaged.", ex);
                }
                await plaintext.WriteAsync(plain, ct);
                total += n;
                if (final) break;
            }
        }
        finally { CryptographicOperations.ZeroMemory(key); }
        if (total != header.DatabaseBytes) throw new BackupFormatException("Backup size does not match its header.");
        return header;
    }

    static async Task<(BackupHeader Header, byte[] Bytes)> ReadHeaderCoreAsync(Stream input, CancellationToken ct)
    {
        var magic = new byte[8];
        if (await ReadFullAsync(input, magic, ct) != 8 || !magic.AsSpan().SequenceEqual(Magic))
            throw new BackupFormatException("This is not a ClinicNode backup file.");
        var len = new byte[4];
        if (await ReadFullAsync(input, len, ct) != 4) throw new BackupFormatException("Backup file is truncated.");
        var n = BinaryPrimitives.ReadInt32LittleEndian(len);
        if (n is <= 0 or > 64 * 1024) throw new BackupFormatException("Backup header is damaged.");
        var bytes = new byte[n];
        if (await ReadFullAsync(input, bytes, ct) != n) throw new BackupFormatException("Backup file is truncated.");
        BackupHeader? header;
        try { header = JsonSerializer.Deserialize<BackupHeader>(bytes, Json); }
        catch (JsonException ex) { throw new BackupFormatException("Backup header is damaged.", ex); }
        if (header is null || header.Format != FormatName) throw new BackupFormatException("This is not a ClinicNode backup file.");
        if (header.Version != 1) throw new BackupFormatException($"Backup version {header.Version} needs a newer ClinicNode.");
        return (header, bytes);
    }

    static byte[] AadBase(byte[] headerBytes)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha.AppendData(Magic);
        sha.AppendData(headerBytes);
        return sha.GetHashAndReset();
    }

    static byte[] Aad(byte[] aadBase, ulong index, bool final)
    {
        var aad = new byte[aadBase.Length + 9];
        aadBase.CopyTo(aad, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(aad.AsSpan(aadBase.Length), index);
        aad[^1] = final ? (byte)1 : (byte)0;
        return aad;
    }

    static byte[] Nonce(byte[] prefix, ulong index)
    {
        var nonce = new byte[12];
        prefix.CopyTo(nonce, 0);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), checked((uint)index));
        return nonce;
    }

    static async Task<int> ReadFullAsync(Stream s, byte[] buf, CancellationToken ct)
    {
        var read = 0;
        while (read < buf.Length)
        {
            var n = await s.ReadAsync(buf.AsMemory(read), ct);
            if (n == 0) break;
            read += n;
        }
        return read;
    }
}
//JJQR - END
