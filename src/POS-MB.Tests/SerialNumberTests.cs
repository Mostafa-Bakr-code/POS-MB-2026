using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// SerialNumber resets daily - but "daily" must mean the shop's actual local
// day, not the UTC day Orders.Date is stored in. Found live: an order
// placed a couple hours after local midnight was still "yesterday" in UTC
// terms, so it kept the previous local day's numbering sequence, while
// later orders that same local day started back at #1 - see
// clsOrderDataAccess.CreateOrderAsync.
public class SerialNumberTests : DatabaseTestBase
{
    [Fact]
    public async Task SerialNumber_ContinuesTheSameLocalDay_EvenAcrossAUtcDayBoundary()
    {
        const double offsetHours = 3.0;
        await SettingsBusiness.SetAsync("TimeZoneOffsetHours", offsetHours.ToString());

        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();

        var firstOrderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);
        var firstOrder = await OrderBusiness.GetByIdAsync(firstOrderId);

        // Backdated to the UTC instant of *today's local midnight* -
        // guaranteed to fall on today's local calendar day (by
        // construction, regardless of what the real current UTC time
        // happens to be right now), while its own UTC calendar day is
        // typically the day before. This is exactly the scenario found
        // live: an order placed in the first few hours after local
        // midnight is already "yesterday" in UTC terms.
        var todaysLocalMidnightUtc = DateTime.UtcNow.AddHours(offsetHours).Date.AddHours(-offsetHours);
        await SetOrderDateAsync(firstOrderId, todaysLocalMidnightUtc.AddMinutes(5));

        var secondOrderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);
        var secondOrder = await OrderBusiness.GetByIdAsync(secondOrderId);

        // Same local day as the first order - must continue the sequence,
        // not reset to 1, even though the two orders' stored (UTC) Date
        // values can fall on different calendar days.
        Assert.Equal(firstOrder!.SerialNumber + 1, secondOrder!.SerialNumber);
    }

    [Fact]
    public async Task SerialNumber_StillResetsOnAGenuinelyDifferentLocalDay()
    {
        await SettingsBusiness.SetAsync("TimeZoneOffsetHours", "3");

        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();

        var firstOrderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);
        // Genuinely a different local day (not just a different UTC day) -
        // a full day earlier, well outside any timezone-shift ambiguity.
        await SetOrderDateAsync(firstOrderId, DateTime.UtcNow.AddDays(-1));

        var secondOrderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);
        var secondOrder = await OrderBusiness.GetByIdAsync(secondOrderId);

        Assert.Equal(1, secondOrder!.SerialNumber);
    }
}
