using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// Covers clsOrderBusiness.MarkOrderPaymentResultAsync - the method the Paymob
// webhook calls after it has already verified the HMAC signature (see
// PaymobHmacTests for that part). This only exercises the DB state
// transitions; it doesn't call Paymob's real API at all.
public class PaymobPaymentResultTests : DatabaseTestBase
{
    private async Task<int> CreateAwaitingPaymentOrderAsync(decimal price)
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        // CreateOrderAsync doesn't produce AwaitingPayment orders yet (that's
        // wired up separately) - forced here to set up the state this method
        // actually needs to be tested against.
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        return orderId;
    }

    [Fact]
    public async Task MarkOrderPaymentResult_MovesToPlaced_WhenSucceededAndAmountMatches()
    {
        var orderId = await CreateAwaitingPaymentOrderAsync(100m);

        await OrderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
    }

    [Fact]
    public async Task MarkOrderPaymentResult_CancelsOrder_WhenPaymentFailed()
    {
        var orderId = await CreateAwaitingPaymentOrderAsync(100m);

        await OrderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: false, amountEgpPaid: 0m);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
    }

    [Fact]
    public async Task MarkOrderPaymentResult_Throws_AndLeavesOrderUntouched_WhenAmountMismatches()
    {
        var orderId = await CreateAwaitingPaymentOrderAsync(100m);

        // Paymob says success and reports a real payment, but for the wrong
        // amount - must not be trusted just because success=true.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            OrderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 50m));

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.AwaitingPayment, order!.Status);
    }

    [Fact]
    public async Task MarkOrderPaymentResult_IsIdempotent_IgnoresACallbackForAnOrderThatAlreadyMovedOn()
    {
        var orderId = await CreateAwaitingPaymentOrderAsync(100m);
        await OrderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m);

        // A duplicate delivery of the same "succeeded" callback (Paymob, like
        // most webhook systems, can retry) must be a harmless no-op, not
        // re-process an order that's already Placed.
        await OrderBusiness.MarkOrderPaymentResultAsync(orderId, paymentSucceeded: true, amountEgpPaid: 100m);

        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
    }

    [Fact]
    public async Task MarkOrderPaymentResult_DoesNothing_ForUnknownOrderId()
    {
        // Should not throw - an unrecognized order id (never happens in
        // practice, but must fail safe) is simply not our problem to act on.
        await OrderBusiness.MarkOrderPaymentResultAsync(999999999, paymentSucceeded: true, amountEgpPaid: 100m);
    }
}
