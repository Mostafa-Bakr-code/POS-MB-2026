using Microsoft.AspNetCore.Mvc;
using POS_MB.Business;
using POS_MB.DataAccess.Models;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(clsOrderBusiness orderBusiness) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        var orders = await orderBusiness.GetAllAsync(startDate, endDate);
        return Ok(orders);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await orderBusiness.GetByIdAsync(id);
        if (order is null) return NotFound();

        var items = await orderBusiness.GetItemsByOrderIdAsync(id);
        return Ok(new { order.OrderId, order.Date, order.Total, order.SerialNumber, order.UserId, order.Status, order.IsComplimentary, order.CreatedAt, order.UpdatedAt, Items = items });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        var items = request.Items
            .Select(i => new NewOrderItem(i.ItemId, i.Quantity, i.Comment))
            .ToList();

        var id = await orderBusiness.CreateOrderAsync(request.UserId, request.IsComplimentary, items);
        return await GetById(id);
    }

    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest request)
    {
        var updated = await orderBusiness.UpdateStatusAsync(id, request.Status);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var cancelled = await orderBusiness.CancelAsync(id);
        return cancelled ? NoContent() : NotFound();
    }
}

public record CreateOrderRequest(int UserId, bool IsComplimentary, List<CreateOrderItemRequest> Items);
public record CreateOrderItemRequest(int ItemId, int Quantity, string? Comment);
public record UpdateOrderStatusRequest(OrderStatus Status);
