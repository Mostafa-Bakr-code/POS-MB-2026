using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// Regression tests for the real bug found via manual testing: orders placed in
// the first few hours after local midnight were stored correctly in UTC but
// silently excluded from "today" filters, because filtering compared the local
// calendar date against the UTC calendar date. Fixed via TimeZoneOffsetHours +
// TimeZoneHelper. These recreate the exact boundary deterministically by
// backdating an order's stored UTC timestamp, instead of depending on what time
// the test happens to run.
public class TimeZoneBoundaryTests : DatabaseTestBase
{
    // Server is UTC+3 (matches the real bug scenario). An order stored as
    // 2026-01-01 22:30 UTC is 2026-01-02 01:30 local - "yesterday" in UTC,
    // "today" locally.
    private static readonly DateTime OrderUtcInstant = new(2026, 1, 1, 22, 30, 0);
    private const string LocalDay = "2026-01-02";
    private const string UtcDay = "2026-01-01";

    private async Task SeedOffsetAndBackdatedOrderAsync()
    {
        await SettingsBusiness.SetAsync("TimeZoneOffsetHours", "3");

        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(itemId, 1, null)]);
        await SetOrderDateAsync(orderId, OrderUtcInstant);
    }

    [Fact]
    public async Task GetAllOrders_FilteringByLocalDay_IncludesOrderStoredAsPreviousUtcDay()
    {
        await SeedOffsetAndBackdatedOrderAsync();

        var orders = (await OrderBusiness.GetAllAsync(DateTime.Parse(LocalDay), DateTime.Parse(LocalDay))).ToList();

        Assert.Single(orders);
    }

    [Fact]
    public async Task GetAllOrders_FilteringByTheUtcDayInstead_ExcludesIt()
    {
        await SeedOffsetAndBackdatedOrderAsync();

        var orders = await OrderBusiness.GetAllAsync(DateTime.Parse(UtcDay), DateTime.Parse(UtcDay));

        Assert.Empty(orders); // the whole point of the bug - UTC day != local day
    }

    [Fact]
    public async Task GetItemSales_GroupedByDay_BucketsUnderTheLocalDayNotTheUtcDay()
    {
        await SeedOffsetAndBackdatedOrderAsync();

        var rows = (await ReportingBusiness.GetItemSalesAsync(groupByDay: true)).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(DateTime.Parse(LocalDay), row.OrderDate);
    }
}
