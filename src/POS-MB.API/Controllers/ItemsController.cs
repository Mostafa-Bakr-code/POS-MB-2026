using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ItemsController(clsItemBusiness itemBusiness) : ControllerBase
{
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
        var id = await itemBusiness.CreateAsync(request.Name, request.CategoryId, request.Price, request.TaxRate);
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
        var updated = await itemBusiness.UpdateAsync(id, request.Name, request.CategoryId, request.Price, request.TaxRate, User.GetUserId());
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
}

// Name length matches Items.ItemName NVARCHAR(50). Price/TaxRate ranges are
// sanity bounds, not DB limits - DECIMAL(18,4) could hold far more than a real
// menu price or tax rate should ever be.
public record CreateItemRequest(
    [Required, StringLength(50)] string Name,
    int CategoryId,
    [Range(typeof(decimal), "0", "100000")] decimal Price,
    [Range(typeof(decimal), "0", "100")] decimal? TaxRate);

public record UpdateItemRequest(
    [Required, StringLength(50)] string Name,
    int CategoryId,
    [Range(typeof(decimal), "0", "100000")] decimal Price,
    [Range(typeof(decimal), "0", "100")] decimal? TaxRate);

public record SetItemAvailabilityRequest(bool IsAvailable);
