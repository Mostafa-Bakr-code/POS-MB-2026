namespace POS_MB.DataAccess.Models;

public class OrderItem
{
    public int OrderItemId { get; set; }
    public int OrderId { get; set; }
    public int ItemId { get; set; }
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal TotalItemsPrice { get; set; }

    // Snapshot of the item's tax rate at time of sale - see project memory
    // on why this exists (historical tax accuracy, not today's Items.TaxRate).
    public decimal TaxRate { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }

    // Resolved via a LEFT JOIN to Items, not stored on this table - only
    // populated by clsOrderDataAccess.GetItemsWithNamesByOrderIdAsync (used
    // to build Paymob's optional itemized checkout display). Every other
    // caller of GetItemsByOrderIdAsync resolves names by cross-referencing
    // the item catalog separately and leaves this null.
    public string? ItemName { get; set; }

    public decimal InitialPrice => TotalItemsPrice / (1 + (TaxRate / 100));
    public decimal TaxValue => InitialPrice * (TaxRate / 100);
}
