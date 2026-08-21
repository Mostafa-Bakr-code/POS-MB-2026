using Microsoft.Maui.Graphics;

namespace POS_MB.Mobile.Models;

public class CategoryDto
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class ItemDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public bool IsAvailable { get; set; }

    // Getter-only - never part of the JSON payload, just a convenience for
    // XAML binding (there's no built-in "!" binding converter without
    // writing one), computed fresh from IsAvailable every time it's read.
    public bool IsUnavailable => !IsAvailable;
    public Color NameTextColor => IsAvailable ? Colors.Black : Colors.Gray;
}
