using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// Guardrails that stop a malformed order (empty cart, zero/negative quantity,
// a cashier order with nobody attached to it) from ever reaching the database
// and producing a nonsensical total.
public class OrderValidationTests : DatabaseTestBase
{
    [Fact]
    public async Task CreateOrder_Throws_WhenCartIsEmpty()
    {
        var userId = await CreateUserAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, []));
    }

    [Fact]
    public async Task CreateOrder_Throws_WhenQuantityIsZeroOrNegative()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(itemId, 0, null)]));
    }

    [Fact]
    public async Task CreateOrder_Throws_WhenCashierOrderHasNoUser()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            OrderBusiness.CreateOrderAsync(OrderSource.Cashier, null, false, [new NewOrderItem(itemId, 1, null)]));
    }

    [Fact]
    public async Task CreateOrder_Allows_MobileOrderWithNoUser()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, false, [new NewOrderItem(itemId, 1, null)]);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.NotNull(order);
        Assert.Null(order.UserId);
    }
}
