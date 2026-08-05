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

    // expectedUserId scopes the update to the caller's own session row - without
    // it, any authenticated user could end (or, if guessed early, prematurely
    // stamp LogOut on) someone else's active shift by guessing a sequential LogId.
    public async Task<bool> EndSessionAsync(int logId, int expectedUserId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Logs
            SET LogOut = SYSUTCDATETIME()
            WHERE LogId = @LogId AND UserId = @UserId AND LogOut IS NULL";

        var rowsAffected = await connection.ExecuteAsync(query, new { LogId = logId, UserId = expectedUserId });

        return rowsAffected > 0;
    }

    // utcStart/utcEndExclusive are already-resolved UTC instants (local-timezone conversion
    // happens in clsLogsBusiness) - filtering on the raw LogIn column, not a CAST(...AS DATE)
    // shortcut, since that would compare against the UTC calendar day, not the caller's local day.
    public async Task<IEnumerable<Log>> GetAllAsync(DateTime? utcStart = null, DateTime? utcEndExclusive = null)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = "SELECT * FROM Logs WHERE 1 = 1";
        if (utcStart is not null) query += " AND LogIn >= @UtcStart";
        if (utcEndExclusive is not null) query += " AND LogIn < @UtcEndExclusive";
        query += " ORDER BY LogId DESC";

        return await connection.QueryAsync<Log>(
            query, new { UtcStart = utcStart, UtcEndExclusive = utcEndExclusive });
    }
}
