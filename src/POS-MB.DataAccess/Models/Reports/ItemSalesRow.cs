namespace POS_MB.DataAccess.Models.Reports;

public class ItemSalesRow
{
    public DateTime? OrderDate { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Revenue { get; set; }
    public decimal ComplimentaryValue { get; set; }
    public decimal AveragePrice => Quantity == 0 ? 0 : (Revenue + ComplimentaryValue) / Quantity;
}
