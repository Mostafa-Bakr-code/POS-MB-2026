using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// The most basic money guarantee: does the app charge the right amount for a
// cart, and does that stay true even if the catalog price changes afterward.
public class OrderTotalTests : DatabaseTestBase
{
    [Fact]
    public async Task CreateOrder_ComputesCorrectTotal_ForMultipleItemsAndQuantities()
    {
        var categoryId = await CreateCategoryAsync();
        var itemAId = await CreateItemAsync(categoryId, "Item A", price: 50m);
        var itemBId = await CreateItemAsync(categoryId, "Item B", price: 114m);
        var userId = await CreateUserAsync();

        var orderId = await OrderBusiness.CreateOrderAsync(
            OrderSource.Cashier, userId, isComplimentary: false,
            [
                new NewOrderItem(itemAId, 2, null), // 50 x 2 = 100
                new NewOrderItem(itemBId, 1, null)  // 114 x 1 = 114
            ]);

        var order = await OrderBusiness.GetByIdAsync(orderId);

        Assert.NotNull(order);
        Assert.Equal(214m, order.Total);
    }

    [Fact]
    public async Task OrderItems_SnapshotPriceAtTimeOfSale_UnaffectedByLaterPriceChange()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();

        var orderId = await OrderBusiness.CreateOrderAsync(
            OrderSource.Cashier, userId, isComplimentary: false,
            [new NewOrderItem(itemId, 1, null)]);

        // Price doubles after the sale - the historical order must not change.
        await ItemBusiness.UpdateAsync(itemId, "Item", categoryId, price: 200m);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        var lines = (await OrderBusiness.GetItemsByOrderIdAsync(orderId)).ToList();

        Assert.Equal(100m, order!.Total);
        Assert.Single(lines);
        Assert.Equal(100m, lines[0].Price);
        Assert.Equal(100m, lines[0].TotalItemsPrice);
    }
}
