using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// "Break Down by Source" lets you compare how many sales of an item came from the
// register vs the mobile app - mirrors PriceBreakdownTests but grouping on
// Orders.OrderSource instead of OrderItems.Price.
public class SourceBreakdownTests : DatabaseTestBase
{
    [Fact]
    public async Task GetItemSales_GroupedBySource_ShowsCashierAndMobileSeparately()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m, taxRate: 14m);
        var userId = await CreateUserAsync();
        var studentId = await CreateStudentAsync();

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);

        var rows = (await ReportingBusiness.GetItemSalesAsync(groupBySource: true)).ToList();

        Assert.Equal(2, rows.Count); // one row per source, not blended

        var cashierRow = rows.Single(r => r.Source == OrderSource.Cashier);
        var mobileRow = rows.Single(r => r.Source == OrderSource.Mobile);
        Assert.Equal(2, cashierRow.Quantity);
        Assert.Equal(200m, cashierRow.Revenue);
        Assert.Equal(1, mobileRow.Quantity);
        Assert.Equal(100m, mobileRow.Revenue);

        // Grouping by source must not change the overall grand total.
        var summary = await ReportingBusiness.GetSalesSummaryAsync();
        Assert.Equal(300m, summary.TotalRevenue);
    }
}
