using POS_MB.DataAccess.Models;

namespace POS_MB.Tests;

// Deactivating a category/item is meant to be purely a visibility switch (hidden
// from the menu) - it must never change historical reporting numbers, and
// reactivating must bring it back exactly as it was. This proves that guarantee
// instead of just asserting it.
public class ReactivationTests : DatabaseTestBase
{
    [Fact]
    public async Task DeactivatingItemAndCategory_DoesNotChangeHistoricalSalesNumbers()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 114m, taxRate: 14m);
        var userId = await CreateUserAsync();

        await OrderBusiness.CreateOrderAsync(OrderSource.Cashier, userId, false, [new NewOrderItem(itemId, 1, null)]);

        var before = await ReportingBusiness.GetSalesSummaryAsync();

        await ItemBusiness.DeactivateAsync(itemId);
        await CategoryBusiness.DeactivateAsync(categoryId);

        var afterDeactivate = await ReportingBusiness.GetSalesSummaryAsync();
        Assert.Equal(before.TotalRevenue, afterDeactivate.TotalRevenue);
        Assert.Equal(before.TotalTax, afterDeactivate.TotalTax);
        Assert.Equal(before.TotalOrders, afterDeactivate.TotalOrders);

        var itemRowBeforeReactivate = Assert.Single(await ReportingBusiness.GetItemSalesAsync());
        Assert.Equal(1, itemRowBeforeReactivate.Quantity); // still reported while inactive

        await ItemBusiness.ReactivateAsync(itemId);
        await CategoryBusiness.ReactivateAsync(categoryId);

        var afterReactivate = await ReportingBusiness.GetSalesSummaryAsync();
        Assert.Equal(before.TotalRevenue, afterReactivate.TotalRevenue);
    }

    [Fact]
    public async Task ReactivatedItem_IsVisibleAgain_InTheActiveOnlyItemList()
    {
        var categoryId = await CreateCategoryAsync();
        var itemId = await CreateItemAsync(categoryId, "Item", price: 100m);

        await ItemBusiness.DeactivateAsync(itemId);
        var whileInactive = await ItemBusiness.GetAllAsync(includeInactive: false);
        Assert.DoesNotContain(whileInactive, i => i.ItemId == itemId);

        await ItemBusiness.ReactivateAsync(itemId);
        var afterReactivate = await ItemBusiness.GetAllAsync(includeInactive: false);
        Assert.Contains(afterReactivate, i => i.ItemId == itemId);
    }
}
