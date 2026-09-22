using POS_MB.Cashier.Api;
using POS_MB.Cashier.Models;
using POS_MB.Cashier.Session;
using POS_MB.Printing;

namespace POS_MB.Cashier;

// Ported verbatim from POS_MB.WinformsApp.KitchenTicketPrintService - runs
// regardless of which page is open (owned by MainShellPage, same shape as
// the heartbeat), so a mobile order accepted via this device OR the chef
// tablet still gets its kitchen ticket printed from here. A browser can't
// open a raw socket to the printer at all, so the chef tablet can move an
// order to Preparing but never print its own ticket - this is what
// actually fires the print. See
// clsOrderDataAccess.GetOrdersNeedingKitchenTicketAsync for the server side.
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
            // unreachable this tick) should not crash the timer; it just
            // tries again next tick.
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
            order.IsComplimentary,
            order.OrderSource == OrderSource.Cashier ? "CASHIER" : "MOBILE");

        var settings = PrinterSettings.Load();

        var printSerial = settings.ReceiptOrderNumberWrapAt > 0
            ? ((receiptOrder.SerialNumber - 1) % settings.ReceiptOrderNumberWrapAt) + 1
            : receiptOrder.SerialNumber;
        var printOrder = receiptOrder with { SerialNumber = printSerial };

        var kitchenTicket = ReceiptBuilder.BuildKitchenTicket(printOrder, settings.KitchenTicketFontSize);

        if (string.IsNullOrWhiteSpace(settings.ClientPrinterIp) && string.IsNullOrWhiteSpace(settings.KitchenPrinterIp))
        {
            // No printer configured on this device at all - nothing to
            // retry, and there's no chef standing at a screen to show a
            // preview to (this can fire while any page, or none, is open).
            // Mark it printed so it doesn't sit in the queue forever; the
            // order itself remains the record of what to prepare.
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

        // Both printers unreachable - deliberately NOT marked printed, so
        // the next poll tick retries automatically once a printer comes back.
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
