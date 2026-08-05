using Dapper;
using POS_MB.DataAccess.Models;

namespace POS_MB.DataAccess;

public class clsRefreshTokenDataAccess(ISqlConnectionFactory connectionFactory)
{
    public async Task<int> AddAsync(int userId, string tokenHash, DateTime expiresAt)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            INSERT INTO RefreshTokens (UserId, TokenHash, ExpiresAt)
            OUTPUT INSERTED.RefreshTokenId
            VALUES (@UserId, @TokenHash, @ExpiresAt);";

        return await connection.ExecuteScalarAsync<int>(
            query, new { UserId = userId, TokenHash = tokenHash, ExpiresAt = expiresAt });
    }

    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            SELECT RefreshTokenId, UserId, TokenHash, ExpiresAt, RevokedAt, CreatedAt
            FROM RefreshTokens
            WHERE TokenHash = @TokenHash";

        return await connection.QuerySingleOrDefaultAsync<RefreshToken>(query, new { TokenHash = tokenHash });
    }

    public async Task RevokeAsync(int refreshTokenId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE RefreshTokens
            SET RevokedAt = SYSUTCDATETIME()
            WHERE RefreshTokenId = @Id AND RevokedAt IS NULL";

        await connection.ExecuteAsync(query, new { Id = refreshTokenId });
    }

    public async Task RevokeAllForUserAsync(int userId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE RefreshTokens
            SET RevokedAt = SYSUTCDATETIME()
            WHERE UserId = @UserId AND RevokedAt IS NULL";

        await connection.ExecuteAsync(query, new { UserId = userId });
    }
}
