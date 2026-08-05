using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsLogsBusiness(clsLogsDataAccess dataAccess, clsSettingsBusiness settingsBusiness)
{
    public Task<int> StartSessionAsync(int userId) =>
        dataAccess.StartSessionAsync(userId);

    public Task<bool> EndSessionAsync(int logId, int expectedUserId) =>
        dataAccess.EndSessionAsync(logId, expectedUserId);

    public async Task<IEnumerable<Log>> GetAllAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        var (utcStart, utcEndExclusive) = await TimeZoneHelper.ResolveUtcRangeAsync(settingsBusiness, startDate, endDate);
        return await dataAccess.GetAllAsync(utcStart, utcEndExclusive);
    }
}
