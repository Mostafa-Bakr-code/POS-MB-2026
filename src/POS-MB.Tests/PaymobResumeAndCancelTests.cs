using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// A student stuck with an order at AwaitingPayment (backed out of the
// payment screen, app crashed mid-checkout) needs a way forward besides
// waiting out the auto-cancel timeout - see clsOrderBusiness.ResumeCheckoutAsync
// and the CancelForStudentAsync extension to allow AwaitingPayment too.
public class PaymobResumeAndCancelTests : DatabaseTestBase
{
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
    public async Task GetByDateAndSerialNumber_ResolvesTheSameOrder()
    {
        var (orderId, _) = await CreateAwaitingPaymentOrderAsync();
        var order = await OrderBusiness.GetByIdAsync(orderId);

        var resolved = await OrderBusiness.GetByDateAndSerialNumberAsync(order!.Date, order.SerialNumber!.Value);

        Assert.NotNull(resolved);
        Assert.Equal(orderId, resolved!.OrderId);
    }
}
