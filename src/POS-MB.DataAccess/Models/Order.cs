namespace POS_MB.DataAccess.Models;

public class Order
{
    public int OrderId { get; set; }
    public DateTime Date { get; set; }
    public decimal Total { get; set; }
    public int? SerialNumber { get; set; }
    public int? UserId { get; set; }
    public int? StudentId { get; set; }

    // Resolved via a LEFT JOIN in clsOrderDataAccess, not stored - same pattern
    // as ItemPriceHistory.ChangedByUserName, so callers (WinForms' order
    // history) never need to separately fetch and join a Users/Students list
    // client-side just to show who placed an order.
    public string? CashierName { get; set; }
    public string? StudentEmail { get; set; }
    public OrderSource OrderSource { get; set; }
    public OrderStatus Status { get; set; }
    public bool IsComplimentary { get; set; }

    // Set once the cashier PC's poller has printed this order's kitchen
    // ticket - see clsOrderDataAccess.GetOrdersNeedingKitchenTicketAsync.
    // Null means "still needs printing" (only meaningful for a Mobile order
    // sitting in Preparing).
    public DateTime? KitchenTicketPrintedAt { get; set; }

    // Set once Paymob confirms payment (the webhook's obj.id) - needed to
    // tell Paymob which transaction to refund if this order is later
    // cancelled. Null for orders never paid through Paymob (Cashier orders,
    // or a Mobile order still AwaitingPayment/never completed checkout).
    public long? PaymobTransactionId { get; set; }

    // Set once an automatic refund actually succeeds - see
    // clsOrderBusiness.RefundIfPaidAsync. Guards against ever attempting to
    // refund the same order twice.
    public DateTime? RefundedAt { get; set; }

    // Paymob's own transaction id for the refund itself - a separate record
    // from PaymobTransactionId (the original charge), linked to it via
    // Paymob's own parent_transaction field. Surfaced in the UI alongside
    // the original transaction id so staff aren't confused when Paymob's own
    // confirmation email references this number instead of the original one.
    public long? RefundTransactionId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
