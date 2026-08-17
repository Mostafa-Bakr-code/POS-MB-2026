using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using POS_MB.API.Auth;
using POS_MB.Business;
using POS_MB.DataAccess.Models;

namespace POS_MB.API.Controllers;

// Separate from OrdersController on purpose - staff orders and student orders
// have different authorization (Permission bitmask vs RequireStudent),
// different attribution (UserId vs StudentId), and no legitimate reason for a
// student token to ever reach the staff-facing endpoints or vice versa.
[ApiController]
[Route("api/students/orders")]
[RequireStudent]
public class StudentOrdersController(clsOrderBusiness orderBusiness, ILogger<StudentOrdersController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] DateTime? startDate = null, [FromQuery] DateTime? endDate = null)
    {
        var orders = await orderBusiness.GetAllForStudentAsync(User.GetUserId(), startDate, endDate);
        return Ok(orders);
    }

    // The StudentId = @StudentId clause in GetByIdForStudentAsync is what makes
    // this safe - a student can't view another student's order just by guessing
    // an OrderId, the query itself never returns a row that isn't theirs.
    [HttpGet("{id:int}")]
    public Task<IActionResult> GetById(int id) => BuildOrderResponseAsync(id);

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStudentOrderRequest request)
    {
        var studentId = User.GetUserId();

        var items = request.Items
            .Select(i => new NewOrderItem(i.ItemId, i.Quantity, i.Comment))
            .ToList();

        // User.GetUserName() is the student's email - see JwtTokenService,
        // the "name" claim on a student token is always set to their email,
        // so this needs no extra lookup.
        var (id, checkoutUrl) = await orderBusiness.CreateStudentOrderAsync(studentId, User.GetUserName(), items, request.UseSavedCard);

        logger.LogInformation("Student {Email} started checkout for OrderId={OrderId}", User.GetUserName(), id);

        var order = await orderBusiness.GetByIdForStudentAsync(id, studentId);
        if (order is null) return NotFound();

        var orderItems = await orderBusiness.GetItemsByOrderIdAsync(id);
        return Ok(new
        {
            order.OrderId, order.Date, order.Total, order.SerialNumber, order.Status, order.CreatedAt, order.UpdatedAt,
            Items = orderItems,
            CheckoutUrl = checkoutUrl
        });
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var cancelled = await orderBusiness.CancelForStudentAsync(id, User.GetUserId());
        if (cancelled)
            logger.LogInformation("Student {Email} cancelled order OrderId={OrderId}", User.GetUserName(), id);
        return cancelled ? NoContent() : NotFound();
    }

    // For an order stuck at AwaitingPayment - backed out of the payment
    // screen, app crashed mid-checkout, connectivity blip - rather than
    // making the student wait out the auto-cancel timeout with no recourse.
    [HttpPost("{id:int}/resume-checkout")]
    public async Task<IActionResult> ResumeCheckout(int id)
    {
        var checkoutUrl = await orderBusiness.ResumeCheckoutAsync(id, User.GetUserId(), User.GetUserName());
        return Ok(new { CheckoutUrl = checkoutUrl });
    }

    private async Task<IActionResult> BuildOrderResponseAsync(int id)
    {
        var order = await orderBusiness.GetByIdForStudentAsync(id, User.GetUserId());
        if (order is null) return NotFound();

        var items = await orderBusiness.GetItemsByOrderIdAsync(id);
        return Ok(new { order.OrderId, order.Date, order.Total, order.SerialNumber, order.Status, order.CreatedAt, order.UpdatedAt, Items = items });
    }
}

public record CreateStudentOrderRequest([Required, MinLength(1)] List<CreateOrderItemRequest> Items, bool UseSavedCard = false);
