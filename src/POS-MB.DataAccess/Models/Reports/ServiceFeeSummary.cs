namespace POS_MB.DataAccess.Models.Reports;

// Deliberately separate from SalesSummary, not an extra field bolted onto it -
// ServiceFee is tracked outside Orders.Total specifically so existing revenue
// figures (TotalRevenue/CashierRevenue/MobileRevenue) never silently change
// meaning. This is purely "how much of the fee did I actually collect, so I
// know what to set aside for Azure/Paymob running costs" - not a revenue
// number, and never added into any other report's totals.
public class ServiceFeeSummary
{
    // Only ever Mobile orders can carry a fee - counted here, not just
    // inferred from MobileOrders on SalesSummary, since a Mobile order
    // placed before this feature existed (or while the fee setting was 0)
    // contributes to MobileOrders there but not here.
    public int OrdersWithFeeCount { get; set; }
    public decimal TotalServiceFeesCollected { get; set; }
}
