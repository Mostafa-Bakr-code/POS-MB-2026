namespace POS_MB.DataAccess.Models.Reports;

public class SalesSummary
{
    public int TotalOrders { get; set; }
    public int CashierOrders { get; set; }
    public int MobileOrders { get; set; }
    public int ComplimentaryOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    // Split out from TotalRevenue so the cashier can tell exactly how much cash
    // to collect from the register - mobile orders are paid separately (not
    // handed to the cashier), so they must never be lumped into that figure.
    public decimal CashierRevenue { get; set; }
    public decimal MobileRevenue { get; set; }
    public decimal ComplimentaryValue { get; set; }
    public decimal TotalTax { get; set; }
    public decimal RevenueExcludingTax => TotalRevenue - TotalTax;
    public decimal AverageOrderValue =>
        TotalOrders == 0 ? 0 : (TotalRevenue + ComplimentaryValue) / TotalOrders;
}
