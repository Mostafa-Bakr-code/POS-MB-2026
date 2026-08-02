using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

public class StaffPerformanceTests : DatabaseTestBase
{
    [Fact]
    public async Task GetStaffPerformance_AttributesRevenueToTheCorrectCashier()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 114m, taxRate: 14m);
        var cashierA = await CreateUserAsync("cashier-a");
        var cashierB = await CreateUserAsync("cashier-b");

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, cashierA, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, cashierA, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, cashierB, false, [new NewOrderItem(itemId, 1, null)]);

        var rows = (await ReportingBusiness.GetStaffPerformanceAsync()).ToList();

        var rowA = rows.Single(r => r.UserId == cashierA);
        var rowB = rows.Single(r => r.UserId == cashierB);

        Assert.Equal(2, rowA.OrderCount);
        Assert.Equal(228m, rowA.Revenue);
        Assert.Equal(28m, rowA.Tax);
        Assert.Equal(1, rowB.OrderCount);
        Assert.Equal(114m, rowB.Revenue);
    }

    [Fact]
    public async Task GetStaffPerformance_ExcludesMobileOrders_SinceTheyHaveNoCashier()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var cashierId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, cashierId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, false, [new NewOrderItem(itemId, 1, null)]);

        var rows = (await ReportingBusiness.GetStaffPerformanceAsync()).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(1, row.OrderCount); // only the cashier order, not the mobile one
    }
}
