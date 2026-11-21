//JJQR - AI (Nov 21, 2026) - .cnbackup format: roundtrip, wrong password, tampering, truncation, reordering.
using ClinicNode.Core.NodeSettings;

namespace ClinicNode.Tests.NodeSettings;

public class BackupFormatTests
{
    const int Fast = 10_000; // PBKDF2 iterations for tests
    static BackupHeader Info(long bytes = 0) => new() { CreatedAt = DateTimeOffset.UtcNow, AppVersion = "1.2.3", Machine = "mac", DatabaseBytes = bytes };

    static async Task<byte[]> Pack(byte[] data, string pw = "correct horse", int chunk = 64)
    {
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        await BackupFormat.WriteAsync(input, output, pw, Info(), Fast, chunk);
        return output.ToArray();
    }

    static async Task<byte[]> Unpack(byte[] file, string pw = "correct horse")
    {
        using var input = new MemoryStream(file);
        using var plain = new MemoryStream();
        await BackupFormat.ReadAsync(input, plain, pw);
        return plain.ToArray();
    }

    static byte[] Data(int n) { var d = new byte[n]; new Random(7).NextBytes(d); return d; }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(1000)]
    public async Task Roundtrip_for_sizes_around_the_chunk_boundary(int size)
    {
        var data = Data(size);
        Assert.Equal(data, await Unpack(await Pack(data)));
    }

    [Fact]
    public async Task Header_is_readable_without_the_password_and_carries_the_metadata()
    {
        var file = await Pack(Data(200));
        var h = await BackupFormat.ReadHeaderAsync(new MemoryStream(file));
        Assert.Equal("clinicnode-backup", h.Format);
        Assert.Equal(1, h.Version);
        Assert.Equal("1.2.3", h.AppVersion);
        Assert.Equal("mac", h.Machine);
        Assert.Equal(200, h.DatabaseBytes);
        Assert.Equal(Fast, h.Iterations);
    }

    [Fact]
    public async Task Ciphertext_does_not_contain_the_plaintext_and_two_backups_differ()
    {
        var data = System.Text.Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("PATIENT-JUAN-DELA-CRUZ ", 40)));
        var a = await Pack(data);
        var b = await Pack(data);
        Assert.False(a.AsSpan().IndexOf("PATIENT-JUAN"u8) >= 0);
        Assert.NotEqual(a, b); // fresh salt and nonce prefix every time
    }

    [Fact]
    public async Task Wrong_password_is_rejected_cleanly()
    {
        var file = await Pack(Data(500));
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => Unpack(file, "not the password"));
        Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Flipping_any_ciphertext_byte_is_detected()
    {
        var file = await Pack(Data(300));
        var headerEnd = 8 + 4 + BitConverter.ToInt32(file, 8);
        foreach (var offset in new[] { headerEnd + 4, headerEnd + 100, file.Length - 1, file.Length - 20 })
        {
            var bad = (byte[])file.Clone();
            bad[offset] ^= 0x01;
            await Assert.ThrowsAsync<BackupFormatException>(() => Unpack(bad));
        }
    }

    [Fact]
    public async Task Changing_the_header_is_detected_even_though_it_is_not_encrypted()
    {
        var file = await Pack(Data(300));
        var text = System.Text.Encoding.UTF8.GetString(file, 12, BitConverter.ToInt32(file, 8));
        var i = text.IndexOf("\"machine\":\"mac\"", StringComparison.Ordinal);
        Assert.True(i > 0);
        var bad = (byte[])file.Clone();
        bad[12 + i + "\"machine\":\"".Length] = (byte)'x'; // mac -> xac, same length
        await Assert.ThrowsAsync<BackupFormatException>(() => Unpack(bad));
    }

    [Fact]
    public async Task Truncation_at_every_interesting_point_fails()
    {
        var file = await Pack(Data(300)); // 5 chunks of 64
        var headerEnd = 8 + 4 + BitConverter.ToInt32(file, 8);
        foreach (var len in new[] { 0, 5, 11, headerEnd - 1, headerEnd, headerEnd + 3, headerEnd + 70, file.Length - 1, file.Length - 17 })
            await Assert.ThrowsAsync<BackupFormatException>(() => Unpack(file[..len]));
    }

    [Fact]
    public async Task Dropping_the_last_chunk_cannot_pass_as_a_shorter_backup()
    {
        var file = await Pack(Data(256)); // exactly 4 full chunks; each chunk is 4 + 64 + 16 = 84 bytes
        var headerEnd = 8 + 4 + BitConverter.ToInt32(file, 8);
        var shorter = file[..(file.Length - 84)];
        Assert.Equal(headerEnd + 3 * 84, shorter.Length);
        await Assert.ThrowsAsync<BackupFormatException>(() => Unpack(shorter));
    }

    [Fact]
    public async Task Swapping_two_chunks_is_detected()
    {
        var file = await Pack(Data(256));
        var headerEnd = 8 + 4 + BitConverter.ToInt32(file, 8);
        var bad = (byte[])file.Clone();
        Array.Copy(file, headerEnd, bad, headerEnd + 84, 84);
        Array.Copy(file, headerEnd + 84, bad, headerEnd, 84);
        await Assert.ThrowsAsync<BackupFormatException>(() => Unpack(bad));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task Short_passwords_are_refused_when_writing(string pw)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Pack(Data(10), pw));
    }

    [Fact]
    public async Task Other_files_are_not_mistaken_for_backups()
    {
        await Assert.ThrowsAsync<BackupFormatException>(() => Unpack("SQLite format 3\0 and some more bytes here"u8.ToArray()));
        await Assert.ThrowsAsync<BackupFormatException>(() => Unpack([]));
    }

    [Fact]
    public async Task A_newer_format_version_asks_for_an_update()
    {
        var file = await Pack(Data(10));
        var json = System.Text.Encoding.UTF8.GetString(file, 12, BitConverter.ToInt32(file, 8));
        var edited = System.Text.Encoding.UTF8.GetBytes(json.Replace("\"version\":1", "\"version\":9"));
        Assert.Equal(json.Length, edited.Length);
        Array.Copy(edited, 0, file, 12, edited.Length);
        var ex = await Assert.ThrowsAsync<BackupFormatException>(() => Unpack(file));
        Assert.Contains("newer", ex.Message);
    }

    [Fact]
    public async Task Large_payload_spanning_many_default_chunks_roundtrips()
    {
        var data = Data(BackupFormat.DefaultChunkSize * 2 + 12345);
        using var input = new MemoryStream(data);
        using var output = new MemoryStream();
        await BackupFormat.WriteAsync(input, output, "correct horse", Info(), Fast);
        output.Position = 0;
        using var plain = new MemoryStream();
        await BackupFormat.ReadAsync(output, plain, "correct horse");
        Assert.Equal(data, plain.ToArray());
    }
}
//JJQR - END
