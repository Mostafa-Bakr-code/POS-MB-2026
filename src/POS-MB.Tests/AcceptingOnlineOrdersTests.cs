using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// Staff can pause new mobile orders any time via the "Accepting Online Orders"
// toggle on the WinForms Order Status screen - enforced here, not just hidden
// on the mobile menu, since a student's app could already have a cart built
// from before the toggle flipped.
public class AcceptingOnlineOrdersTests : DatabaseTestBase
{
    [Fact]
    public async Task CreateOrder_Throws_ForMobileOrder_WhenNotAccepting()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        await SettingsBusiness.SetAsync("AcceptingOnlineOrders", "false");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]));
    }

    [Fact]
    public async Task CreateOrder_Allows_MobileOrder_WhenAccepting()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        await SettingsBusiness.SetAsync("AcceptingOnlineOrders", "true");

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);

        Assert.True(orderId > 0);
    }

    [Fact]
    public async Task CreateOrder_Allows_MobileOrder_WhenSettingWasNeverSet()
    {
        // No SetAsync call at all - a fresh install/test DB with the key never
        // touched must default to accepting, not silently block every mobile
        // order until someone remembers to seed the setting.
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);

        Assert.True(orderId > 0);
    }

    [Fact]
    public async Task CreateOrder_UnaffectedForCashierOrders_WhenNotAccepting()
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
}
