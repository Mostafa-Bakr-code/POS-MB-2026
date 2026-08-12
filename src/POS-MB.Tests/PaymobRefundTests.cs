using Microsoft.Extensions.Logging.Abstractions;
using POS_MB.Business;
using POS_MB.Business.Payments;
using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// A cancelled order that was already paid through Paymob should be
// automatically refunded - see clsOrderBusiness.RefundIfPaidAsync. Uses a
// fake PaymobClient (never touches the real network - refunding is real
// money, not something to risk on a test typo) to verify the refund is
// actually attempted with the right transaction id/amount, not just that
// the guard clauses correctly skip it.
public class PaymobRefundTests : DatabaseTestBase
{
    // records what it was called with instead of ever making an HTTP call.
    private class FakePaymobClient(bool refundSucceeds, long refundTransactionId = 999000)
        : PaymobClient(new HttpClient(), new PaymobOptions())
    {
        public int CallCount { get; private set; }
        public long? LastTransactionId { get; private set; }
        public decimal? LastAmount { get; private set; }

        public override Task<PaymobRefundResult> RefundAsync(long transactionId, decimal amountEgp)
        {
            CallCount++;
            LastTransactionId = transactionId;
            LastAmount = amountEgp;
            return Task.FromResult(new PaymobRefundResult(refundSucceeds, refundSucceeds ? refundTransactionId : null));
        }
    }

    private async Task<int> CreatePaidOrderAsync(decimal price, long transactionId)
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        await OrderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: price, transactionId);
        return orderId;
    }

    private clsOrderBusiness CreateOrderBusinessWith(FakePaymobClient fakeClient) =>
        new(new POS_MB.DataAccess.clsOrderDataAccess(ConnectionFactory), SettingsBusiness, fakeClient, NullLogger<clsOrderBusiness>.Instance);

    [Fact]
    public async Task Cancel_RefundsAPaidOrder_ForItsFullTotal()
    {
        var orderId = await CreatePaidOrderAsync(100m, transactionId: 777888);
        var fake = new FakePaymobClient(refundSucceeds: true, refundTransactionId: 999222);
        var orderBusiness = CreateOrderBusinessWith(fake);

        await orderBusiness.CancelAsync(orderId);

        Assert.Equal(1, fake.CallCount);
        Assert.Equal(777888, fake.LastTransactionId);
        Assert.Equal(100m, fake.LastAmount);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
        Assert.NotNull(order.RefundedAt);
        // The refund's own transaction id, deliberately different from
        // PaymobTransactionId (the original charge) - Paymob creates a
        // separate record for the refund itself.
        Assert.Equal(999222, order.RefundTransactionId);
    }

    [Fact]
    public async Task Cancel_LeavesRefundedAtNull_WhenPaymobRejectsTheRefund()
    {
        var orderId = await CreatePaidOrderAsync(100m, transactionId: 777888);
        var fake = new FakePaymobClient(refundSucceeds: false);
        var orderBusiness = CreateOrderBusinessWith(fake);

        // Must not throw - the cancellation itself still has to succeed even
        // when the refund attempt fails; see RefundIfPaidAsync.
        var cancelled = await orderBusiness.CancelAsync(orderId);

        Assert.True(cancelled);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
        Assert.Null(order.RefundedAt);
    }

    [Fact]
    public async Task Cancel_NeverAttemptsARefund_ForAnOrderNeverPaidThroughPaymob()
    {
        // A plain Placed order created directly (never went through
        // AwaitingPayment/checkout at all) has no PaymobTransactionId.
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        var fake = new FakePaymobClient(refundSucceeds: true);
        var orderBusiness = CreateOrderBusinessWith(fake);

        await orderBusiness.CancelAsync(orderId);

        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task Cancel_NeverRefundsTwice_ForAnOrderAlreadyRefunded()
    {
        var orderId = await CreatePaidOrderAsync(100m, transactionId: 777888);
        var firstFake = new FakePaymobClient(refundSucceeds: true);
        await CreateOrderBusinessWith(firstFake).CancelAsync(orderId);
        Assert.Equal(1, firstFake.CallCount);

        // A second cancel attempt on the same already-cancelled, already-
        // refunded order (e.g. a retried request) must not refund again.
        var secondFake = new FakePaymobClient(refundSucceeds: true);
        await CreateOrderBusinessWith(secondFake).CancelAsync(orderId);

        Assert.Equal(0, secondFake.CallCount);
    }

    [Fact]
    public async Task StudentCancel_RefundsAPaidOrder_TheSameWayStaffCancelDoes()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        await OrderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m, transactionId: 777888);

        var fake = new FakePaymobClient(refundSucceeds: true);
        var orderBusiness = CreateOrderBusinessWith(fake);

        await orderBusiness.CancelForStudentAsync(orderId, studentId);

        Assert.Equal(1, fake.CallCount);
        Assert.Equal(777888, fake.LastTransactionId);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.NotNull(order!.RefundedAt);
    }
}
