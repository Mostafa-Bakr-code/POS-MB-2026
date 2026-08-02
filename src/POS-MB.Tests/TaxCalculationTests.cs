using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// The old app hardcoded a blanket 14% tax over the whole order total, regardless
// of each item's actual tax rate. These prove tax is always computed per line
// from that line's own snapshotted rate, not one rate applied to everything.
public class TaxCalculationTests : DatabaseTestBase
{
    [Fact]
    public async Task GetSalesSummary_UsesEachItemsOwnTaxRate_NotOneBlanketRateForTheWholeOrder()
    {
        var categoryId = await CreateCategoryAsync();
        // 114 @ 14% -> exactly 14 tax. 110 @ 10% -> exactly 10 tax. Totals: 100 tax
        // over 224 revenue would be the wrong "blanket rate" answer; 24 is correct.
        var itemA = await CreateItemAsync(categoryId, "Item A", price: 114m, taxRate: 14m);
        var itemB = await CreateItemAsync(categoryId, "Item B", price: 110m, taxRate: 10m);
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false,
            [new NewOrderItem(itemA, 1, null), new NewOrderItem(itemB, 1, null)]);

        var summary = await ReportingBusiness.GetSalesSummaryAsync();

        Assert.Equal(224m, summary.TotalRevenue);
        Assert.Equal(24m, summary.TotalTax);
    }

    [Fact]
    public async Task GetItemSales_ComplimentaryOrder_CountsQuantityButZeroesRevenueAndTax()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 114m, taxRate: 14m);
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, isComplimentary: true,
            [new NewOrderItem(itemId, 2, null)]);

        var row = Assert.Single(await ReportingBusiness.GetItemSalesAsync());

        Assert.Equal(2, row.Quantity);       // still counted for stock/popularity purposes
        Assert.Equal(0m, row.Revenue);       // but not counted as paid revenue
        Assert.Equal(0m, row.Tax);
        Assert.Equal(228m, row.ComplimentaryValue); // tracked separately instead
    }

    [Fact]
    public async Task ItemPriceChange_DoesNotRetroactivelyAffectPastOrdersTaxRate()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 114m, taxRate: 14m);
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(itemId, 1, null)]);

        // Tax rate changes after the sale - the historical order's tax must not change.
        await ItemBusiness.UpdateAsync(itemId, "Item", categoryId, price: 114m, taxRate: 20m);

        var summary = await ReportingBusiness.GetSalesSummaryAsync();

        Assert.Equal(14m, summary.TotalTax); // still the original 14%, not the new 20%
    }
}
