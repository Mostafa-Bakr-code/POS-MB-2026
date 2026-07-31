namespace POS_MB.DataAccess.Models.Reports;

public class ItemSalesRow
{
    public DateTime? OrderDate { get; set; }
    // Only populated when the report is grouped by price (see groupByPrice) - the exact
    // historical price this row's units were sold at, not today's catalog price.
    public decimal? SoldAtPrice { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Revenue { get; set; }
    public decimal ComplimentaryValue { get; set; }
    public decimal Tax { get; set; }
    public decimal RevenueExcludingTax => Revenue - Tax;
    public decimal AveragePrice => Quantity == 0 ? 0 : (Revenue + ComplimentaryValue) / Quantity;
}
