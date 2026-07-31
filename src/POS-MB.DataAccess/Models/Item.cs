namespace POS_MB.DataAccess.Models;

public class Item
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public decimal Price { get; set; }
    public decimal TaxRate { get; set; }
    public bool IsActive { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Derived from Price/TaxRate, not stored — see project memory on why
    // InitialPrice/TaxValue were dropped from the Items table.
    public decimal InitialPrice => Price / (1 + (TaxRate / 100));
    public decimal TaxValue => InitialPrice * (TaxRate / 100);
}
