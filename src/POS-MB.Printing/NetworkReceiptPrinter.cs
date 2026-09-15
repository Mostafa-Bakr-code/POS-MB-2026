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

    // Found live: a printer that just finished cutting the previous job's
    // paper can briefly refuse new connections on its network module even
    // though it's completely fine - without a retry, back-to-back orders
    // could catch it in that instant and wrongly treat it as unreachable,
    // triggering the cross-printer fallback (or even blocking a new order,
    // if both happened to be mid-job at once) seconds after it had printed
    // perfectly fine. One retry after a short pause is enough for it to
    // finish and start accepting connections again.
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    public async Task PrintAsync(byte[] data)
    {
        await ConnectWithRetryAsync(async client =>
        {
            using var stream = client.GetStream();
            await stream.WriteAsync(data);
            await stream.FlushAsync();
        });
    }

    private async Task ConnectWithRetryAsync(Func<TcpClient, Task> action)
    {
        try
        {
            await ConnectAndRunAsync(action);
        }
        catch
        {
            await Task.Delay(RetryDelay);
            await ConnectAndRunAsync(action);
        }
    }

    private async Task ConnectAndRunAsync(Func<TcpClient, Task> action)
    {
        using var client = new TcpClient();
        using var cts = new CancellationTokenSource(ConnectTimeout);
        await client.ConnectAsync(ipAddress, port, cts.Token);
        await action(client);
    }
}
