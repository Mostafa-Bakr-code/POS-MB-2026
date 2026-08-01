using Dapper;
using POS_MB.DataAccess.Models;

namespace POS_MB.DataAccess;

public class clsCategoryDataAccess(ISqlConnectionFactory connectionFactory)
{
    public async Task<IEnumerable<Category>> GetAllAsync(bool includeInactive = false)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = includeInactive
            ? "SELECT * FROM Categories"
            : "SELECT * FROM Categories WHERE IsActive = 1";

        return await connection.QueryAsync<Category>(query);
    }

    public async Task<Category?> GetByIdAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT * FROM Categories WHERE CategoryId = @Id";

        return await connection.QuerySingleOrDefaultAsync<Category>(query, new { Id = id });
    }

    public async Task<Category?> GetByNameAsync(string name)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT * FROM Categories WHERE CategoryName = @Name";

        return await connection.QuerySingleOrDefaultAsync<Category>(query, new { Name = name });
    }

    public async Task<bool> ExistsAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT COUNT(1) FROM Categories WHERE CategoryId = @Id";

        return await connection.ExecuteScalarAsync<int>(query, new { Id = id }) > 0;
    }

    public async Task<int> AddAsync(string name)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            INSERT INTO Categories (CategoryName)
            OUTPUT INSERTED.CategoryId
            VALUES (@Name);";

        return await connection.ExecuteScalarAsync<int>(query, new { Name = name });
    }

    public async Task<bool> UpdateAsync(int id, string name)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Categories
            SET CategoryName = @Name,
                UpdatedAt = SYSUTCDATETIME()
            WHERE CategoryId = @Id";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id, Name = name });

        return rowsAffected > 0;
    }

    public async Task<bool> DeactivateAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Categories
            SET IsActive = 0,
                UpdatedAt = SYSUTCDATETIME()
            WHERE CategoryId = @Id";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id });

        return rowsAffected > 0;
    }

    public async Task<bool> ReactivateAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Categories
            SET IsActive = 1,
                UpdatedAt = SYSUTCDATETIME()
            WHERE CategoryId = @Id";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id });

        return rowsAffected > 0;
    }
}
