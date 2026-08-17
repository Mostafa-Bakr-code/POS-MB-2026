using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using POS_MB.Business;
using POS_MB.Business.Payments;
using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// A student stuck with an order at AwaitingPayment (backed out of the
// payment screen, app crashed mid-checkout) needs a way forward besides
// waiting out the auto-cancel timeout - see clsOrderBusiness.ResumeCheckoutAsync
// and the CancelForStudentAsync extension to allow AwaitingPayment too.
public class PaymobResumeAndCancelTests : DatabaseTestBase
{
    // Never touches the real network - resuming checkout is real money, not
    // something to risk on a test typo. Lets tests control both what the
    // "did this already succeed?" inquiry reports and what a fresh checkout
    // attempt would return.
    private class FakePaymobClient(PaymobInquiryResult inquiryResult) : PaymobClient(new HttpClient(), new PaymobOptions())
    {
        public int InquiryCallCount { get; private set; }
        public string? LastInquiredReference { get; private set; }
        public int CreateIntentionCallCount { get; private set; }

        public override Task<PaymobInquiryResult> InquireByMerchantOrderIdAsync(string merchantOrderId)
        {
            InquiryCallCount++;
            LastInquiredReference = merchantOrderId;
            return Task.FromResult(inquiryResult);
        }

        public override Task<PaymobIntentionResult> CreateIntentionAsync(
            decimal amountEgp, string specialReference, string customerEmail, int expirationSeconds,
            IReadOnlyList<PaymobItemLine>? items = null, string? savedCardToken = null)
        {
            CreateIntentionCallCount++;
            return Task.FromResult(new PaymobIntentionResult("fake_client_secret", 123456789));
        }
    }

    private clsOrderBusiness CreateOrderBusinessWith(FakePaymobClient fakeClient) =>
        new(new POS_MB.DataAccess.clsOrderDataAccess(ConnectionFactory), SettingsBusiness, fakeClient, NullLogger<clsOrderBusiness>.Instance, StudentBusiness);

