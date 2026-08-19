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
    // OrderHistory (the full, date-filterable browse screen) and Orders (a
    // live single-day working queue - the WinForms Order Status screen and
    // the chef tablet both need to list today's orders without also granting
    // access to historical browsing) legitimately share this one endpoint -
    // either permission is enough.
    [HttpGet]
    [RequirePermission(Permission.OrderHistory | Permission.Orders)]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null, [FromQuery] OrderSource? orderSource = null)
    {
        var orders = await orderBusiness.GetAllAsync(startDate, endDate, orderSource);
        return Ok(orders);
    }

    // GetById() and Create() both need this exact response shape, but GetById()
    // carries its own permission check, which would wrongly block a cashier
    // (Orders only, no OrderHistory) from seeing the receipt for the order
    // they just placed - so both call this shared, unchecked builder instead
    // of Create() calling GetById() directly (previously duplicated
    // byte-for-byte between the two instead of one delegating to the other).
    // Orders is also what an Orders-only working-queue client (the WinForms
    // Order Status screen's "View" button, the chef tablet's card detail)
    // needs - same reasoning as GetAll above.
    [HttpGet("{id:int}")]
    [RequirePermission(Permission.OrderHistory | Permission.Orders)]
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
        var cancelled = await orderBusiness.CancelAsync(id, $"Staff: {User.GetUserName()}");
        if (cancelled)
            logger.LogInformation("User {UserName} cancelled order OrderId={OrderId}", User.GetUserName(), id);
        return cancelled ? NoContent() : NotFound();
    }

    // The cashier-PC kitchen-ticket poller (WinForms) needs full order+item
    // detail for every order still awaiting a print, regardless of which
    // client (tablet or WinForms) moved it into Preparing - see
    // clsOrderDataAccess.GetOrdersNeedingKitchenTicketAsync for why printing
    // is decoupled from whichever screen did the accepting.
    [HttpGet("needing-kitchen-ticket")]
    [RequirePermission(Permission.Orders)]
    public async Task<IActionResult> GetOrdersNeedingKitchenTicket()
    {
        var orders = await orderBusiness.GetOrdersNeedingKitchenTicketAsync();

        var results = new List<object>();
        foreach (var order in orders)
            results.Add(await BuildOrderDtoAsync(order));

        return Ok(results);
    }

    [HttpPost("{id:int}/mark-kitchen-ticket-printed")]
    [RequirePermission(Permission.Orders)]
    public async Task<IActionResult> MarkKitchenTicketPrinted(int id)
    {
        var marked = await orderBusiness.MarkKitchenTicketPrintedAsync(id);
        return marked ? NoContent() : NotFound();
    }

    private async Task<IActionResult> BuildOrderResponseAsync(int id)
    {
        var order = await orderBusiness.GetByIdAsync(id);
        if (order is null) return NotFound();

        return Ok(await BuildOrderDtoAsync(order));
    }

    private async Task<object> BuildOrderDtoAsync(Order order)
    {
        var items = await orderBusiness.GetItemsByOrderIdAsync(order.OrderId);
        return new { order.OrderId, order.Date, order.Total, order.SerialNumber, order.UserId, order.StudentId, order.CashierName, order.StudentEmail, order.OrderSource, order.Status, order.IsComplimentary, order.PaymobTransactionId, order.RefundedAt, order.RefundTransactionId, order.CreatedAt, order.UpdatedAt, Items = items };
    }
}

// Comment length matches OrderItems.Comment NVARCHAR(50) - this is the exact
// column that originally crashed with an unhandled 500 (see GlobalExceptionHandler)
// before any length validation existed anywhere in the request pipeline.
public record CreateOrderRequest(OrderSource OrderSource, int? UserId, bool IsComplimentary, [Required, MinLength(1)] List<CreateOrderItemRequest> Items);
public record CreateOrderItemRequest(int ItemId, [Range(1, int.MaxValue)] int Quantity, [StringLength(50)] string? Comment);
public record UpdateOrderStatusRequest(OrderStatus Status);
