using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController(clsCategoryBusiness categoryBusiness, IWebHostEnvironment webHostEnvironment) : ControllerBase
{
    // Same restrictions as ItemsController's image upload - see there for why.
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };
    private const long MaxImageSizeBytes = 5 * 1024 * 1024;
    // Read-only endpoints stay open to any authenticated user - the order-taking
    // screen needs the category list for every cashier, not just ones with the
    // Categories management permission.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false)
    {
        var categories = await categoryBusiness.GetAllAsync(includeInactive);
        return Ok(categories);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var category = await categoryBusiness.GetByIdAsync(id);
        return category is null ? NotFound() : Ok(category);
    }

    [HttpPost]
    [RequirePermission(Permission.Categories)]
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        var id = await categoryBusiness.CreateAsync(request.Name);
        var category = await categoryBusiness.GetByIdAsync(id);
        return CreatedAtAction(nameof(GetById), new { id }, category);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permission.Categories)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryRequest request)
    {
        var updated = await categoryBusiness.UpdateAsync(id, request.Name);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/deactivate")]
    [RequirePermission(Permission.Categories)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var deactivated = await categoryBusiness.DeactivateAsync(id);
        return deactivated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/reactivate")]
    [RequirePermission(Permission.Categories)]
    public async Task<IActionResult> Reactivate(int id)
    {
        var reactivated = await categoryBusiness.ReactivateAsync(id);
        return reactivated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/image")]
    [RequirePermission(Permission.Categories)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        if (!await categoryBusiness.ExistsAsync(id)) return NotFound();

        if (file.Length == 0) return BadRequest("No file was uploaded.");
        if (file.Length > MaxImageSizeBytes) return BadRequest("Image must be 5MB or smaller.");

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedImageExtensions.Contains(extension)) return BadRequest("Only .jpg and .png images are allowed.");

        var imagesDirectory = Path.Combine(webHostEnvironment.WebRootPath, "category-images");
        Directory.CreateDirectory(imagesDirectory);

        var filePath = Path.Combine(imagesDirectory, $"{id}{extension}");
        await using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        var imageUrl = $"/category-images/{id}{extension}?v={DateTime.UtcNow.Ticks}";
        await categoryBusiness.SetImageUrlAsync(id, imageUrl);

        var category = await categoryBusiness.GetByIdAsync(id);
        return Ok(category);
    }

    [HttpDelete("{id:int}/image")]
    [RequirePermission(Permission.Categories)]
    public async Task<IActionResult> RemoveImage(int id)
    {
        var category = await categoryBusiness.GetByIdAsync(id);
        if (category is null) return NotFound();

        if (category.ImageUrl is not null)
        {
            var fileName = category.ImageUrl.Split('?')[0].TrimStart('/');
            var filePath = Path.Combine(webHostEnvironment.WebRootPath, fileName.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
        }

        await categoryBusiness.SetImageUrlAsync(id, null);
        return NoContent();
    }
}

// Length matches Categories.CategoryName NVARCHAR(20) in schema.sql - caught
// here with a clean 400 instead of an unhandled SQL truncation error 500 steps
// further down (the same category of bug the order-comment crash was).
public record CreateCategoryRequest([Required, StringLength(20)] string Name);
public record UpdateCategoryRequest([Required, StringLength(20)] string Name);
