namespace POS_MB.WinformsApp.Models;

public enum OrderSource : byte
{
    Cashier = 0,
    Mobile = 1
}

public enum OrderStatus : byte
{
    Placed = 0,
    Preparing = 1,
    Ready = 2,
    Completed = 3,
    Cancelled = 4,
    // Matches POS_MB.DataAccess.Models.OrderStatus - a Mobile order sits here
    // from checkout until Paymob's webhook confirms payment. Not yet used by
    // any order-creation code path (see project notes on the Paymob rollout) -
    // Order Status's "Show Completed/Cancelled Too" filter will need to
    // explicitly exclude this once real orders can reach it, so staff never
    // sees an unpaid order in the working queue.
    AwaitingPayment = 5
}
