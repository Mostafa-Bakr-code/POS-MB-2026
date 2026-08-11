using POS_MB.Printing;
using POS_MB.WinformsApp.Api;
using POS_MB.WinformsApp.Models;
using POS_MB.WinformsApp.Session;

namespace POS_MB.WinformsApp;

// Runs on the cashier PC regardless of which screen is open (owned by
// FormMain, same shape as the Accepting-Online-Orders heartbeat) - a browser
// can't open a raw socket to the ESC/POS printer, so the chef tablet can move
// an order to Preparing but can't print its ticket itself. This is what
// actually fires the print, decoupled from whichever client (tablet or
// OrderStatusControl here) did the accepting. See
// clsOrderDataAccess.GetOrdersNeedingKitchenTicketAsync for the server side
// of this design.
public class KitchenTicketPrintService(ApiClient apiClient)
{
    private bool _isPolling;

    public event Action<string, bool>? StatusChanged;

    // Guards against overlapping runs if a poll takes longer than the timer
    // interval (e.g. a slow/unreachable printer) - the next tick just skips
    // instead of starting a second concurrent pass over the same orders.
    public async Task PollOnceAsync()
    {
        if (_isPolling) return;
        _isPolling = true;
        try
        {
            var orders = await apiClient.GetOrdersNeedingKitchenTicketAsync();
            if (orders.Count == 0) return;

            var allItems = await apiClient.GetItemsAsync(includeInactive: true);
            var itemNamesById = allItems.ToDictionary(i => i.ItemId, i => i.ItemName);

            foreach (var order in orders)
                await PrintAndMarkAsync(order, itemNamesById);
        }
        catch
        {
            // Best-effort background task - a transient failure (API
            // unreachable this tick) should not surface a dialog or crash
            // the timer; it just tries again next tick.
        }
        finally
        {
            _isPolling = false;
        }
    }

    private async Task PrintAndMarkAsync(OrderDto order, Dictionary<int, string> itemNamesById)
    {
        var receiptOrder = new ReceiptOrder(
            order.SerialNumber ?? order.OrderId,
            AppSession.ToLocalDisplay(order.Date),
            order.Items.Select(oi => new ReceiptItem(
                itemNamesById.GetValueOrDefault(oi.ItemId, "Item"),
                oi.Quantity, oi.Price, oi.TaxRate, oi.Comment)).ToList(),
            order.Total,
            order.IsComplimentary);

        var settings = PrinterSettings.Load();

        var printSerial = settings.ReceiptOrderNumberWrapAt > 0
            ? ((receiptOrder.SerialNumber - 1) % settings.ReceiptOrderNumberWrapAt) + 1
            : receiptOrder.SerialNumber;
        var printOrder = receiptOrder with { SerialNumber = printSerial };

        var kitchenTicket = ReceiptBuilder.BuildKitchenTicket(printOrder, settings.KitchenTicketFontSize);

        if (string.IsNullOrWhiteSpace(settings.ClientPrinterIp) && string.IsNullOrWhiteSpace(settings.KitchenPrinterIp))
        {
            // No printer configured on this machine at all - nothing to
            // retry, and there's no chef standing at OrderStatusControl to
            // show a preview dialog to anymore (this can now fire while any
            // screen, or none, is open). Mark it printed so it doesn't sit
            // in the queue forever; the order itself (visible on the tablet/
            // WinForms) remains the record of what to prepare.
            await apiClient.MarkKitchenTicketPrintedAsync(order.OrderId);
            ShowStatus($"Order #{printOrder.SerialNumber}: no printer configured - ticket not printed.", success: false);
            return;
        }

        var kitchenFailure = await PrintSafelyAsync(settings.KitchenPrinterIp, settings.KitchenPrinterPort, kitchenTicket, "Kitchen ticket");
        if (kitchenFailure is null)
        {
            await apiClient.MarkKitchenTicketPrintedAsync(order.OrderId);
            ShowStatus($"Order #{printOrder.SerialNumber}: kitchen ticket printed.", success: true);
            return;
        }

        var fallbackFailure = await PrintSafelyAsync(settings.ClientPrinterIp, settings.ClientPrinterPort, kitchenTicket, "Kitchen ticket (fallback)");
        if (fallbackFailure is null)
        {
            await apiClient.MarkKitchenTicketPrintedAsync(order.OrderId);
            ShowStatus($"Order #{printOrder.SerialNumber}: kitchen printer unreachable - printed on client printer instead.", success: false);
            return;
        }

        // Both printers unreachable - deliberately NOT marked printed, so the
        // next poll tick retries automatically once a printer comes back.
        ShowStatus($"Order #{printOrder.SerialNumber}: print failed - both printers unreachable, will retry.", success: false);
    }

    private static async Task<string?> PrintSafelyAsync(string ip, int port, byte[] data, string label)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return $"{label} (not configured)";

        try
        {
            await new NetworkReceiptPrinter(ip, port).PrintAsync(data);
            return null;
        }
        catch
        {
            return label;
        }
    }

    private void ShowStatus(string text, bool success) => StatusChanged?.Invoke(text, success);
}
