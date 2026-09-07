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

    // Relative path under wwwroot (e.g. "/item-images/12.jpg?v=...") - set via
    // POST /api/items/{id}/image. Null means no photo has been uploaded yet.
    public string? ImageUrl { get; set; }

    // Optional menu blurb ("grilled chicken, garlic sauce, pickles") - purely
    // display metadata, unrelated to OrderItem.Comment (a per-order customer
    // note like "no onions").
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Derived from Price/TaxRate, not stored — see project memory on why
    // InitialPrice/TaxValue were dropped from the Items table.
    public decimal InitialPrice => Price / (1 + (TaxRate / 100));
    public decimal TaxValue => InitialPrice * (TaxRate / 100);
}
