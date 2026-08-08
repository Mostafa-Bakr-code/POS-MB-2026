using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// A cashier order is paid and handed over at the register in the same moment
// it's created - it starts (and normally stays, barring a cashier-side Cancel)
// at Completed, since there's no separate prep-tracking step visible from that
// side of the counter. A mobile order is the opposite: Placed is the real
// start of a workflow the kitchen needs to work through via the Order Status
// screen.
public class OrderInitialStatusTests : DatabaseTestBase
{
    [Fact]
    public async Task CreateOrder_StartsAtCompleted_ForCashierOrders()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Completed, order!.Status);
    }

    [Fact]
    public async Task CreateOrder_StartsAtPlaced_ForMobileOrders()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
    }
}
