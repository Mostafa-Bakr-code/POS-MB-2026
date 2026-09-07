namespace POS_MB.DataAccess.Models;

public class Category
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    // Relative path under wwwroot (e.g. "/category-images/3.jpg?v=...") - set
    // via POST /api/categories/{id}/image. Null means no photo uploaded yet.
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
