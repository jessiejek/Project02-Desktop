//JJQR - AI (Nov 21, 2026) - send raw ESC/POS bytes to a network thermal printer (TCP 9100 "JetDirect"). Works from Mac Catalyst
// (com.apple.security.network.client is already in Entitlements.plist) and Windows.
using System.Net.Sockets;

namespace ClinicNode.Core.Printing;

public static class EscPosNetwork
{
    public static async Task SendAsync(string host, int port, byte[] payload, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, timeout.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(payload, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }
}
//JJQR - END
