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
        new(new POS_MB.DataAccess.clsOrderDataAccess(ConnectionFactory), SettingsBusiness, fakeClient, NullLogger<clsOrderBusiness>.Instance, StudentBusiness);

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
    public async Task Cancel_RecordsWhoCancelledIt()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);

        await OrderBusiness.CancelAsync(orderId, "Staff: chef_test");

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal("Staff: chef_test", order!.CancelledBy);
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
    public async Task LateSuccessfulPayment_ForAnAlreadyCancelledOrder_RefundsAutomatically()
    {
        // The race this covers: a student self-cancels (or the abandoned-
        // payment sweep fires) in the same narrow window the payment is
        // actually completing on Paymob's side - the webhook's "succeeded"
        // callback arrives after the order is already Cancelled. See
        // clsOrderBusiness.MarkOrderPaymentResultAsync.
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var fake = new FakePaymobClient(refundSucceeds: true, refundTransactionId: 999333);
        var orderBusiness = CreateOrderBusinessWith(fake);

        var orderId = await orderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await orderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        await orderBusiness.CancelForStudentAsync(orderId, studentId); // cancelled before the webhook arrives

        // The webhook's success callback arrives late.
        await orderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m, transactionId: 777444);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        // Never resurrected back to Placed - the kitchen/system already
        // moved on from this order.
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
        Assert.Equal(777444, order.PaymobTransactionId);
        Assert.NotNull(order.RefundedAt);
        Assert.Equal(999333, order.RefundTransactionId);
        Assert.Equal(1, fake.CallCount);
        Assert.Equal(777444, fake.LastTransactionId);
    }

    // The one deliberate exception to "never resurrect a cancelled order" -
    // found live: Paymob's own failure page has a prominent "Try Again"
    // button, and a student retrying immediately (before the original
    // failure's webhook is even processed) is common, not rare. An order
    // cancelled for an explicit payment failure was never Placed, so
    // there's nothing for a resurrection to surprise - unlike every other
    // cancellation reason, which keeps the old refund-only behavior (see
    // LateSuccessfulPayment_ForAnAlreadyCancelledOrder_RefundsAutomatically
    // above, which must be completely unaffected by this).
    [Fact]
    public async Task LateSuccessfulPayment_AfterAnExplicitPaymentFailure_ResurrectsTheOrderInsteadOfRefunding()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var fake = new FakePaymobClient(refundSucceeds: true);
        var orderBusiness = CreateOrderBusinessWith(fake);

        var orderId = await orderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await orderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);

        // The first attempt's webhook reports failure - cancels the order
        // under its own specific reason.
        await orderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: false, amountEgpPaid: 0m, transactionId: null);
        var cancelled = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal("Auto (payment failed)", cancelled!.CancelledBy);

        // The retry (a genuinely separate transaction) succeeds, and its
        // webhook arrives after the order is already cancelled.
        await orderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m, transactionId: 888222);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
        Assert.Equal(888222, order.PaymobTransactionId);
        Assert.Null(order.CancelledBy); // not cancelled anymore - shouldn't still claim to be
        Assert.Null(order.RefundedAt); // this charge is the order's legitimate payment, not a stray one
        Assert.Equal(0, fake.CallCount); // never refunded
    }

    [Fact]
    public async Task LateSuccessfulPayment_ForAnOrderCancelledByStaff_StillOnlyRefunds_NeverResurrects()
    {
        // Same shape as the payment-failure case above, but cancelled by
        // staff instead - must NOT be resurrected, since staff cancelling
        // an already-Placed-visible order is exactly the "kitchen may have
        // already acted on it" scenario the no-resurrection rule protects.
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var fake = new FakePaymobClient(refundSucceeds: true, refundTransactionId: 555222);
        var orderBusiness = CreateOrderBusinessWith(fake);

        var orderId = await orderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await orderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        await orderBusiness.CancelAsync(orderId, "Staff: chef_test");

        await orderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m, transactionId: 888223);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
        Assert.Equal("Staff: chef_test", order.CancelledBy); // untouched
        Assert.NotNull(order.RefundedAt);
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task LateSuccessfulPayment_RefundsTheActualReportedAmount_NotTheOrderTotal()
    {
        // Paymob rejects a refund request for more than a transaction's own
        // amount ("Requested Refund Amount is greater than the maximum
        // refund amount permissible" - straight from their own "Common
        // errors" docs). order.Total is only ever an expectation for a
        // late-arriving callback, never actually verified against this
        // specific transaction the way the normal AwaitingPayment path
        // does - refunding order.Total instead of what this callback
        // actually reports would risk exactly that failure if the two ever
        // disagreed.
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var fake = new FakePaymobClient(refundSucceeds: true, refundTransactionId: 999333);
        var orderBusiness = CreateOrderBusinessWith(fake);

        var orderId = await orderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await orderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        await orderBusiness.CancelForStudentAsync(orderId, studentId);

        // Reports an amount deliberately different from the order's own
        // Total (100m) - a contrived case for the test, but the amount this
        // callback reports is the only figure that's actually verified
        // truth about what Paymob really charged.
        await orderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 85m, transactionId: 777444);

        Assert.Equal(85m, fake.LastAmount);
    }

    [Fact]
    public async Task LateSuccessfulPayment_IsIdempotent_ARetriedWebhookDeliveryDoesNotRefundTwice()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var fake = new FakePaymobClient(refundSucceeds: true);
        var orderBusiness = CreateOrderBusinessWith(fake);

        var orderId = await orderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await orderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        await orderBusiness.CancelForStudentAsync(orderId, studentId);

        await orderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m, transactionId: 777444);
        // Paymob retries webhook delivery - same callback, second time.
        await orderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m, transactionId: 777444);

        Assert.Equal(1, fake.CallCount); // not refunded twice
    }

    [Fact]
    public async Task LateFailedPayment_ForAnAlreadyCancelledOrder_IsANoOp()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var fake = new FakePaymobClient(refundSucceeds: true);
        var orderBusiness = CreateOrderBusinessWith(fake);

        var orderId = await orderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await orderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        await orderBusiness.CancelForStudentAsync(orderId, studentId);

        // A failed-payment callback landing late has nothing to undo - no
        // money was ever taken.
        await orderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: false, amountEgpPaid: 0m, transactionId: null);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
        Assert.Null(order.PaymobTransactionId);
        Assert.Equal(0, fake.CallCount);
    }

    [Fact]
    public async Task SuccessCallback_ForAnOrderAlreadyFullyProcessed_IsIgnored()
    {
        // A duplicate delivery of a callback for an order that's already
        // Placed (not Cancelled) - the normal idempotency case, distinct
        // from the late-cancel race above.
        var orderId = await CreatePaidOrderAsync(100m, transactionId: 777888);
        var fake = new FakePaymobClient(refundSucceeds: true);
        var orderBusiness = CreateOrderBusinessWith(fake);

        await orderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m, transactionId: 777888);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
        Assert.Equal(777888, order.PaymobTransactionId);
        Assert.Null(order.RefundedAt); // never cancelled, so never refunded
        Assert.Equal(0, fake.CallCount); // no refund was ever attempted
    }

    [Fact]
    public async Task LateDuplicateCharge_ForAnAlreadyPaidOrder_RefundsTheStrayChargeOnly()
    {
        // The double-charge race: a student taps "Continue to Payment"
        // again in the narrow window before the first payment's webhook
        // has arrived (see ResumeCheckoutAsync), then actually pays a
        // second time on the new checkout session. The order was only ever
        // meant to be charged once - the extra charge must be refunded on
        // its own, without disturbing the order's own legitimate
        // transaction/status.
        var orderId = await CreatePaidOrderAsync(100m, transactionId: 777888);
        var fake = new FakePaymobClient(refundSucceeds: true, refundTransactionId: 999555);
        var orderBusiness = CreateOrderBusinessWith(fake);

        // A second, genuinely different transaction id succeeding for the
        // same order.
        await orderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m, transactionId: 555111);

        Assert.Equal(1, fake.CallCount);
        Assert.Equal(555111, fake.LastTransactionId); // refunded the STRAY charge, not the original
        Assert.Equal(100m, fake.LastAmount);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        // The order's own record is untouched - it's still the original,
        // legitimate payment.
        Assert.Equal(OrderStatus.Placed, order!.Status);
        Assert.Equal(777888, order.PaymobTransactionId);
        Assert.Null(order.RefundedAt); // the ORDER was never refunded - only the stray charge was
    }

    [Fact]
    public async Task MarkPaidAsync_DoesNotOverwriteStatus_WhenOrderIsNoLongerAwaitingPayment()
    {
        // Directly exercises the DataAccess-level guard that closes a real
        // database race: clsOrderBusiness reads Status, decides what to do,
        // then writes separately - a concurrent cancel can land in that gap.
        // Without "AND Status = AwaitingPayment" in MarkPaidAsync's own
        // WHERE clause, this write would blindly resurrect a Cancelled
        // order back to Placed.
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        await OrderBusiness.CancelForStudentAsync(orderId, studentId); // simulates the concurrent cancel that "won"

        var dataAccess = new POS_MB.DataAccess.clsOrderDataAccess(ConnectionFactory);
        var wrote = await dataAccess.MarkPaidAsync(orderId, 777888);

        Assert.False(wrote); // the guard correctly refused to write
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status); // untouched
        Assert.Null(order.PaymobTransactionId);
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
