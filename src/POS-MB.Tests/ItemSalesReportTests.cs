using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

public class ItemSalesReportTests : DatabaseTestBase
{
    [Fact]
    public async Task GetItemSales_AggregatesQuantityAndRevenue_AcrossMultipleOrders()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 114m, taxRate: 14m);
        var userId = await CreateUserAsync();

        // Two separate orders for the same item - the report has to add them
        // together, not just report whichever order it saw last.
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(itemId, 1, null)]);

        var rows = (await ReportingBusiness.GetItemSalesAsync()).ToList();
        var row = Assert.Single(rows);

        Assert.Equal(2, row.Quantity);
        Assert.Equal(228m, row.Revenue);
        Assert.Equal(28m, row.Tax);
        Assert.Equal(200m, row.RevenueExcludingTax);
    }

    // Regression test for a real bug found via manual testing: two different Items
    // that happen to share a name (differing only by case - SQL Server's default
    // collation treats "Shawerma" and "shawerma" as equal) were being merged into
    // one report row with a meaningless averaged price, because the query grouped
    // by name instead of the item's actual ItemId. Fixed by grouping by ItemId.
    [Fact]
    public async Task GetItemSales_KeepsDifferentItemsSeparate_EvenWhenNamesCollideCaseInsensitively()
    {
        var categoryId = await CreateCategoryAsync();
        var cheapItemId = await CreateItemAsync(categoryId, "Shawerma", price: 45m, taxRate: 14m);
        var expensiveItemId = await CreateItemAsync(categoryId, "shawerma", price: 80m, taxRate: 14m);
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(cheapItemId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(expensiveItemId, 1, null)]);

        var rows = (await ReportingBusiness.GetItemSalesAsync())
            .Where(r => r.ItemName.Equals("shawerma", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Equal(2, rows.Count); // must NOT be merged into a single averaged row
        Assert.Contains(rows, r => r.Quantity == 1 && r.Revenue == 45m);
        Assert.Contains(rows, r => r.Quantity == 1 && r.Revenue == 80m);
    }
}
