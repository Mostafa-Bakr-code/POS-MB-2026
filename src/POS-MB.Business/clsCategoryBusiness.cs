using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsCategoryBusiness(clsCategoryDataAccess dataAccess)
{
    public Task<IEnumerable<Category>> GetAllAsync(bool includeInactive = false) =>
        dataAccess.GetAllAsync(includeInactive);

    public Task<Category?> GetByIdAsync(int id) =>
        dataAccess.GetByIdAsync(id);

    public Task<Category?> GetByNameAsync(string name) =>
        dataAccess.GetByNameAsync(name);

    public Task<bool> ExistsAsync(int id) =>
        dataAccess.ExistsAsync(id);

    public async Task<int> CreateAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category name is required.", nameof(name));

        return await dataAccess.AddAsync(name);
    }

    public async Task<bool> UpdateAsync(int id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Category name is required.", nameof(name));

        return await dataAccess.UpdateAsync(id, name);
    }

    public Task<bool> DeactivateAsync(int id) =>
        dataAccess.DeactivateAsync(id);
}
