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

    // Atomically finds AND revokes an active token in one round trip (UPDATE ...
    // OUTPUT), rather than a separate SELECT-then-UPDATE - two concurrent
    // requests presenting the same still-valid token can otherwise both pass a
    // separate "is it still active" check before either revokes it, letting one
    // token spawn two rotations. SQL Server locks the row for the duration of
    // this single UPDATE, so only one caller's WHERE clause can ever match.
    public async Task<RefreshToken?> TryClaimActiveTokenAsync(string tokenHash)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE RefreshTokens
            SET RevokedAt = SYSUTCDATETIME()
            OUTPUT INSERTED.RefreshTokenId, INSERTED.UserId, INSERTED.TokenHash, INSERTED.ExpiresAt, INSERTED.RevokedAt, INSERTED.CreatedAt
            WHERE TokenHash = @TokenHash AND RevokedAt IS NULL AND ExpiresAt > SYSUTCDATETIME();";

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
