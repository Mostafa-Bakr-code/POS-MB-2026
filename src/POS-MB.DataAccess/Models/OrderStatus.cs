namespace POS_MB.DataAccess.Models;

public enum OrderStatus : byte
{
    Placed = 0,
    Preparing = 1,
    Ready = 2,
    Completed = 3,
    Cancelled = 4,
    // Appended, not inserted - existing numeric values must never change,
    // they're already stored in real rows. A Mobile order sits here from the
    // moment checkout starts until Paymob's webhook confirms payment; it's
    // deliberately invisible to the kitchen/Order Status screen until then -
    // nothing about it (price, items) is final until payment succeeds.
    AwaitingPayment = 5
}
