using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using POS_MB.Business;
using POS_MB.Business.Payments;
using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// A different safety net from MobileOrderAutoCancelTests (which covers
// stale Placed orders) - this one is for a checkout that never finished at
// all: the student closed the app mid-payment, the WebView crashed, or they
// just gave up. See clsOrderBusiness.CancelAbandonedPaymentsAsync.
public class PaymobAbandonedPaymentTests : DatabaseTestBase
{
    // Never touches the real network - see the equivalent fake in
    // PaymobResumeAndCancelTests for the full reasoning.
    private class FakePaymobClient(Func<string, PaymobInquiryResult> inquiryByReference) : PaymobClient(new HttpClient(), new PaymobOptions())
    {
        public override Task<PaymobInquiryResult> InquireByMerchantOrderIdAsync(string merchantOrderId) =>
            Task.FromResult(inquiryByReference(merchantOrderId));

        // A reconciled-as-paid order on this path is immediately eligible
        // for an automatic refund too (see HandleLateOrDuplicatePaymentAsync)
        // - needs its own override, or RefundIfPaidAsync's real network call
        // just throws and gets silently swallowed by its own try/catch.
        public override Task<PaymobRefundResult> RefundAsync(long transactionId, decimal amountEgp) =>
            Task.FromResult(new PaymobRefundResult(true, transactionId + 1000));
    }

    private clsOrderBusiness CreateOrderBusinessWith(PaymobClient fakeClient) =>
        new(new POS_MB.DataAccess.clsOrderDataAccess(ConnectionFactory), SettingsBusiness, fakeClient, NullLogger<clsOrderBusiness>.Instance, StudentBusiness);

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

    // RecheckRecentlyAbandonedPaymentsAsync - the one-time follow-up for an
    // order CancelAbandonedPaymentsAsync already gave up on, in case the
    // payment (e.g. a delayed 3D Secure approval) resolved shortly after.
    // See clsOrderBusiness for the full reasoning on the 15-20 minute window.
    private async Task<int> CreateRecentlyAbandonedOrderAsync(string paymobReference, TimeSpan sinceCancelled)
    {
        var orderId = await CreateAwaitingPaymentOrderAsync();

        using (var connection = ConnectionFactory.CreateConnection())
        {
            await connection.ExecuteAsync("UPDATE Orders SET PaymobReferences = @Ref WHERE OrderId = @OrderId",
                new { Ref = paymobReference, OrderId = orderId });
        }

        await OrderBusiness.CancelAsync(orderId, "Auto (payment abandoned)");
        await SetOrderUpdatedAtAsync(orderId, DateTime.UtcNow - sinceCancelled);
        return orderId;
    }

    [Fact]
    public async Task RecheckRecentlyAbandoned_ReconcilesAsPaid_WithoutResurrectingTheOrder()
    {
        var orderId = await CreateRecentlyAbandonedOrderAsync("20260101-1-120000", TimeSpan.FromMinutes(17));
        var fake = new FakePaymobClient(reference => reference == "20260101-1-120000"
            ? new PaymobInquiryResult(true, true, 777555, 100m)
            : new PaymobInquiryResult(false, false, null, null));
        var orderBusiness = CreateOrderBusinessWith(fake);

        var reconciledCount = await orderBusiness.RecheckRecentlyAbandonedPaymentsAsync();

        Assert.Equal(1, reconciledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        // Recorded and refunded, but NOT resurrected back to Placed - the
        // kitchen already moved on from this order (see
        // HandleLateOrDuplicatePaymentAsync).
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
        Assert.Equal(777555, order.PaymobTransactionId);
        Assert.NotNull(order.RefundedAt);
    }

    [Fact]
    public async Task RecheckRecentlyAbandoned_IgnoresOrders_CancelledTooRecently()
    {
        // Only 5 minutes ago - a checkout attempt made right before
        // cancellation could still legitimately resolve later, well within
        // its own PaymentExpirationSeconds window. Too soon to check yet.
        await CreateRecentlyAbandonedOrderAsync("20260101-2-120000", TimeSpan.FromMinutes(5));
        var fake = new FakePaymobClient(_ => new PaymobInquiryResult(true, true, 777555, 100m));
        var orderBusiness = CreateOrderBusinessWith(fake);

        var reconciledCount = await orderBusiness.RecheckRecentlyAbandonedPaymentsAsync();

        Assert.Equal(0, reconciledCount);
    }

    [Fact]
    public async Task RecheckRecentlyAbandoned_IgnoresOrders_AlreadyPastTheWindow()
    {
        // 30 minutes ago - well past the one-time check window, whether or
        // not it was ever actually re-checked (a prior tick may have
        // already handled it, or it aged out unnoticed - either way, this
        // must not check it again indefinitely).
        await CreateRecentlyAbandonedOrderAsync("20260101-3-120000", TimeSpan.FromMinutes(30));
        var fake = new FakePaymobClient(_ => new PaymobInquiryResult(true, true, 777555, 100m));
        var orderBusiness = CreateOrderBusinessWith(fake);

        var reconciledCount = await orderBusiness.RecheckRecentlyAbandonedPaymentsAsync();

        Assert.Equal(0, reconciledCount);
    }
}
