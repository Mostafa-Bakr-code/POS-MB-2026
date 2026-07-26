using Microsoft.AspNetCore.Mvc;
using POS_MB.Business;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController(clsCategoryBusiness categoryBusiness) : ControllerBase
{
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
    public async Task<IActionResult> Create([FromBody] CreateCategoryRequest request)
    {
        var id = await categoryBusiness.CreateAsync(request.Name);
        var category = await categoryBusiness.GetByIdAsync(id);
        return CreatedAtAction(nameof(GetById), new { id }, category);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryRequest request)
    {
        var updated = await categoryBusiness.UpdateAsync(id, request.Name);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var deactivated = await categoryBusiness.DeactivateAsync(id);
        return deactivated ? NoContent() : NotFound();
    }
}

public record CreateCategoryRequest(string Name);
public record UpdateCategoryRequest(string Name);
