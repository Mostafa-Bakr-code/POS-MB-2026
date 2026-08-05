using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using POS_MB.DataAccess;

namespace POS_MB.Business;

public class clsRefreshTokenBusiness(clsRefreshTokenDataAccess dataAccess, ILogger<clsRefreshTokenBusiness> logger)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(1);

    public async Task<string> IssueAsync(int userId)
    {
        var (plainText, hash) = GenerateToken();
        await dataAccess.AddAsync(userId, hash, DateTime.UtcNow.Add(Lifetime));
        return plainText;
    }

    /// <summary>
    /// Validates a refresh token and rotates it: the presented token is atomically
    /// found-and-revoked in a single UPDATE (TryClaimActiveTokenAsync), so two
    /// concurrent requests presenting the same token can never both succeed - only
    /// one can ever win the claim, closing a check-then-update race a separate
    /// SELECT-then-UPDATE would leave open. Returns null if the token is unknown,
    /// expired, or already revoked - a revoked token being presented again means
    /// someone is replaying a token that was already rotated away (the legitimate
    /// holder would have the newer one instead), which is treated as a sign of
    /// theft: every refresh token for that user is revoked as a precaution,
    /// forcing a fresh login everywhere.
    /// </summary>
    public async Task<(int UserId, string NewToken)?> ValidateAndRotateAsync(string presentedToken)
    {
        var hash = Hash(presentedToken);
        var claimed = await dataAccess.TryClaimActiveTokenAsync(hash);

        if (claimed is null)
        {
            // The atomic claim above is what actually enforces single-use; this
            // lookup is purely diagnostic, to tell "already revoked" (a real
            // theft signal) apart from "unknown/expired" (routine).
            var existing = await dataAccess.GetByTokenHashAsync(hash);
            if (existing is not null && existing.RevokedAt is not null)
            {
                logger.LogWarning("Refresh-token reuse detected for UserId={UserId} - all refresh tokens revoked",
                    existing.UserId);
                await dataAccess.RevokeAllForUserAsync(existing.UserId);
            }
            return null;
        }

        var (plainText, newHash) = GenerateToken();
        await dataAccess.AddAsync(claimed.UserId, newHash, DateTime.UtcNow.Add(Lifetime));

        return (claimed.UserId, plainText);
    }

    /// <summary>
    /// Revokes every refresh token a user currently holds - used when a password
    /// changes or an account is deactivated, so a session token issued before
    /// either of those (stolen or otherwise) doesn't keep working afterward.
    /// </summary>
    public Task RevokeAllForUserAsync(int userId) =>
        dataAccess.RevokeAllForUserAsync(userId);

    /// <summary>
    /// Revokes a refresh token on logout - but only if it actually belongs to
    /// the caller. Without this check, any authenticated user could submit any
    /// refresh token value and log someone else out (an ownership violation:
    /// logout should only ever be able to end your own session). Returns false
    /// if the token doesn't exist or belongs to a different user - the caller
    /// treats both the same as "nothing to revoke" either way, without
    /// distinguishing which, so a mismatch doesn't confirm whether a given
    /// token value belongs to someone else.
    /// </summary>
    public async Task<bool> RevokeAsync(string presentedToken, int expectedUserId)
    {
        var existing = await dataAccess.GetByTokenHashAsync(Hash(presentedToken));
        if (existing is null || existing.UserId != expectedUserId) return false;

        if (existing.RevokedAt is null)
            await dataAccess.RevokeAsync(existing.RefreshTokenId);

        return true;
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
