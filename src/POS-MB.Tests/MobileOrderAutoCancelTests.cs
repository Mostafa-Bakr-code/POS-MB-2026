using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// The safety net that catches whatever slips past the manual toggle and
// heartbeat check: a mobile order that's sat at Placed too long gets
// auto-cancelled, regardless of why (see clsOrderBusiness.CancelStaleMobileOrdersAsync).
public class MobileOrderAutoCancelTests : DatabaseTestBase
{
    [Fact]
    public async Task CancelStaleMobileOrders_CancelsOrder_PastTheConfiguredTimeout()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        await SettingsBusiness.SetAsync("MobileOrderAutoCancelMinutes", "10");

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        // Backdating UpdatedAt (when it actually became Placed), not Date
        // (when checkout started) - see clsOrderDataAccess.GetStaleMobileOrdersAsync.
        await SetOrderUpdatedAtAsync(orderId, DateTime.UtcNow.AddMinutes(-15)); // 15 > 10-minute timeout

        var cancelledCount = await OrderBusiness.CancelStaleMobileOrdersAsync();

        Assert.Equal(1, cancelledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
    }

    [Fact]
    public async Task CancelStaleMobileOrders_LeavesOrderAlone_WhenOnlyCreationTimeIsStale()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        await SettingsBusiness.SetAsync("MobileOrderAutoCancelMinutes", "10");

        // Simulates a slow Paymob checkout: the order was created 15 minutes
        // ago (Date), but only just became Placed (UpdatedAt = now, from
        // CreateOrderAsync's own default). The timeout must not fire here -
        // the kitchen has only had this order for a moment, regardless of
        // how long checkout took before that.
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await SetOrderDateAsync(orderId, DateTime.UtcNow.AddMinutes(-15));

        var cancelledCount = await OrderBusiness.CancelStaleMobileOrdersAsync();

        Assert.Equal(0, cancelledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
    }

    [Fact]
    public async Task CancelStaleMobileOrders_LeavesRecentOrderAlone()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        await SettingsBusiness.SetAsync("MobileOrderAutoCancelMinutes", "10");

        // No backdating - just placed, well within the 10-minute timeout.
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);

        var cancelledCount = await OrderBusiness.CancelStaleMobileOrdersAsync();

        Assert.Equal(0, cancelledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
    }

    [Fact]
    public async Task CancelStaleMobileOrders_UsesDefaultTenMinutes_WhenNeverConfigured()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        // No SetAsync call for the timeout at all - must fall back to 10 minutes.

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await SetOrderUpdatedAtAsync(orderId, DateTime.UtcNow.AddMinutes(-11));

        var cancelledCount = await OrderBusiness.CancelStaleMobileOrdersAsync();

        Assert.Equal(1, cancelledCount);
    }

    [Fact]
    public async Task CancelStaleMobileOrders_LeavesOrderAlone_OnceAlreadyAccepted()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        await SettingsBusiness.SetAsync("MobileOrderAutoCancelMinutes", "10");

        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await SetOrderDateAsync(orderId, DateTime.UtcNow.AddMinutes(-15));
        // The kitchen already accepted it - stale-by-time no longer applies,
        // it's actively being worked on, not stuck.
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.Preparing);

        var cancelledCount = await OrderBusiness.CancelStaleMobileOrdersAsync();

        Assert.Equal(0, cancelledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Preparing, order!.Status);
    }

    [Fact]
    public async Task CancelStaleMobileOrders_NeverTouchesCashierOrders()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();
        await SettingsBusiness.SetAsync("MobileOrderAutoCancelMinutes", "10");

        // Cashier orders start at Completed already (see OrderInitialStatusTests),
        // which alone would exclude it here - force it back to Placed so this
        // test actually proves the OrderSource filter matters, not just that
        // Status already happened to rule it out.
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.Placed);
        await SetOrderUpdatedAtAsync(orderId, DateTime.UtcNow.AddMinutes(-30));

        var cancelledCount = await OrderBusiness.CancelStaleMobileOrdersAsync();

        Assert.Equal(0, cancelledCount);
    }
}
