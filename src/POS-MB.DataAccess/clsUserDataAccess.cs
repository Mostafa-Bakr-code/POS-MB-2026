using Dapper;
using POS_MB.DataAccess.Models;

namespace POS_MB.DataAccess;

public class clsUserDataAccess(ISqlConnectionFactory connectionFactory)
{
    // Password column is aliased to PasswordHash so the model/every call site
    // is honest about the fact that it's never plaintext.
    private const string SelectColumns =
        "UserId, UserName, Password AS PasswordHash, Permissions, IsActive, CreatedAt, UpdatedAt";

    public async Task<IEnumerable<User>> GetAllAsync(bool includeInactive = false)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = $"SELECT {SelectColumns} FROM Users";
        if (!includeInactive) query += " WHERE IsActive = 1";

        return await connection.QueryAsync<User>(query);
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = $"SELECT {SelectColumns} FROM Users WHERE UserId = @Id";

        return await connection.QuerySingleOrDefaultAsync<User>(query, new { Id = id });
    }

    public async Task<User?> GetByUserNameAsync(string userName)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = $"SELECT {SelectColumns} FROM Users WHERE UserName = @UserName";

        return await connection.QuerySingleOrDefaultAsync<User>(query, new { UserName = userName });
    }

    public async Task<bool> ExistsAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = "SELECT COUNT(1) FROM Users WHERE UserId = @Id";

        return await connection.ExecuteScalarAsync<int>(query, new { Id = id }) > 0;
    }

    public async Task<int> AddAsync(string userName, string passwordHash, int permissions)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            INSERT INTO Users (UserName, Password, Permissions)
            OUTPUT INSERTED.UserId
            VALUES (@UserName, @PasswordHash, @Permissions);";

        return await connection.ExecuteScalarAsync<int>(
            query, new { UserName = userName, PasswordHash = passwordHash, Permissions = permissions });
    }

    public async Task<bool> UpdateAsync(int id, string userName, string passwordHash, int permissions)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Users
            SET UserName = @UserName,
                Password = @PasswordHash,
                Permissions = @Permissions,
                UpdatedAt = SYSUTCDATETIME()
            WHERE UserId = @Id";

        var rowsAffected = await connection.ExecuteAsync(
            query, new { Id = id, UserName = userName, PasswordHash = passwordHash, Permissions = permissions });

        return rowsAffected > 0;
    }

    public async Task<bool> DeactivateAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Users
            SET IsActive = 0,
                UpdatedAt = SYSUTCDATETIME()
            WHERE UserId = @Id";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id });

        return rowsAffected > 0;
    }

    public async Task<bool> ReactivateAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Users
            SET IsActive = 1,
                UpdatedAt = SYSUTCDATETIME()
            WHERE UserId = @Id";

        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id });

        return rowsAffected > 0;
    }
}
