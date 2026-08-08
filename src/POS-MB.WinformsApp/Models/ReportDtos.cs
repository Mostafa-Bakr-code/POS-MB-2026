namespace POS_MB.WinformsApp.Models;

public enum ReportType
{
    SalesSummary,
    ItemSales,
    TopSellers,
    StaffPerformance
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
