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
    // inquiryByReference: lets a test give different orders' worth of
    // references different answers (see the multi-attempt history tests
    // below) - falls back to the single fixed inquiryResult when omitted,
    // which is all every other test here needs.
    private class FakePaymobClient(PaymobInquiryResult inquiryResult, Func<string, PaymobInquiryResult>? inquiryByReference = null) : PaymobClient(new HttpClient(), new PaymobOptions())
    {
        public int InquiryCallCount { get; private set; }
        public string? LastInquiredReference { get; private set; }
        public List<string> InquiredReferences { get; } = [];
        public int CreateIntentionCallCount { get; private set; }
        public string? LastSavedCardToken { get; private set; }

        public override Task<PaymobInquiryResult> InquireByMerchantOrderIdAsync(string merchantOrderId)
        {
            InquiryCallCount++;
            LastInquiredReference = merchantOrderId;
            InquiredReferences.Add(merchantOrderId);
            return Task.FromResult(inquiryByReference?.Invoke(merchantOrderId) ?? inquiryResult);
        }

        public override Task<PaymobIntentionResult> CreateIntentionAsync(
            decimal amountEgp, string specialReference, string customerEmail, int expirationSeconds,
            IReadOnlyList<PaymobItemLine>? items = null, string? savedCardToken = null)
        {
            CreateIntentionCallCount++;
            LastSavedCardToken = savedCardToken;
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
        Assert.Equal("Student", order.CancelledBy);
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

        var (checkoutUrl, alreadyPaid) = await orderBusiness.ResumeCheckoutAsync(orderId, studentId, "student@example.com");

        Assert.NotNull(checkoutUrl);
        Assert.False(alreadyPaid);
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

        // Establish a real PaymobReferences entry the way CreateStudentOrderAsync
        // normally would, via a fake client standing in for the initial checkout.
        var setupFake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var (orderId, _) = await CreateOrderBusinessWith(setupFake).CreateStudentOrderAsync(studentId, "student@example.com", [new NewOrderItem(itemId, 1, null)]);

        // Now the student taps "Continue to Payment" - Paymob reports the
        // original attempt actually already succeeded.
        var resumeFake = new FakePaymobClient(new PaymobInquiryResult(true, true, 888999, 100m));
        var orderBusiness = CreateOrderBusinessWith(resumeFake);

        var (checkoutUrl, alreadyPaid) = await orderBusiness.ResumeCheckoutAsync(orderId, studentId, "student@example.com");

        // Good news, not an error - a client has no sane way to tell "this
        // genuinely failed" apart from "this actually already succeeded" if
        // both throw the same exception, so this must be a normal result,
        // not a thrown one (see clsOrderBusiness.ResumeCheckoutAsync).
        Assert.True(alreadyPaid);
        Assert.Null(checkoutUrl);
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

        var (checkoutUrl, alreadyPaid) = await orderBusiness.ResumeCheckoutAsync(orderId, studentId, "student@example.com");

        Assert.NotNull(checkoutUrl);
        Assert.False(alreadyPaid);
        Assert.Equal(1, resumeFake.InquiryCallCount);
        Assert.Equal(1, resumeFake.CreateIntentionCallCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.AwaitingPayment, order!.Status); // untouched
    }

    // Found live: a student backed out mid-attempt (abandoning the bank's
    // own 3D Secure step), and a retry created a second, different Paymob
    // reference. Only remembering the newest one would mean an earlier
    // attempt that later resolves to a real charge becomes permanently
    // unverifiable by anything except a webhook - see Order.PaymobReferences.
    [Fact]
    public async Task ResumeCheckout_FindsASuccessOnAnEarlierAttempt_NotJustTheLatestOne()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();

        // First attempt: created, but the student backed out before it
        // resolved (still not-found/unresolved when checked at the time).
        var firstAttemptFake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var (orderId, _) = await CreateOrderBusinessWith(firstAttemptFake).CreateStudentOrderAsync(studentId, "student@example.com", [new NewOrderItem(itemId, 1, null)]);

        // Second attempt (a resume/retry): also never resolves at the time
        // the student is looking at it.
        var secondAttemptFake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        await CreateOrderBusinessWith(secondAttemptFake).ResumeCheckoutAsync(orderId, studentId, "student@example.com");

        var order = await OrderBusiness.GetByIdAsync(orderId);
        var references = order!.PaymobReferences!.Split(';');
        Assert.Equal(2, references.Length); // both attempts were actually recorded, not just the latest

        // Some time later, it turns out the FIRST attempt actually succeeded
        // (the bank approved it after all) - the second one never did.
        var checkFake = new FakePaymobClient(
            new PaymobInquiryResult(false, false, null, null),
            reference => reference == references[0]
                ? new PaymobInquiryResult(true, true, 999111, 100m)
                : new PaymobInquiryResult(false, false, null, null));

        var (checkoutUrl, alreadyPaid) = await CreateOrderBusinessWith(checkFake).ResumeCheckoutAsync(orderId, studentId, "student@example.com");

        Assert.True(alreadyPaid);
        Assert.Null(checkoutUrl);
        var reconciled = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, reconciled!.Status);
        Assert.Equal(999111, reconciled.PaymobTransactionId);
    }

    // ReconcileIfAwaitingPaymentAsync - lets the order-detail poll notice a
    // payment succeeding on its own, before the student ever taps "Continue
    // Payment" and hits the (now harmless, but still needless) already-paid
    // path. See StudentOrdersController.BuildOrderResponseAsync.
    [Fact]
    public async Task ReconcileIfAwaitingPayment_UpdatesTheOrder_WhenPaymobConfirmsSuccess()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();

        var setupFake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var (orderId, _) = await CreateOrderBusinessWith(setupFake).CreateStudentOrderAsync(studentId, "student@example.com", [new NewOrderItem(itemId, 1, null)]);
        var order = await OrderBusiness.GetByIdAsync(orderId);

        var fake = new FakePaymobClient(new PaymobInquiryResult(true, true, 555444, 100m));
        var orderBusiness = CreateOrderBusinessWith(fake);

        var reconciled = await orderBusiness.ReconcileIfAwaitingPaymentAsync(order!);

        Assert.Equal(OrderStatus.Placed, reconciled.Status);
        Assert.Equal(555444, reconciled.PaymobTransactionId);
    }

    [Fact]
    public async Task ReconcileIfAwaitingPayment_LeavesNonAwaitingPaymentOrdersUntouched()
    {
        var (orderId, _) = await CreateAwaitingPaymentOrderAsync();
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.Placed);
        var placedOrder = (await OrderBusiness.GetByIdAsync(orderId))!;

        var fake = new FakePaymobClient(new PaymobInquiryResult(true, true, 555444, 100m));
        var orderBusiness = CreateOrderBusinessWith(fake);

        await orderBusiness.ReconcileIfAwaitingPaymentAsync(placedOrder);

        Assert.Equal(0, fake.InquiryCallCount); // no reason to ask Paymob about anything - already resolved
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
            await connection.ExecuteAsync("UPDATE Orders SET PaymobReferences = @Ref WHERE OrderId = @OrderId",
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
        // No PaymobReferences at all - nothing to ask Paymob about, should
        // behave exactly as before this change.
        var orderId = await CreateStaleAwaitingPaymentOrderAsync(lastPaymobReference: null);

        var fake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var orderBusiness = CreateOrderBusinessWith(fake);

        var cancelledCount = await orderBusiness.CancelAbandonedPaymentsAsync();

        Assert.Equal(1, cancelledCount);
        Assert.Equal(0, fake.InquiryCallCount);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
    }

    // Found live: a student who chose "pay with saved card" backed out
    // while the first attempt was still processing (it had actually already
    // succeeded) and tapped "Continue Payment" - which used to silently
    // drop the saved-card choice and ask for card details again.
    [Fact]
    public async Task ResumeCheckout_WithUseSavedCard_ReoffersTheSavedCard()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var email = $"resume-saved-card-{Guid.NewGuid():N}@example.com";
        var studentId = await CreateStudentAsync(email);
        await StudentBusiness.SaveCardTokenAsync(email, "tok_abc123", "xxxx-xxxx-xxxx-2346", "MasterCard");

        var setupFake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var (orderId, _) = await CreateOrderBusinessWith(setupFake).CreateStudentOrderAsync(studentId, email, [new NewOrderItem(itemId, 1, null)]);

        var resumeFake = new FakePaymobClient(new PaymobInquiryResult(false, false, null, null));
        var orderBusiness = CreateOrderBusinessWith(resumeFake);

        await orderBusiness.ResumeCheckoutAsync(orderId, studentId, email, useSavedCard: true);

        Assert.Equal("tok_abc123", resumeFake.LastSavedCardToken);
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
