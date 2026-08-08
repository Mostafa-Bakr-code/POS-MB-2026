using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;
using POS_MB.DataAccess.Models;

namespace POS_MB.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(clsOrderBusiness orderBusiness, ILogger<OrdersController> logger) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permission.OrderHistory)]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null, [FromQuery] OrderSource? orderSource = null)
    {
        var orders = await orderBusiness.GetAllAsync(startDate, endDate, orderSource);
        return Ok(orders);
    }

    // GetById() and Create() both need this exact response shape, but GetById()
    // carries its own [RequirePermission(OrderHistory)] check, which would wrongly
    // block a cashier (Orders only, no OrderHistory) from seeing the receipt for
    // the order they just placed - so both call this shared, unchecked builder
    // instead of Create() calling GetById() directly (previously duplicated
    // byte-for-byte between the two instead of one delegating to the other).
    [HttpGet("{id:int}")]
    [RequirePermission(Permission.OrderHistory)]
    public Task<IActionResult> GetById(int id) => BuildOrderResponseAsync(id);

    [HttpPost]
    [RequirePermission(Permission.Orders)]
    public async Task<IActionResult> Create([FromBody] CreateOrderRequest request)
    {
        // The Complimentary toggle is only hidden client-side for cashiers who
        // lack it - a raw request could otherwise mark any order free regardless
        // of the Orders permission check above, so it needs its own check.
        if (request.IsComplimentary && !User.HasPermission(Permission.Complimentary))
        {
            logger.LogWarning("User {UserName} denied: attempted a complimentary order without the Complimentary permission",
                User.GetUserName());
            return Forbid();
        }

        var items = request.Items
            .Select(i => new NewOrderItem(i.ItemId, i.Quantity, i.Comment))
            .ToList();

        // A cashier order is always attributed to whoever is actually logged in -
        // never a client-supplied id, which would let any cashier attribute their
        // sale to a coworker and corrupt the staff performance report. Any other
        // OrderSource (today, only Mobile) has no legitimate use for a
        // client-supplied UserId either - there's no student account system yet
        // to attribute a mobile order to - so it's forced null rather than
        // trusted from the body. Originally this only special-cased Cashier and
        // let non-Cashier values pass request.UserId through unchecked, which
        // meant a cashier could bypass the whole protection just by sending
        // OrderSource: Mobile with an arbitrary UserId.
        var userId = request.OrderSource == OrderSource.Cashier
            ? User.GetUserId()
            : (int?)null;

        var id = await orderBusiness.CreateOrderAsync(request.OrderSource, userId, studentId: null, request.IsComplimentary, items);
        return await BuildOrderResponseAsync(id);
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
        if (cancelled)
            logger.LogInformation("User {UserName} cancelled order OrderId={OrderId}", User.GetUserName(), id);
        return cancelled ? NoContent() : NotFound();
    }

    private async Task<IActionResult> BuildOrderResponseAsync(int id)
    {
        var order = await orderBusiness.GetByIdAsync(id);
        if (order is null) return NotFound();

        var items = await orderBusiness.GetItemsByOrderIdAsync(id);
        return Ok(new { order.OrderId, order.Date, order.Total, order.SerialNumber, order.UserId, order.StudentId, order.OrderSource, order.Status, order.IsComplimentary, order.CreatedAt, order.UpdatedAt, Items = items });
    }
}

// Comment length matches OrderItems.Comment NVARCHAR(50) - this is the exact
// column that originally crashed with an unhandled 500 (see GlobalExceptionHandler)
// before any length validation existed anywhere in the request pipeline.
public record CreateOrderRequest(OrderSource OrderSource, int? UserId, bool IsComplimentary, [Required, MinLength(1)] List<CreateOrderItemRequest> Items);
public record CreateOrderItemRequest(int ItemId, [Range(1, int.MaxValue)] int Quantity, [StringLength(50)] string? Comment);
public record UpdateOrderStatusRequest(OrderStatus Status);
