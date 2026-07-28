namespace POS_MB.DataAccess.Models.Reports;

public class SalesSummary
{
    public int TotalOrders { get; set; }
    public int CashierOrders { get; set; }
    public int MobileOrders { get; set; }
    public int ComplimentaryOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal ComplimentaryValue { get; set; }
    public decimal TotalTax { get; set; }
    public decimal RevenueExcludingTax => TotalRevenue - TotalTax;
    public decimal AverageOrderValue =>
        TotalOrders == 0 ? 0 : (TotalRevenue + ComplimentaryValue) / TotalOrders;
}
