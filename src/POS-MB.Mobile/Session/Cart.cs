using POS_MB.Mobile.Models;

namespace POS_MB.Mobile.Session;

public class CartLine
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public decimal Subtotal => Price * Quantity;
}

// In-memory only, same as AppSession - lost if the app closes, which is fine
// for a cart (nobody expects an abandoned cart to survive a restart).
public static class Cart
{
    public static List<CartLine> Lines { get; } = [];

    public static void Add(ItemDto item)
    {
        var existing = Lines.FirstOrDefault(l => l.ItemId == item.ItemId);
        if (existing is not null)
            existing.Quantity++;
        else
            Lines.Add(new CartLine { ItemId = item.ItemId, ItemName = item.ItemName, Price = item.Price, Quantity = 1 });
    }

    public static void Remove(int itemId)
    {
        var existing = Lines.FirstOrDefault(l => l.ItemId == itemId);
        if (existing is null) return;

        existing.Quantity--;
        if (existing.Quantity <= 0) Lines.Remove(existing);
    }

    public static int TotalItemCount => Lines.Sum(l => l.Quantity);
    public static decimal Total => Lines.Sum(l => l.Subtotal);

    public static void Clear() => Lines.Clear();
}
