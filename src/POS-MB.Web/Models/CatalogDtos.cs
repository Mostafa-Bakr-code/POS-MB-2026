using POS_MB.Web.Api;

namespace POS_MB.Web.Models;

// Mirrors POS-MB.Mobile's Models/CatalogDtos.cs - the MAUI Color helpers
// (NameTextColor etc.) don't apply here, a Razor component just checks
// IsAvailable directly to pick a CSS class instead.
public class CategoryDto
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string? ImageUrl { get; set; }

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

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string? FullImageUrl => ImageUrl is null ? null : $"{ApiConfig.BaseUrl.TrimEnd('/')}{ImageUrl}";
}
