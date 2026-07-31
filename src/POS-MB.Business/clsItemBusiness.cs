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

    public async Task<int> CreateAsync(string name, int categoryId, decimal price, decimal? taxRate = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Item name is required.", nameof(name));
        if (price < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(price));

        var resolvedTaxRate = taxRate ?? await GetDefaultTaxRateAsync();

        return await dataAccess.AddAsync(name, categoryId, price, resolvedTaxRate);
    }

    public async Task<bool> UpdateAsync(int id, string name, int categoryId, decimal price, decimal? taxRate = null, int? changedByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Item name is required.", nameof(name));
        if (price < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(price));

        var resolvedTaxRate = taxRate ?? await GetDefaultTaxRateAsync();

        var existing = await dataAccess.GetByIdAsync(id);
        if (existing is null) return false;

        var updated = await dataAccess.UpdateAsync(id, name, categoryId, price, resolvedTaxRate);

        if (updated && (existing.Price != price || existing.TaxRate != resolvedTaxRate))
        {
            await dataAccess.LogPriceChangeAsync(id, existing.Price, price, existing.TaxRate, resolvedTaxRate, changedByUserId);
        }

        return updated;
    }

    public Task<bool> DeactivateAsync(int id) =>
        dataAccess.DeactivateAsync(id);

    public Task<bool> SetAvailabilityAsync(int id, bool isAvailable) =>
        dataAccess.SetAvailabilityAsync(id, isAvailable);

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
