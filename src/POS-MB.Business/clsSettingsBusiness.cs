using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsSettingsBusiness(clsSettingsDataAccess dataAccess)
{
    public Task<IEnumerable<Setting>> GetAllAsync() =>
        dataAccess.GetAllAsync();

    public Task<Setting?> GetByKeyAsync(string key) =>
        dataAccess.GetByKeyAsync(key);

    public async Task<int> SetAsync(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Setting key is required.", nameof(key));

        return await dataAccess.UpsertAsync(key, value);
    }

    public Task<bool> DeleteAsync(string key) =>
        dataAccess.DeleteAsync(key);
}
