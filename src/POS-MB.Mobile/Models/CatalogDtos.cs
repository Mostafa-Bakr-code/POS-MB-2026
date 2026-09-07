using Microsoft.Maui.Graphics;
using POS_MB.Mobile.Api;

namespace POS_MB.Mobile.Models;

public class CategoryDto
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? ImageUrl { get; set; }

    // Same resolution as ItemDto's - the API only ever stores/returns a
    // relative path.
    public bool HasImage => ImageUrl is not null;
    public bool HasNoImage => ImageUrl is null;
    public string? FullImageUrl => ImageUrl is null ? null : $"{ApiConfig.BaseUrl.TrimEnd('/')}{ImageUrl}";
}

public class ItemDto
{
    public int ItemId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public bool IsAvailable { get; set; }
    public string? ImageUrl { get; set; }
    public string? Description { get; set; }

    // Getter-only - never part of the JSON payload, just a convenience for
    // XAML binding (there's no built-in "!" binding converter without
    // writing one), computed fresh from IsAvailable every time it's read.
    public bool IsUnavailable => !IsAvailable;
    public Color NameTextColor => IsAvailable ? Colors.Black : Colors.Gray;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    // The API only ever stores/returns a relative path (it doesn't know its
    // own externally-reachable address) - resolved against ApiConfig.BaseUrl
    // here so the menu page can bind an Image source directly.
    public bool HasImage => ImageUrl is not null;
    public bool HasNoImage => ImageUrl is null;
    public string? FullImageUrl => ImageUrl is null ? null : $"{ApiConfig.BaseUrl.TrimEnd('/')}{ImageUrl}";
}
