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
    public string? ImageUrl { get; set; }
    public decimal Subtotal => Price * Quantity;

    // True for a line added via another line's "+Extra" button rather than
    // tapped directly off the menu - see Cart.AddExtra below. Only ever
    // populated on a non-extra line - one level of nesting, same reasoning
    // as the cashier side's own CartLine.
    public bool IsExtra { get; set; }
    public List<CartLine> Extras { get; } = [];
    public bool CanAddExtra => !IsExtra;

    // Display-only helpers so CartPage's DataTemplate can bind directly,
    // matching the cashier side's "gray placeholder vs real comment" look
    // without needing a value converter for a single simple case.
    public string DisplayComment => string.IsNullOrWhiteSpace(Comment) ? "No comment" : Comment;
    public Color CommentTextColor => string.IsNullOrWhiteSpace(Comment) ? Colors.Gray : Colors.Black;
    public string CommentButtonText => string.IsNullOrWhiteSpace(Comment) ? "Add Comment" : "Edit Comment";

    // Same resolution as ItemDto.FullImageUrl - the API only ever
    // stores/returns a relative path.
    public bool HasImage => ImageUrl is not null;
    public bool HasNoImage => ImageUrl is null;
    public string? FullImageUrl => ImageUrl is null ? null : $"{Api.ApiConfig.BaseUrl.TrimEnd('/')}{ImageUrl}";
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
    public static void Add(ItemDto item) =>
        InsertGrouped(Lines, new CartLine { ItemId = item.ItemId, ItemName = item.ItemName, Price = item.Price, Quantity = 1, ImageUrl = item.ImageUrl });

    // An extra always attaches to the ONE specific line it was requested
    // for, not to the cart generally - so two separate occurrences of the
    // same base item can carry two different extras without either the
    // in-app cart or the kitchen ticket being ambiguous about which extra
    // belongs to which. See CartPage's own "+Extra" button.
    public static void AddExtra(CartLine parentLine, ItemDto extraItem) =>
        InsertGrouped(parentLine.Extras, new CartLine { ItemId = extraItem.ItemId, ItemName = extraItem.ItemName, Price = extraItem.Price, Quantity = 1, ImageUrl = extraItem.ImageUrl, IsExtra = true });

    // Shared by base items (against Lines) and extras (against their one
    // parent's own Extras list) - groups with any existing line for the
    // same item instead of always appending at the bottom.
    private static void InsertGrouped(List<CartLine> list, CartLine newLine)
    {
        var lastIndex = list.FindLastIndex(l => l.ItemId == newLine.ItemId);
        if (lastIndex >= 0)
            list.Insert(lastIndex + 1, newLine);
        else
            list.Add(newLine);
    }

    // Used by the cart screen's own "+1" button (WinForms' btnAddOne
    // equivalent) - adds another separate line for the same item without
    // needing to go back to the menu. Inserted right after the line it was
    // pressed on (not appended at the bottom) so the kitchen sees same-item
    // lines grouped together and can prepare them as a batch. Works for an
    // extra line too, via FindOwningList - it lands in that same extra's
    // parent, not at the top level.
    public static void AddAnother(CartLine existing)
    {
        var list = FindOwningList(existing);
        var index = list.IndexOf(existing);
        list.Insert(index + 1, new CartLine { ItemId = existing.ItemId, ItemName = existing.ItemName, Price = existing.Price, Quantity = 1, ImageUrl = existing.ImageUrl, IsExtra = existing.IsExtra });
    }

    public static void Remove(CartLine line) => FindOwningList(line).Remove(line);

    // A cart line can live at the top level OR inside some other line's
    // Extras - AddAnother/Remove are shared by both via this lookup rather
    // than needing two separate code paths.
    private static List<CartLine> FindOwningList(CartLine target)
    {
        if (Lines.Contains(target)) return Lines;
        foreach (var line in Lines)
            if (line.Extras.Contains(target)) return line.Extras;
        return Lines;
    }

    // Flattened: each base item immediately followed by its own extras,
    // then the next base item - what actually gets sent to the server (see
    // CartPage.OnPlaceOrderClicked) and summed below, so an extra's price
    // and quantity are never silently dropped from the total/order.
    public static IEnumerable<CartLine> Flattened => Lines.SelectMany(l => new[] { l }.Concat(l.Extras));

    public static int TotalItemCount => Flattened.Sum(l => l.Quantity);
    public static decimal Total => Flattened.Sum(l => l.Subtotal);

    public static void Clear() => Lines.Clear();

    // Used when the server rejects specific items at checkout (see
    // CartPage.OnPlaceOrderClicked) - sweeps extras too, not just the top
    // level, since an extra that went unavailable needs removing from
    // whichever line it was attached to just as much as a base item would.
    public static void RemoveAllWhere(Predicate<CartLine> predicate)
    {
        Lines.RemoveAll(predicate);
        foreach (var line in Lines)
            line.Extras.RemoveAll(predicate);
    }
}
