using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// The queue the cashier PC's background poller (KitchenTicketPrintService)
// checks every few seconds - decoupled from whichever client (WinForms or
// the chef tablet) actually moved an order into Preparing, since a browser
// can't print directly. See clsOrderDataAccess.GetOrdersNeedingKitchenTicketAsync.
public class KitchenTicketQueueTests : DatabaseTestBase
{
    [Fact]
    public async Task NeedingKitchenTicket_IncludesMobileOrder_JustMovedToPreparing()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.Preparing);

        var needing = await OrderBusiness.GetOrdersNeedingKitchenTicketAsync();

        Assert.Contains(needing, o => o.OrderId == orderId);
    }

    [Fact]
    public async Task NeedingKitchenTicket_ExcludesOrder_StillPlaced()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        // Left at Placed - nothing to print until the kitchen accepts it.

        var needing = await OrderBusiness.GetOrdersNeedingKitchenTicketAsync();

        Assert.DoesNotContain(needing, o => o.OrderId == orderId);
    }

    [Fact]
    public async Task NeedingKitchenTicket_IncludesCashierOrder_AsSoonAsPlaced()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();
        // No status change needed - a Cashier order is created straight
        // into Completed and must qualify immediately (placing an order
        // never blocks on printer state), unlike Mobile which waits for
        // kitchen acceptance (Preparing).
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);

        var needing = await OrderBusiness.GetOrdersNeedingKitchenTicketAsync();

        Assert.Contains(needing, o => o.OrderId == orderId);
    }

    [Fact]
    public async Task NeedingKitchenTicket_ExcludesCancelledCashierOrder()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var userId = await CreateUserAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, null, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.Cancelled);

        var needing = await OrderBusiness.GetOrdersNeedingKitchenTicketAsync();

        Assert.DoesNotContain(needing, o => o.OrderId == orderId);
    }

    [Fact]
    public async Task MarkKitchenTicketPrinted_RemovesOrderFromTheQueue()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.Preparing);

        var marked = await OrderBusiness.MarkKitchenTicketPrintedAsync(orderId);
        var needing = await OrderBusiness.GetOrdersNeedingKitchenTicketAsync();

        Assert.True(marked);
        Assert.DoesNotContain(needing, o => o.OrderId == orderId);
    }

    [Fact]
    public async Task MarkKitchenTicketPrinted_IsIdempotent_SecondCallReturnsFalse()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);
        var studentId = await CreateStudentAsync();
        var orderId = await OrderBusiness.CreateOrderAsync(OrderSource.Mobile, null, studentId, false, [new NewOrderItem(itemId, 1, null)]);
        await OrderBusiness.UpdateStatusAsync(orderId, OrderStatus.Preparing);

        var firstMark = await OrderBusiness.MarkKitchenTicketPrintedAsync(orderId);
        var secondMark = await OrderBusiness.MarkKitchenTicketPrintedAsync(orderId);

        Assert.True(firstMark);
        Assert.False(secondMark); // already claimed - guards against a second poller double-printing
    }
}
