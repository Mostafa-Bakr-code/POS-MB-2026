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
    public async Task PrintAsync(byte[] data)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(ipAddress, port);
        using var stream = client.GetStream();
        await stream.WriteAsync(data);
        await stream.FlushAsync();
    }
}
