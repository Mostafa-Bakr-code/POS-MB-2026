namespace POS_MB.DataAccess.Models;

public class ItemPriceHistory
{
    public int ItemPriceHistoryId { get; set; }
    public int ItemId { get; set; }
    public decimal OldPrice { get; set; }
    public decimal NewPrice { get; set; }
    public decimal OldTaxRate { get; set; }
    public decimal NewTaxRate { get; set; }
    public int? ChangedByUserId { get; set; }
    public string? ChangedByUserName { get; set; }
    public DateTime ChangedAt { get; set; }
}
