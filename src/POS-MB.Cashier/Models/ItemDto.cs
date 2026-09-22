namespace POS_MB.Cashier.Models;

public class ItemDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public decimal Price { get; set; }
    public decimal TaxRate { get; set; }
    public bool IsActive { get; set; }
    public bool IsAvailable { get; set; }
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }
}

public class ItemPriceHistoryDto
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
