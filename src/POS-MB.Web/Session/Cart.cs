using POS_MB.Web.Models;

namespace POS_MB.Web.Session;

// Mirrors POS-MB.Mobile's Session/Cart.cs - same grouping/extras logic,
// just without the MAUI Color display helpers (a Razor component picks its
// own CSS class instead of binding to a Color property).
public class CartLine
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string? Comment { get; set; }
    public string? ImageUrl { get; set; }
    public decimal Subtotal => Price * Quantity;

    public bool IsExtra { get; set; }
    public List<CartLine> Extras { get; } = [];
    public bool CanAddExtra => !IsExtra;

    public string DisplayComment => string.IsNullOrWhiteSpace(Comment) ? "No comment" : Comment;

    public bool HasImage => ImageUrl is not null;
    public string? FullImageUrl => ImageUrl is null ? null : $"{Api.ApiConfig.BaseUrl.TrimEnd('/')}{ImageUrl}";
}

// In-memory only, same as AppSession - lost on page refresh, which is an
// acceptable tradeoff for a browser client (same as Mobile losing its cart
// if the app is killed).
public static class Cart
{
    public static List<CartLine> Lines { get; } = [];

    public static void Add(ItemDto item) =>
        InsertGrouped(Lines, new CartLine { ItemId = item.ItemId, ItemName = item.ItemName, Price = item.Price, Quantity = 1, ImageUrl = item.ImageUrl });

    public static void AddExtra(CartLine parentLine, ItemDto extraItem) =>
        InsertGrouped(parentLine.Extras, new CartLine { ItemId = extraItem.ItemId, ItemName = extraItem.ItemName, Price = extraItem.Price, Quantity = 1, ImageUrl = extraItem.ImageUrl, IsExtra = true });

    private static void InsertGrouped(List<CartLine> list, CartLine newLine)
    {
        var lastIndex = list.FindLastIndex(l => l.ItemId == newLine.ItemId);
        if (lastIndex >= 0)
            list.Insert(lastIndex + 1, newLine);
        else
            list.Add(newLine);
    }

    public static void AddAnother(CartLine existing)
    {
        var list = FindOwningList(existing);
        var index = list.IndexOf(existing);
        list.Insert(index + 1, new CartLine { ItemId = existing.ItemId, ItemName = existing.ItemName, Price = existing.Price, Quantity = 1, ImageUrl = existing.ImageUrl, IsExtra = existing.IsExtra });
    }

    public static void Remove(CartLine line) => FindOwningList(line).Remove(line);

    private static List<CartLine> FindOwningList(CartLine target)
    {
        if (Lines.Contains(target)) return Lines;
        foreach (var line in Lines)
            if (line.Extras.Contains(target)) return line.Extras;
        return Lines;
    }

    public static IEnumerable<CartLine> Flattened => Lines.SelectMany(l => new[] { l }.Concat(l.Extras));

    public static int TotalItemCount => Flattened.Sum(l => l.Quantity);
    public static decimal Total => Flattened.Sum(l => l.Subtotal);

    public static void Clear() => Lines.Clear();

    public static void RemoveAllWhere(Predicate<CartLine> predicate)
    {
        Lines.RemoveAll(predicate);
        foreach (var line in Lines)
            line.Extras.RemoveAll(predicate);
    }
}
