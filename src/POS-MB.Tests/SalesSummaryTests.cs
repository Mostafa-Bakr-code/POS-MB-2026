using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// Sales Summary is the number a cashier reads out loud at the end of a shift -
// it has to be exactly right, including the less-obvious cases (complimentary,
// cancelled) that the old app got wrong.
public class SalesSummaryTests : DatabaseTestBase
{
    [Fact]
    public async Task GetSalesSummary_ComputesRevenueAndTax_ForNormalOrder()
    {
        var categoryId = await CreateCategoryAsync();
        // Price is tax-inclusive; 114 at 14% splits into exactly 100 excl. tax + 14 tax.
        var itemId = await CreateItemAsync(categoryId, "Item", price: 114m, taxRate: 14m);
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(
            OrderSource.Cashier, userId, null, isComplimentary: false,
            [new NewOrderItem(itemId, 1, null)]);

        var summary = await ReportingBusiness.GetSalesSummaryAsync();

        Assert.Equal(1, summary.TotalOrders);
        Assert.Equal(114m, summary.TotalRevenue);
        Assert.Equal(14m, summary.TotalTax);
        Assert.Equal(100m, summary.RevenueExcludingTax);
        Assert.Equal(0m, summary.ComplimentaryValue);
        Assert.Equal(0, summary.ComplimentaryOrders);
    }

    [Fact]
    public async Task GetSalesSummary_ExcludesComplimentaryFromRevenue_ButTracksItSeparately()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 114m, taxRate: 14m);
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(
            OrderSource.Cashier, userId, null, isComplimentary: false,
            [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(
            OrderSource.Cashier, userId, null, isComplimentary: true,
            [new NewOrderItem(itemId, 1, null)]);

        var summary = await ReportingBusiness.GetSalesSummaryAsync();

        Assert.Equal(2, summary.TotalOrders);
        Assert.Equal(1, summary.ComplimentaryOrders);
        Assert.Equal(114m, summary.TotalRevenue); // only the paid order
        Assert.Equal(114m, summary.ComplimentaryValue); // the free order, tracked separately
    }

    [Fact]
    public async Task GetSalesSummary_ExcludesCancelledOrdersEntirely()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 114m, taxRate: 14m);
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(
            OrderSource.Cashier, userId, null, isComplimentary: false,
            [new NewOrderItem(itemId, 1, null)]);

        var cancelledOrderId = await OrderBusiness.CreateOrderAsync(
            OrderSource.Cashier, userId, null, isComplimentary: false,
            [new NewOrderItem(itemId, 10, null)]); // 1140 - would be obvious if wrongly counted
        await OrderBusiness.CancelAsync(cancelledOrderId);

        var summary = await ReportingBusiness.GetSalesSummaryAsync();

        Assert.Equal(1, summary.TotalOrders);
        Assert.Equal(114m, summary.TotalRevenue);
    }
}
