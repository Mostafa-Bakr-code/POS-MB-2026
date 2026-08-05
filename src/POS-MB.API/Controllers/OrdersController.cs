using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;
using POS_MB.DataAccess.Models;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(clsOrderBusiness orderBusiness) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.OrderHistory)]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null, [FromQuery] OrderSource? orderSource = null)
    {
        var orders = await orderBusiness.GetAllAsync(startDate, endDate, orderSource);
        return Ok(orders);
    }

    [HttpGet("{id:int}")]
    [RequirePermission(Permission.OrderHistory)]
    public async Task<IActionResult> GetById(int id)
    {
        var order = await orderBusiness.GetByIdAsync(id);
        if (order is null) return NotFound();

        var items = await orderBusiness.GetItemsByOrderIdAsync(id);
        return Ok(new { order.OrderId, order.Date, order.Total, order.SerialNumber, order.UserId, order.OrderSource, order.Status, order.IsComplimentary, order.CreatedAt, order.UpdatedAt, Items = items });
    }

    [HttpPost]
    [RequirePermission(Permission.Orders)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        // The Complimentary toggle is only hidden client-side for cashiers who
        // lack it - a raw request could otherwise mark any order free regardless
        // of the Orders permission check above, so it needs its own check.
        if (request.IsComplimentary && !User.HasPermission(Permission.Complimentary))
            return Forbid();

        var items = request.Items
            .Select(i => new NewOrderItem(i.ItemId, i.Quantity, i.Comment))
            .ToList();

        var id = await orderBusiness.CreateOrderAsync(request.OrderSource, request.UserId, request.IsComplimentary, items);
        return await GetByIdInternal(id);
    }

    [HttpPut("{id:int}/status")]
    [RequirePermission(Permission.Orders)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateOrderStatusRequest request)
    {
        var updated = await orderBusiness.UpdateStatusAsync(id, request.Status);
        return updated ? NoContent() : NotFound();
    }

    [HttpPost("{id:int}/cancel")]
    [RequirePermission(Permission.Orders)]
    public async Task<IActionResult> Cancel(int id)
    {
        var cancelled = await orderBusiness.CancelAsync(id);
        return cancelled ? NoContent() : NotFound();
    }

    // Create() needs to return the same shape as GetById() but GetById() now
    // carries its own [RequirePermission(OrderHistory)] check, which would
    // wrongly block a cashier (Orders only, no OrderHistory) from seeing the
    // receipt for the order they just placed - so Create() builds its own
    // response via this shared, unchecked helper instead of calling GetById().
    private async Task<IActionResult> GetByIdInternal(int id)
    {
        var order = await orderBusiness.GetByIdAsync(id);
        if (order is null) return NotFound();

        var items = await orderBusiness.GetItemsByOrderIdAsync(id);
        return Ok(new { order.OrderId, order.Date, order.Total, order.SerialNumber, order.UserId, order.OrderSource, order.Status, order.IsComplimentary, order.CreatedAt, order.UpdatedAt, Items = items });
    }
}

public record CreateOrderRequest(OrderSource OrderSource, int? UserId, bool IsComplimentary, List<CreateOrderItemRequest> Items);
public record CreateOrderItemRequest(int ItemId, int Quantity, string? Comment);
public record UpdateOrderStatusRequest(OrderStatus Status);
