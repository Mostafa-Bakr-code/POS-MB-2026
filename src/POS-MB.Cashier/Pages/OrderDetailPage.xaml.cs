using POS_MB.Cashier.Controls;
using POS_MB.Cashier.Models;
using POS_MB.Cashier.Session;

namespace POS_MB.Cashier.Pages;

// Replaces FormOrderDetailDialog - read-only, pushed as a normal
// (non-modal) page. WinForms computed a dynamic dialog height depending on
// which optional info lines applied (payment/cancel) - here that's just a
// multi-line Label that grows/shrinks naturally, no manual sizing needed.
public partial class OrderDetailPage : ContentPage
{
    public OrderDetailPage(OrderDto order, Dictionary<int, string> itemNamesById)
    {
        InitializeComponent();
        Title = $"Order #{order.SerialNumber ?? order.OrderId}";

        var placedBy = order.CashierName is not null ? $"  (Cashier: {order.CashierName})"
            : order.StudentEmail is not null ? $"  (Student: {order.StudentEmail})"
            : "";

        // Only a Mobile order ever paid through Paymob has a transaction id -
        // the same id you'd search for on Paymob's own dashboard if a
        // student disputes a charge/refund.
        var paymentInfo = order.PaymobTransactionId is long transactionId
            ? $"\nPaid via Paymob (Transaction #{transactionId})" +
              (order.RefundedAt is DateTime refundedAt
                  ? $"\nRefunded: {AppSession.ToLocalDisplay(refundedAt):yyyy-MM-dd HH:mm}" +
                    (order.RefundTransactionId is long refundTransactionId ? $" (Refund Transaction #{refundTransactionId})" : "")
                  : "")
            : "";

        var cancelInfo = order.CancelledBy is string cancelledBy ? $"\nCancelled by: {cancelledBy}" : "";

        HeaderLabel.Text =
            $"Date: {AppSession.ToLocalDisplay(order.Date):yyyy-MM-dd HH:mm}\n" +
            $"Source: {order.OrderSource}{placedBy}\n" +
            $"Status: {order.Status}{(order.IsComplimentary ? "  (Complimentary)" : "")}\n" +
            $"Total: {order.Total:0.00}" +
            paymentInfo +
            cancelInfo;

        LinesGrid.SetColumns(
        [
            new GridColumn("Item", l => itemNamesById.GetValueOrDefault(((OrderItemDto)l).ItemId, $"Item #{((OrderItemDto)l).ItemId}"), weight: 1.6),
            new GridColumn("Qty", l => ((OrderItemDto)l).Quantity.ToString(), weight: 0.5),
            new GridColumn("Unit Price", l => ((OrderItemDto)l).Price.ToString("0.00"), weight: 0.8),
            new GridColumn("Line Total", l => ((OrderItemDto)l).TotalItemsPrice.ToString("0.00"), weight: 0.8),
            new GridColumn("Comment", l => ((OrderItemDto)l).Comment ?? "", weight: 1)
        ]);
        LinesGrid.ItemsSource = order.Items.Cast<object>();
    }

    private async void OnCloseClicked(object? sender, EventArgs e) => await Navigation.PopAsync();
}
