using System.Security.Cryptography;
using System.Text;
using POS_MB.DataAccess;

namespace POS_MB.Business;

public class clsRefreshTokenBusiness(clsRefreshTokenDataAccess dataAccess)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    public async Task<string> IssueAsync(int userId)
    {
        var (plainText, hash) = GenerateToken();
        await dataAccess.AddAsync(userId, hash, DateTime.UtcNow.Add(Lifetime));
        return plainText;
    }

    /// <summary>
    /// Validates a refresh token and rotates it: the presented token is revoked and
    /// a new one issued in its place, so each token is only ever usable once. Returns
    /// null if the token is unknown, expired, or already revoked - a revoked token
    /// being presented again means someone is replaying a token that was already
    /// rotated away (the legitimate holder would have the newer one instead), which
    /// is treated as a sign of theft: every refresh token for that user is revoked
    /// as a precaution, forcing a fresh login everywhere.
    /// </summary>
    public async Task<(int UserId, string NewToken)?> ValidateAndRotateAsync(string presentedToken)
    {
        var hash = Hash(presentedToken);
        var existing = await dataAccess.GetByTokenHashAsync(hash);
        if (existing is null) return null;

        if (existing.RevokedAt is not null)
        {
            await dataAccess.RevokeAllForUserAsync(existing.UserId);
            return null;
        }

        if (existing.ExpiresAt <= DateTime.UtcNow) return null;

        await dataAccess.RevokeAsync(existing.RefreshTokenId);

        var (plainText, newHash) = GenerateToken();
        await dataAccess.AddAsync(existing.UserId, newHash, DateTime.UtcNow.Add(Lifetime));

        return (existing.UserId, plainText);
    }

    public async Task RevokeAsync(string presentedToken)
    {
        var existing = await dataAccess.GetByTokenHashAsync(Hash(presentedToken));
        if (existing is not null && existing.RevokedAt is null)
            await dataAccess.RevokeAsync(existing.RefreshTokenId);
    }

    private static (string PlainText, string Hash) GenerateToken()
    {
        var plainText = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (plainText, Hash(plainText));
    }

    // Unlike passwords, this is a high-entropy random value an attacker can't
    // guess - a fast cryptographic hash is enough, no need for the slow,
    // salted hashing passwords require.
    private static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
