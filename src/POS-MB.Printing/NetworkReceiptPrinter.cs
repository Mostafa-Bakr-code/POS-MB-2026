using System.Net.Sockets;

namespace POS_MB.Printing;

// Sends raw ESC/POS bytes to a printer reachable by IP address on the local
// network - port 9100 is the standard "raw data" port almost every network
// thermal printer listens on, the same one used by Windows' own "Standard TCP/IP
// Port" printer type. Deliberately not going through any OS print-driver system -
// this is a plain socket write, which is why the same code works unchanged
// regardless of what OS or UI framework calls it (Windows, a future Android
// tablet, anything with basic networking).
public class NetworkReceiptPrinter(string ipAddress, int port = 9100) : IReceiptPrinter
{
    // Found live: with no explicit timeout, a genuinely unreachable printer
    // (e.g. its network cable unplugged) fell back to Windows' own TCP
    // connect timeout - around 21 seconds - before ConnectAsync ever threw.
    // With two printers failing plus a same-printer fallback retry (see
    // OrderTakingControl.PrintOrderCoreAsync), the "Print failed" status
    // could take 40+ seconds to appear - long enough that a cashier had
    // already moved on to another order or screen, making the failure
    // message easy to miss or get silently overwritten by the next order's
    // "Order placed" status before anyone saw it.
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public async Task PrintAsync(byte[] data)
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(ConnectTimeout);
        await client.ConnectAsync(ipAddress, port, cts.Token);
        using var stream = client.GetStream();
        await stream.WriteAsync(data);
        await stream.FlushAsync();
    }

    // A plain connectivity check (no bytes sent) - used to decide whether an
    // order can even be placed at all when both printers are down, before
    // any receipt content exists yet. Same timeout as an actual print
    // attempt, so this never itself becomes the slow part of that decision.
    public async Task<bool> IsReachableAsync()
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(ConnectTimeout);
            await client.ConnectAsync(ipAddress, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
