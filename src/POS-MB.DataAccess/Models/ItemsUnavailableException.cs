namespace POS_MB.DataAccess.Models;

public record UnavailableItem(int ItemId, string ItemName, string Reason);

// Carries structured per-item detail, not just a display string - so a client
// (the mobile cart in particular) can reliably identify and auto-remove exactly
// the items that failed, rather than parsing a human-readable message or
// forcing the user to manually figure out which item was rejected.
public class ItemsUnavailableException(IReadOnlyList<UnavailableItem> items)
    : Exception("One or more items in this order are no longer available.")
{
    public IReadOnlyList<UnavailableItem> Items { get; } = items;
}