    private async Task<(int OrderId, int StudentId)> CreateAwaitingPaymentOrderAsync()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.AwaitingPayment);
        return (orderId, studentId);
    }

    [Fact]
    public async Task CancelForStudent_Succeeds_WhileAwaitingPayment()
    {
        var (orderId, studentId) = await CreateAwaitingPaymentOrderAsync();

        var cancelled = await OrderBusiness.CancelForStudentAsync(orderId, studentId);

        Assert.True(cancelled);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
    }

    [Fact]
    public async Task ResumeCheckout_Throws_WhenOrderIsNotAwaitingPayment()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        // Still Placed (default) - never touched AwaitingPayment at all.

        await Assert.ThrowsAsync<ArgumentException>(() =>
            OrderBusiness.ResumeCheckoutAsync(orderId, studentId, "student@example.com"));
    }

    [Fact]
    public async Task ResumeCheckout_Throws_ForAnotherStudentsOrder()
    {
        var (orderId, _) = await CreateAwaitingPaymentOrderAsync();
        var otherStudentId = await CreateStudentAsync();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            OrderBusiness.ResumeCheckoutAsync(orderId, otherStudentId, "other@example.com"));
    }

    [Fact]
    public async Task ResumeCheckout_SkipsInquiry_WhenNoPriorReferenceExists()
    {
        // Created directly via CreateOrderAsync, never through
        // CreateStudentOrderAsync/StartPaymobCheckoutAsync - no checkout
        // attempt has ever actually been sent to Paymob yet, so there's
        // nothing to ask about.
        var (orderId, studentId) = await CreateAwaitingPaymentOrderAsync();
        var fake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var orderBusiness = CreateOrderBusinessWith(fake);

        var checkoutUrl = await orderBusiness.ResumeCheckoutAsync(orderId, studentId, "student@example.com");

        Assert.NotNull(checkoutUrl);
        Assert.Equal(0, fake.InquiryCallCount);
        Assert.Equal(1, fake.CreateIntentionCallCount);
    }

    [Fact]
    public async Task ResumeCheckout_DetectsAnAlreadySucceededPayment_ReconcilesInsteadOfChargingAgain()
    {
        // The actual race this closes: a first payment succeeds on
        // Paymob's side, but our own database hasn't heard about it yet
        // (the webhook hasn't arrived) when the student taps "Continue to
        // Payment" again.
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();

        // Establish a real LastPaymobReference the way CreateStudentOrderAsync
        // normally would, via a fake client standing in for the initial checkout.
        var setupFake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var (orderId, _) = await CreateOrderBusinessWith(setupFake).CreateStudentOrderAsync(studentId, "student@example.com", [new NewOrderItem(itemId, 1, null)]);

        // Now the student taps "Continue to Payment" - Paymob reports the
        // original attempt actually already succeeded.
        var resumeFake = new FakePaymobClient(new PaymobInquiryResult(true, true, 888999, 100m));
        var orderBusiness = CreateOrderBusinessWith(resumeFake);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            orderBusiness.ResumeCheckoutAsync(orderId, studentId, "student@example.com"));

        Assert.Equal(0, resumeFake.CreateIntentionCallCount); // never opened a second payment window
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
        Assert.Equal(888999, order.PaymobTransactionId);
    }

    [Fact]
    public async Task ResumeCheckout_ProceedsNormally_WhenNothingWasFound()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();

        var setupFake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var (orderId, _) = await CreateOrderBusinessWith(setupFake).CreateStudentOrderAsync(studentId, "student@example.com", [new NewOrderItem(itemId, 1, null)]);

        // Genuinely never completed - the normal, expected resume case.
        var resumeFake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var orderBusiness = CreateOrderBusinessWith(resumeFake);

        var checkoutUrl = await orderBusiness.ResumeCheckoutAsync(orderId, studentId, "student@example.com");

        Assert.NotNull(checkoutUrl);
        Assert.Equal(1, resumeFake.InquiryCallCount);
        Assert.Equal(1, resumeFake.CreateIntentionCallCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.AwaitingPayment, order!.Status); // untouched
    }

    // CancelAbandonedPaymentsAsync's own reconciliation check - added after a
    // real incident: a webhook never arrived (the delivery path was down),
    // the sweep would otherwise have cancelled a genuinely paid order,
    // taking the student's money with no record of it. See
    // clsOrderBusiness.WasActuallyPaidAsync.
    private async Task<int> CreateStaleAwaitingPaymentOrderAsync(string? lastPaymobReference)
    {
        var (orderId, _) = await CreateAwaitingPaymentOrderAsync();
        await SetOrderDateAsync(orderId, DateTime.UtcNow.AddMinutes(-15)); // past the 600s expiration window
        if (lastPaymobReference is not null)
        {
            using var connection = ConnectionFactory.CreateConnection();
            await connection.ExecuteAsync("UPDATE Orders SET LastPaymobReference = @Ref WHERE OrderId = @OrderId",
                new { Ref = lastPaymobReference, OrderId = orderId });
        }
        return orderId;
    }

    [Fact]
    public async Task CancelAbandonedPayments_ReconcilesAsPaid_InsteadOfCancelling_WhenPaymobConfirmsSuccess()
    {
        var orderId = await CreateStaleAwaitingPaymentOrderAsync("20260101-1");
        var fake = new FakePaymobClient(new PaymobInquiryResult(true, true, 777888, 100m));
        var orderBusiness = CreateOrderBusinessWith(fake);

        var cancelledCount = await orderBusiness.CancelAbandonedPaymentsAsync();

        Assert.Equal(0, cancelledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
        Assert.Equal(777888, order.PaymobTransactionId);
    }

    [Fact]
    public async Task CancelAbandonedPayments_CancelsNormally_WhenPaymobConfirmsNothingWasPaid()
    {
        var orderId = await CreateStaleAwaitingPaymentOrderAsync("20260101-2");
        var fake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var orderBusiness = CreateOrderBusinessWith(fake);

        var cancelledCount = await orderBusiness.CancelAbandonedPaymentsAsync();

        Assert.Equal(1, cancelledCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
    }

    [Fact]
    public async Task CancelAbandonedPayments_CancelsNormally_WhenNoCheckoutWasEverStarted()
    {
        // No LastPaymobReference at all - nothing to ask Paymob about,
        // should behave exactly as before this change.
        var orderId = await CreateStaleAwaitingPaymentOrderAsync(lastPaymobReference: null);
        var fake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var orderBusiness = CreateOrderBusinessWith(fake);

        var cancelledCount = await orderBusiness.CancelAbandonedPaymentsAsync();

        Assert.Equal(1, cancelledCount);
        Assert.Equal(0, fake.InquiryCallCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
    }

    [Fact]
    public async Task GetByDateAndSerialNumber_ResolvesTheSameOrder()
    {
        var (orderId, _) = await CreateAwaitingPaymentOrderAsync();
        var order = await OrderBusiness.GetByIdAsync(orderId);

        var resolved = await OrderBusiness.GetByDateAndSerialNumberAsync(order!.Date, order.SerialNumber!.Value);

        Assert.NotNull(resolved);
        Assert.Equal(orderId, resolved!.OrderId);
    }
}
