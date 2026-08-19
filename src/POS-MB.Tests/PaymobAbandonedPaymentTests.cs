using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// A different safety net from MobileOrderAutoCancelTests (which covers
// stale Placed orders) - this one is for a checkout that never finished at
// all: the student closed the app mid-payment, the WebView crashed, or they
// just gave up. See clsOrderBusiness.CancelAbandonedPaymentsAsync.
public class PaymobAbandonedPaymentTests : DatabaseTestBase
{
    private async Task<int> CreateAwaitingPaymentOrderAsync()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        return orderId;
    }

    [Fact]
    public async Task CancelAbandonedPayments_CancelsOrder_PastPaymentExpiration()
    {
        var orderId = await CreateAwaitingPaymentOrderAsync();
        // PaymentExpirationSeconds is 600 (10 minutes) - well past that.
        await SetOrderDateAsync(orderId, DateTime.UtcNow.AddMinutes(-15));

        var cancelledCount = await OrderBusiness.CancelAbandonedPaymentsAsync();

        Assert.Equal(1, cancelledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
        // Distinguishes this from CancelStaleMobileOrdersAsync's own reason -
        // "never paid at all" vs "kitchen never noticed a paid order".
        Assert.Equal("Auto (payment abandoned)", order.CancelledBy);
    }

    [Fact]
    public async Task CancelAbandonedPayments_LeavesRecentCheckoutAlone()
    {
        var orderId = await CreateAwaitingPaymentOrderAsync();
        // No backdating - still well within the payment session window.

        var cancelledCount = await OrderBusiness.CancelAbandonedPaymentsAsync();

        Assert.Equal(0, cancelledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.AwaitingPayment, order!.Status);
    }

    [Fact]
    public async Task CancelAbandonedPayments_LeavesOrderAlone_OncePaymentSucceeded()
    {
        var orderId = await CreateAwaitingPaymentOrderAsync();
        await SetOrderDateAsync(orderId, DateTime.UtcNow.AddMinutes(-15));
        // The webhook already confirmed payment and moved it to Placed -
        // must not be swept up here just because the order is old.
        await OrderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m, transactionId: 555111);

        var cancelledCount = await OrderBusiness.CancelAbandonedPaymentsAsync();

        Assert.Equal(0, cancelledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
    }

    [Fact]
    public async Task CancelAbandonedPayments_NeverTouchesStalePlacedOrders()
    {
        // Confirms the two sweeps (stale-Placed vs stale-AwaitingPayment) are
        // properly isolated by status - an old Placed order (the OTHER
        // safety net's job) must not be cancelled by this one too.
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await SetOrderDateAsync(orderId, DateTime.UtcNow.AddMinutes(-15));

        var cancelledCount = await OrderBusiness.CancelAbandonedPaymentsAsync();

        Assert.Equal(0, cancelledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
    }
}
