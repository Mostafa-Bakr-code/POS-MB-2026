using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// Staff can pause new mobile orders any time via the "Accepting Online Orders"
// toggle on the WinForms Order Status screen, and mobile orders also require a
// recent "shop heartbeat" (sent every ~15s by that same screen while it's
// open) - both enforced here, not just hidden on the mobile menu, since a
// student's app could already have a cart built from before either changed.
// DatabaseTestBase records a fresh heartbeat for every test by default (see
// its constructor), so tests here that care about heartbeat staleness/absence
// override that deliberately.
public class AcceptingOnlineOrdersTests : DatabaseTestBase
{
    [Fact]
    public async Task CreateOrder_Throws_ForMobileOrder_WhenManuallyPaused()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        await SettingsBusiness.SetAsync("AcceptingOnlineOrders", "false");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]));
    }

    [Fact]
    public async Task CreateOrder_Allows_MobileOrder_WhenAcceptingAndHeartbeatFresh()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        await SettingsBusiness.SetAsync("AcceptingOnlineOrders", "true");

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);

        Assert.True(orderId > 0);
    }

    [Fact]
    public async Task CreateOrder_Allows_MobileOrder_WhenToggleWasNeverSet()
    {
        // No SetAsync call for the toggle at all - a fresh install/test DB
        // with that key never touched must default to accepting (only the
        // heartbeat, recorded automatically by DatabaseTestBase, is what's
        // actually in play here).
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);

        Assert.True(orderId > 0);
    }

    [Fact]
    public async Task CreateOrder_UnaffectedForCashierOrders_WhenManuallyPaused()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();
        await SettingsBusiness.SetAsync("AcceptingOnlineOrders", "false");

        // The toggle is specifically about online (mobile) orders - a cashier
        // taking an order in person isn't affected by it at all.
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);

        Assert.True(orderId > 0);
    }

    [Fact]
    public async Task CreateOrder_Throws_ForMobileOrder_WhenHeartbeatIsStale()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();

        // Overwrites the fresh heartbeat DatabaseTestBase just recorded -
        // simulates nobody having had the Order Status screen open recently
        // enough to trust that a new order would actually be noticed.
        await SettingsBusiness.SetAsync("ShopHeartbeatUtc", DateTime.UtcNow.AddMinutes(-10).ToString("O"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]));
        Assert.Contains("temporarily unable", ex.Message);
    }

    [Fact]
    public async Task CreateOrder_Throws_ForMobileOrder_WhenHeartbeatWasNeverRecorded()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();

        // Simulates a genuinely fresh install/day 1 - nobody has ever opened
        // the Order Status screen, so the system has no basis to believe a
        // mobile order would be seen at all.
        await SettingsBusiness.DeleteAsync("ShopHeartbeatUtc");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]));
    }

    [Fact]
    public async Task CreateOrder_UnaffectedForCashierOrders_WhenHeartbeatIsStale()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();
        await SettingsBusiness.SetAsync("ShopHeartbeatUtc", DateTime.UtcNow.AddMinutes(-10).ToString("O"));

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);

        Assert.True(orderId > 0);
    }
}
