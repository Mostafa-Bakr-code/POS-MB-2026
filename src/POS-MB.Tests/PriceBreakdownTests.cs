using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// "Break Down by Price" lets you compare how an item sold at each price it's ever
// been sold at (e.g. before/after a price change) - it relies entirely on
// OrderItems.Price being a historical snapshot, not a join back to today's catalog
// price. This proves that snapshot survives a price change and reports correctly.
public class PriceBreakdownTests : DatabaseTestBase
{
    [Fact]
    public async Task GetItemSales_GroupedByPrice_ShowsEachHistoricalPriceSeparately()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m, taxRate: 14m);
        var userId = await CreateUserAsync();

        // Two sales at the original price...
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);

        // ...then the price changes, and one more sale happens at the new price.
        await ItemBusiness.UpdateAsync(itemId, "Item", categoryId, price: 150m, taxRate: 14m);
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);

        var rows = (await ReportingBusiness.GetItemSalesAsync(groupByPrice: true)).ToList();

        Assert.Equal(2, rows.Count); // one row per distinct historical price, not blended

        var oldPriceRow = rows.Single(r => r.SoldAtPrice == 100m);
        var newPriceRow = rows.Single(r => r.SoldAtPrice == 150m);
        Assert.Equal(2, oldPriceRow.Quantity);
        Assert.Equal(200m, oldPriceRow.Revenue);
        Assert.Equal(1, newPriceRow.Quantity);
        Assert.Equal(150m, newPriceRow.Revenue);

        // And the un-grouped total must still be the correct grand total across
        // both prices - grouping by price must not change the overall numbers.
        var summary = await ReportingBusiness.GetSalesSummaryAsync();
        Assert.Equal(350m, summary.TotalRevenue);
    }
}
