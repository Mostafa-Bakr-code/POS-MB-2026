using Dapper;
using POS_MB.DataAccess.Models;

namespace POS_MB.DataAccess;

public class clsLogsDataAccess(ISqlConnectionFactory connectionFactory)
{
    public async Task<int> StartSessionAsync(int userId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            INSERT INTO Logs (UserId, LogIn)
            OUTPUT INSERTED.LogId
            VALUES (@UserId, SYSUTCDATETIME());";

        return await connection.ExecuteScalarAsync<int>(query, new { UserId = userId });
    }

    public async Task<bool> EndSessionAsync(int logId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Logs
            SET LogOut = SYSUTCDATETIME()
            WHERE LogId = @LogId AND LogOut IS NULL";

        var rowsAffected = await connection.ExecuteAsync(query, new { LogId = logId });

        return rowsAffected > 0;
    }

    public async Task<IEnumerable<Log>> GetAllAsync(DateTime? startDate = null, DateTime? endDate = null)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = "SELECT * FROM Logs WHERE 1 = 1";
        if (startDate is not null) query += " AND CAST(LogIn AS DATE) >= @StartDate";
        if (endDate is not null) query += " AND CAST(LogIn AS DATE) <= @EndDate";
        query += " ORDER BY LogId DESC";

        return await connection.QueryAsync<Log>(
            query, new { StartDate = startDate?.Date, EndDate = endDate?.Date });
    }
}
