using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsItemBusiness(clsItemDataAccess dataAccess)
{
    public const decimal DefaultTaxRate = 14.00m;

    public Task<IEnumerable<Item>> GetAllAsync(bool includeInactive = false, int? categoryId = null) =>
        dataAccess.GetAllAsync(includeInactive, categoryId);

    public Task<Item?> GetByIdAsync(int id) =>
        dataAccess.GetByIdAsync(id);

    public Task<bool> ExistsAsync(int id) =>
        dataAccess.ExistsAsync(id);

    public async Task<int> CreateAsync(string name, int categoryId, decimal price, decimal taxRate = DefaultTaxRate)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Item name is required.", nameof(name));
        if (price < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(price));

        return await dataAccess.AddAsync(name, categoryId, price, taxRate);
    }

    public async Task<bool> UpdateAsync(int id, string name, int categoryId, decimal price, decimal taxRate = DefaultTaxRate)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Item name is required.", nameof(name));
        if (price < 0)
            throw new ArgumentException("Price cannot be negative.", nameof(price));

        return await dataAccess.UpdateAsync(id, name, categoryId, price, taxRate);
    }

    public Task<bool> DeactivateAsync(int id) =>
        dataAccess.DeactivateAsync(id);
}
