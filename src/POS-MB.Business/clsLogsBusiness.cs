using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsLogsBusiness(clsLogsDataAccess dataAccess)
{
    public Task<int> StartSessionAsync(int userId) =>
        dataAccess.StartSessionAsync(userId);

    public Task<bool> EndSessionAsync(int logId) =>
        dataAccess.EndSessionAsync(logId);

    public Task<IEnumerable<Log>> GetAllAsync(DateTime? startDate = null, DateTime? endDate = null) =>
        dataAccess.GetAllAsync(startDate, endDate);
}
