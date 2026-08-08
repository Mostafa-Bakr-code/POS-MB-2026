using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// A student can only self-cancel while the kitchen hasn't started yet
// (Status still Placed) - once it's Preparing, real ingredients/time are
// already committed, so there's no turning back. Enforced server-side, not
// just hidden in the mobile UI.
public class StudentOrderCancellationTests : DatabaseTestBase
{
    [Fact]
    public async Task CancelForStudent_Succeeds_WhileOrderIsStillPlaced()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);

        var cancelled = await OrderBusiness.CancelForStudentAsync(orderId, studentId);

        Assert.True(cancelled);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Cancelled, order!.Status);
    }

    [Fact]
    public async Task CancelForStudent_Throws_OnceKitchenHasStartedPreparing()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.Preparing);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            OrderBusiness.CancelForStudentAsync(orderId, studentId));

        // Rejected attempt must not have changed anything.
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Preparing, order!.Status);
    }

    [Fact]
    public async Task CancelForStudent_ReturnsFalse_WhenOrderIsNotTheStudentsOwn()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var ownerId = await CreateStudentAsync();
        var otherId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, ownerId, false, [new NewOrderItem(itemId, 1, null)]);

        var cancelled = await OrderBusiness.CancelForStudentAsync(orderId, otherId);

        Assert.False(cancelled);
        var order = await OrderBusiness.GetByIdAsync(orderId);
        Assert.Equal(OrderStatus.Placed, order!.Status);
    }
}
