using System.Globalization;
using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsItemBusiness(clsItemDataAccess dataAccess, clsSettingsBusiness settingsBusiness)
{
    private const string DefaultTaxRateSettingKey = "DefaultTaxRate";
    private const decimal FallbackTaxRate = 14.00m;

    public Task<IEnumerable<Item>> GetAllAsync(bool includeInactive = false, int? categoryId = null, bool availableOnly = false) =>
        dataAccess.GetAllAsync(includeInactive, categoryId, availableOnly);

    public Task<Item?> GetByIdAsync(int id) =>
        dataAccess.GetByIdAsync(id);

    public Task<bool> ExistsAsync(int id) =>
        dataAccess.ExistsAsync(id);

    public async Task<int> CreateAsync(string name, int categoryId, decimal price, decimal? taxRate = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Item name is required.", nameof(name));
        if (price < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(price));

        var resolvedTaxRate = taxRate ?? await GetDefaultTaxRateAsync();

        return await dataAccess.AddAsync(name, categoryId, price, resolvedTaxRate, description);
    }

    public async Task<bool> UpdateAsync(int id, string name, int categoryId, decimal price, decimal? taxRate = null, int? changedByUserId = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Item name is required.", nameof(name));
        if (price < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(price));

        var existing = await dataAccess.GetByIdAsync(id);
        if (existing is null) return false;

        // Unlike CreateAsync (a genuinely new item with no rate to preserve),
        // "not specified" here must mean "keep this item's own current rate",
        // not "use the shop default" - found live: the WinForms edit dialog
        // doesn't expose a tax-rate field at all, so every plain name/price
        // edit was silently resetting any item with a non-default rate (e.g.
        // a 0%-rated item) back to the default.
        var resolvedTaxRate = taxRate ?? existing.TaxRate;

        // The price-history insert happens inside dataAccess.UpdateAsync itself,
        // in the same transaction as the UPDATE - see its comment for why.
        return await dataAccess.UpdateAsync(id, name, categoryId, price, resolvedTaxRate, description, existing.Price, existing.TaxRate, changedByUserId);
    }

    public Task<bool> DeactivateAsync(int id) =>
        dataAccess.DeactivateAsync(id);

    public Task<bool> ReactivateAsync(int id) =>
        dataAccess.ReactivateAsync(id);

    public Task<bool> SetAvailabilityAsync(int id, bool isAvailable) =>
        dataAccess.SetAvailabilityAsync(id, isAvailable);

    public Task<bool> SetImageUrlAsync(int id, string? imageUrl) =>
        dataAccess.SetImageUrlAsync(id, imageUrl);

    public Task<IEnumerable<ItemPriceHistory>> GetPriceHistoryAsync(int id) =>
        dataAccess.GetPriceHistoryAsync(id);

    private async Task<decimal> GetDefaultTaxRateAsync()
    {
        var setting = await settingsBusiness.GetByKeyAsync(DefaultTaxRateSettingKey);

        if (setting?.Value is not null && decimal.TryParse(setting.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate))
            return rate;

        return FallbackTaxRate;
    }
}
