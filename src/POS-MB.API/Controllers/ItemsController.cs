using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ItemsController(clsItemBusiness itemBusiness, IWebHostEnvironment webHostEnvironment) : ControllerBase
{
    // Only these - a menu photo has no reason to be anything else, and
    // restricting the extension list avoids ever writing something an
    // uploader didn't intend to serve as a static file under wwwroot.
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };
    private const long MaxImageSizeBytes = 5 * 1024 * 1024; // 5MB - a menu photo, not a print-quality asset
    // Read-only endpoints stay open to any authenticated user - order-taking
    // needs the item catalog for every cashier, not just ones with the Items
    // management permission.
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false, [FromQuery] int? categoryId = null, [FromQuery] bool availableOnly = false)
    {
        var items = await itemBusiness.GetAllAsync(includeInactive, categoryId, availableOnly);
        return Ok(items);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await itemBusiness.GetByIdAsync(id);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    [RequirePermission(Permission.Items)]
    public async Task<IActionResult> Create([FromBody] CreateItemRequest request)
    {
        var id = await itemBusiness.CreateAsync(request.Name, request.CategoryId, request.Price, request.TaxRate, request.Description);
        var item = await itemBusiness.GetByIdAsync(id);
        return CreatedAtAction(nameof(GetById), new { id }, item);
    }

    [HttpPut("{id:int}")]
    [RequirePermission(Permission.Items)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateItemRequest request)
    {
        // changedByUserId always comes from the caller's own token, never the
        // request body - a client-supplied id here would let anyone with the
        // Items permission attribute their own price change to a different
        // employee in the price-history audit trail (GET .../price-history is
        // open to any authenticated user). Same class of bug as order attribution
        // in OrdersController.Create.
        var updated = await itemBusiness.UpdateAsync(id, request.Name, request.CategoryId, request.Price, request.TaxRate, User.GetUserId(), request.Description);
        return updated ? NoContent() : NotFound();
    }

    [HttpGet("{id:int}/price-history")]
    public async Task<IActionResult> GetPriceHistory(int id)
    {
        var history = await itemBusiness.GetPriceHistoryAsync(id);
        return Ok(history);
    }

    [HttpPost("{id:int}/deactivate")]
    [RequirePermission(Permission.Items)]
    public async Task<IActionResult> Deactivate(int id)
    {
        var deactivated = await itemBusiness.DeactivateAsync(id);
        return deactivated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/reactivate")]
    [RequirePermission(Permission.Items)]
    public async Task<IActionResult> Reactivate(int id)
    {
        var reactivated = await itemBusiness.ReactivateAsync(id);
        return reactivated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/availability")]
    [RequirePermission(Permission.Items)]
    public async Task<IActionResult> SetAvailability(int id, [FromBody] SetItemAvailabilityRequest request)
    {
        var updated = await itemBusiness.SetAvailabilityAsync(id, request.IsAvailable);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/image")]
    [RequirePermission(Permission.Items)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        if (!await itemBusiness.ExistsAsync(id)) return NotFound();

        if (file.Length == 0) return BadRequest("No file was uploaded.");
        if (file.Length > MaxImageSizeBytes) return BadRequest("Image must be 5MB or smaller.");

        var extension = Path.GetExtension(file.FileName);
        if (!AllowedImageExtensions.Contains(extension)) return BadRequest("Only .jpg and .png images are allowed.");

        var imagesDirectory = Path.Combine(webHostEnvironment.WebRootPath, "item-images");
        Directory.CreateDirectory(imagesDirectory);

        // Named by ItemId, not a generated filename - a re-upload for the same
        // item just overwrites its one photo, so there's never a stale file
        // left behind under a different name for this item.
        var filePath = Path.Combine(imagesDirectory, $"{id}{extension}");
        await using (var stream = System.IO.File.Create(filePath))
            await file.CopyToAsync(stream);

        // ?v={ticks} busts the mobile app's image cache on re-upload - without
        // it, the URL (keyed by ItemId) never changes, so a client that already
        // cached the old photo would keep showing it after a re-upload.
        var imageUrl = $"/item-images/{id}{extension}?v={DateTime.UtcNow.Ticks}";
        await itemBusiness.SetImageUrlAsync(id, imageUrl);

        var item = await itemBusiness.GetByIdAsync(id);
        return Ok(item);
    }

    [HttpDelete("{id:int}/image")]
    [RequirePermission(Permission.Items)]
    public async Task<IActionResult> RemoveImage(int id)
    {
        var item = await itemBusiness.GetByIdAsync(id);
        if (item is null) return NotFound();

        if (item.ImageUrl is not null)
        {
            var fileName = item.ImageUrl.Split('?')[0].TrimStart('/');
            var filePath = Path.Combine(webHostEnvironment.WebRootPath, fileName.Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
        }

        await itemBusiness.SetImageUrlAsync(id, null);
        return NoContent();
    }
}

// Name length matches Items.ItemName NVARCHAR(50). Price/TaxRate ranges are
// sanity bounds, not DB limits - DECIMAL(18,4) could hold far more than a real
// menu price or tax rate should ever be.
public record CreateItemRequest(
    [Required, StringLength(50)] string Name,
    int CategoryId,
    [Range(typeof(decimal), "0", "100000")] decimal Price,
    [Range(typeof(decimal), "0", "100")] decimal? TaxRate,
    [StringLength(500)] string? Description = null);

public record UpdateItemRequest(
    [Required, StringLength(50)] string Name,
    int CategoryId,
    [Range(typeof(decimal), "0", "100000")] decimal Price,
    [Range(typeof(decimal), "0", "100")] decimal? TaxRate,
    [StringLength(500)] string? Description = null);

public record SetItemAvailabilityRequest(bool IsAvailable);
