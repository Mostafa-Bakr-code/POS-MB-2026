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
}
