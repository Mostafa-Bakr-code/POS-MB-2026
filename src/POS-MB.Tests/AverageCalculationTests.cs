using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

public class AverageCalculationTests : DatabaseTestBase
{
    [Fact]
    public async Task GetSalesSummary_WithNoOrdersAtAll_ReturnsZeroAverageInsteadOfDividingByZero()
    {
        var summary = await ReportingBusiness.GetSalesSummaryAsync();

        Assert.Equal(0, summary.TotalOrders);
        Assert.Equal(0m, summary.AverageOrderValue);
    }

    [Fact]
    public async Task GetSalesSummary_AverageOrderValue_IncludesComplimentaryValueInTheNumerator()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]); // paid 100
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, true, [new NewOrderItem(itemId, 1, null)]);  // complimentary 100

        var summary = await ReportingBusiness.GetSalesSummaryAsync();

        // (100 paid + 100 complimentary) / 2 orders = 100 - a free order still
        // represents real food given out, not "zero" activity.
        Assert.Equal(100m, summary.AverageOrderValue);
    }

    [Fact]
    public async Task GetItemSales_AveragePrice_ReturnsZero_ForAnItemNeverSold()
    {
        var categoryId = await CreateCategoryAsync();
        await CreateItemAsync(categoryId, "Never Sold Item", price: 50m);

        var rows = await ReportingBusiness.GetItemSalesAsync();

        Assert.Empty(rows); // never sold - correctly absent, not a zero-filled row
    }
}
