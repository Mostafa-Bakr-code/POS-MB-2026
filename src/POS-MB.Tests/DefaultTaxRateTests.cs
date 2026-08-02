namespace POS_MB.Tests;

public class DefaultTaxRateTests : DatabaseTestBase
{
    [Fact]
    public async Task CreateItem_WithNoExplicitTaxRate_FallsBackToTheDefaultTaxRateSetting()
    {
        await SettingsBusiness.SetAsync("DefaultTaxRate", "20");
        var categoryId = await CreateCategoryAsync();

        var itemId = await ItemBusiness.CreateAsync("Item", categoryId, price: 100m, taxRate: null);

        var item = await ItemBusiness.GetByIdAsync(itemId);
        Assert.Equal(20m, item!.TaxRate);
    }

    [Fact]
    public async Task CreateItem_WithExplicitTaxRate_OverridesTheDefaultSetting()
    {
        await SettingsBusiness.SetAsync("DefaultTaxRate", "20");
        var categoryId = await CreateCategoryAsync();

        var itemId = await ItemBusiness.CreateAsync("Item", categoryId, price: 100m, taxRate: 5m);

        var item = await ItemBusiness.GetByIdAsync(itemId);
        Assert.Equal(5m, item!.TaxRate); // explicit value wins, not the setting
    }
}
