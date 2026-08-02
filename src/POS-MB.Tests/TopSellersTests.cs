using POS_MB.DataAccess.Models;
using POS_MB.DataAccess.Models.Reports;

namespace POS_MB.Tests;

public class TopSellersTests : DatabaseTestBase
{
    [Fact]
    public async Task GetTopSellers_SortsByQuantity_HighestFirst()
    {
        var categoryId = await CreateCategoryAsync();
        var lowQtyHighPriceId = await CreateItemAsync(categoryId, "Low Qty High Price", price: 500m);
        var highQtyLowPriceId = await CreateItemAsync(categoryId, "High Qty Low Price", price: 10m);
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(lowQtyHighPriceId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(highQtyLowPriceId, 5, null)]);

        var rows = (await ReportingBusiness.GetTopSellersAsync(sortBy: TopSellersSortBy.Quantity)).ToList();

        Assert.Equal("High Qty Low Price", rows[0].ItemName);
        Assert.Equal(5, rows[0].Quantity);
        Assert.Equal("Low Qty High Price", rows[1].ItemName);
    }

    [Fact]
    public async Task GetTopSellers_SortsByRevenue_HighestFirst()
    {
        var categoryId = await CreateCategoryAsync();
        var lowQtyHighPriceId = await CreateItemAsync(categoryId, "Low Qty High Price", price: 500m); // revenue 500
        var highQtyLowPriceId = await CreateItemAsync(categoryId, "High Qty Low Price", price: 10m);  // revenue 50
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(lowQtyHighPriceId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(highQtyLowPriceId, 5, null)]);

        var rows = (await ReportingBusiness.GetTopSellersAsync(sortBy: TopSellersSortBy.Revenue)).ToList();

        Assert.Equal("Low Qty High Price", rows[0].ItemName);
        Assert.Equal(500m, rows[0].Revenue);
    }

    [Fact]
    public async Task GetTopSellers_RespectsTakeLimit()
    {
        var categoryId = await CreateCategoryAsync();
        var userId = await CreateUserAsync();

        for (var i = 0; i < 3; i++)
        {
            var itemId = await CreateItemAsync(categoryId, $"Item {i}", price: 10m);
            await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(itemId, 1, null)]);
        }

        var rows = (await ReportingBusiness.GetTopSellersAsync(take: 2)).ToList();

        Assert.Equal(2, rows.Count);
    }
}
