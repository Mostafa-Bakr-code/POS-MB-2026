using Microsoft.Maui.Graphics;
using POS_MB.Mobile.Models;

namespace POS_MB.Mobile.Session;

public class CartLine
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string? Comment { get; set; }
    public decimal Subtotal => Price * Quantity;

    // Display-only helpers so CartPage's DataTemplate can bind directly,
    // matching the cashier side's "gray placeholder vs real comment" look
    // without needing a value converter for a single simple case.
    public string DisplayComment => string.IsNullOrWhiteSpace(Comment) ? "No comment" : Comment;
    public Color CommentTextColor => string.IsNullOrWhiteSpace(Comment) ? Colors.Gray : Colors.Black;
    public string CommentButtonText => string.IsNullOrWhiteSpace(Comment) ? "Add Comment" : "Edit Comment";
}

// In-memory only, same as AppSession - lost if the app closes, which is fine
// for a cart (nobody expects an abandoned cart to survive a restart).
//
// Deliberately never merges same-item additions into one line with an
// incremented quantity - mirrors WinForms' OrderTakingControl exactly (see
// AddToCart's own comment there): tapping "+" on the same item twice adds two
// SEPARATE lines, each with its own comment, because two hot dogs might need
// two different comments ("no onions" vs "extra spicy"), not one shared field.
public static class Cart
{
    public static List<CartLine> Lines { get; } = [];

    // Groups with any existing line(s) for the same item instead of always
    // appending at the bottom - so tapping the same item again from the menu
    // lands right next to its other line(s), same reasoning as AddAnother
    // below (the cart's own "+1" button).
    public static void Add(ItemDto item)
    {
        var lastIndex = Lines.FindLastIndex(l => l.ItemId == item.ItemId);
        var newLine = new CartLine { ItemId = item.ItemId, ItemName = item.ItemName, Price = item.Price, Quantity = 1 };

        if (lastIndex >= 0)
            Lines.Insert(lastIndex + 1, newLine);
        else
            Lines.Add(newLine);
    }

    // Used by the cart screen's own "+1" button (WinForms' btnAddOne
    // equivalent) - adds another separate line for the same item without
    // needing to go back to the menu. Inserted right after the line it was
    // pressed on (not appended at the bottom) so the kitchen sees same-item
    // lines grouped together and can prepare them as a batch.
    public static void AddAnother(CartLine existing)
    {
        var index = Lines.IndexOf(existing);
        Lines.Insert(index + 1, new CartLine { ItemId = existing.ItemId, ItemName = existing.ItemName, Price = existing.Price, Quantity = 1 });
    }

    public static void Remove(CartLine line) => Lines.Remove(line);

    public static int TotalItemCount => Lines.Sum(l => l.Quantity);
    public static decimal Total => Lines.Sum(l => l.Subtotal);

    public static void Clear() => Lines.Clear();
}
