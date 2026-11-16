namespace POS_MB.WinformsApp.Models;

public enum ReportType
{
    SalesSummary,
    ItemSales,
    TopSellers,
    StaffPerformance,
    ServiceFeesCollected
}

public enum TopSellersSortBy
{
    Quantity,
    Revenue
}

public class SalesSummaryDto
{
    public int TotalOrders { get; set; }
    public int CashierOrders { get; set; }
    public int MobileOrders { get; set; }
    public int ComplimentaryOrders { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal CashierRevenue { get; set; }
    public decimal MobileRevenue { get; set; }
    public decimal ComplimentaryValue { get; set; }
    public decimal TotalTax { get; set; }
    public decimal RevenueExcludingTax { get; set; }
    public decimal AverageOrderValue { get; set; }
}

public class ItemSalesRowDto
{
    public DateTime? OrderDate { get; set; }
    public decimal? SoldAtPrice { get; set; }
    public OrderSource? Source { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Revenue { get; set; }
    public decimal ComplimentaryValue { get; set; }
    public decimal Tax { get; set; }
    public decimal RevenueExcludingTax { get; set; }
    public decimal AveragePrice { get; set; }
}

public class StaffPerformanceRowDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int OrderCount { get; set; }
    public decimal Revenue { get; set; }
    public decimal Tax { get; set; }
    public decimal RevenueExcludingTax { get; set; }
}

// Deliberately separate from SalesSummaryDto - see the server-side
// ServiceFeeSummary's own comment for why this must never get mixed into
// any revenue figure.
public class ServiceFeeSummaryDto
{
    public int OrdersWithFeeCount { get; set; }
    public decimal TotalServiceFeesCollected { get; set; }
}
