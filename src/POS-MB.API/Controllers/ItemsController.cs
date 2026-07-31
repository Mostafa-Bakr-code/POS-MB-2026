using Microsoft.AspNetCore.Mvc;
using POS_MB.Business;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ItemsController(clsItemBusiness itemBusiness) : ControllerBase
{
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
    public async Task<IActionResult> Create([FromBody] CreateItemRequest request)
    {
        var id = await itemBusiness.CreateAsync(request.Name, request.CategoryId, request.Price, request.TaxRate);
        var item = await itemBusiness.GetByIdAsync(id);
        return CreatedAtAction(nameof(GetById), new { id }, item);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateItemRequest request)
    {
        var updated = await itemBusiness.UpdateAsync(id, request.Name, request.CategoryId, request.Price, request.TaxRate);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var deactivated = await itemBusiness.DeactivateAsync(id);
        return deactivated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/availability")]
    public async Task<IActionResult> SetAvailability(int id, [FromBody] SetItemAvailabilityRequest request)
    {
        var updated = await itemBusiness.SetAvailabilityAsync(id, request.IsAvailable);
        return updated ? NoContent() : NotFound();
    }
}

public record CreateItemRequest(string Name, int CategoryId, decimal Price, decimal? TaxRate);
public record UpdateItemRequest(string Name, int CategoryId, decimal Price, decimal? TaxRate);
public record SetItemAvailabilityRequest(bool IsAvailable);
