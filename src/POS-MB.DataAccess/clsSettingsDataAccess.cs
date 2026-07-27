using Dapper;
using POS_MB.DataAccess.Models;

namespace POS_MB.DataAccess;

public class clsSettingsDataAccess(ISqlConnectionFactory connectionFactory)
{
    public async Task<IEnumerable<Setting>> GetAllAsync()
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT * FROM Settings";

        return await connection.QueryAsync<Setting>(query);
    }

    public async Task<Setting?> GetByKeyAsync(string key)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT * FROM Settings WHERE [Key] = @Key";

        return await connection.QuerySingleOrDefaultAsync<Setting>(query, new { Key = key });
    }

    public async Task<int> UpsertAsync(string key, string? value)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            MERGE Settings AS target
            USING (SELECT @Key AS [Key], @Value AS [Value]) AS source
            ON target.[Key] = source.[Key]
            WHEN MATCHED THEN
                UPDATE SET target.[Value] = source.[Value]
            WHEN NOT MATCHED THEN
                INSERT ([Key], [Value]) VALUES (source.[Key], source.[Value])
            OUTPUT INSERTED.Id;";

        return await connection.ExecuteScalarAsync<int>(query, new { Key = key, Value = value });
    }

    public async Task<bool> DeleteAsync(string key)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "DELETE FROM Settings WHERE [Key] = @Key";

        var rowsAffected = await connection.ExecuteAsync(query, new { Key = key });

        return rowsAffected > 0;
    }
}
