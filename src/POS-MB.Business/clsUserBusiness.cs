using Microsoft.AspNetCore.Identity;
using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsUserBusiness(clsUserDataAccess dataAccess, clsRefreshTokenBusiness refreshTokenBusiness)
{
    private static readonly PasswordHasher<User> Hasher = new();

    public Task<IEnumerable<User>> GetAllAsync(bool includeInactive = false) =>
        dataAccess.GetAllAsync(includeInactive);

    public Task<User?> GetByIdAsync(int id) =>
        dataAccess.GetByIdAsync(id);

    public Task<bool> ExistsAsync(int id) =>
        dataAccess.ExistsAsync(id);

    public async Task<int> CreateAsync(string userName, string password, int permissions)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("Username is required.", nameof(userName));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.", nameof(password));

        var passwordHash = Hasher.HashPassword(new User(), password);

        return await dataAccess.AddAsync(userName, passwordHash, permissions);
    }

    /// <summary>
    /// Updates a user. A null/blank password leaves the existing password unchanged -
    /// editing someone's permissions shouldn't force resetting their password too.
    /// </summary>
    public async Task<bool> UpdateAsync(int id, string userName, string? password, int permissions)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("Username is required.", nameof(userName));

        var passwordChanged = !string.IsNullOrWhiteSpace(password);

        string passwordHash;
        if (!passwordChanged)
        {
            var existing = await dataAccess.GetByIdAsync(id)
                ?? throw new InvalidOperationException($"User {id} does not exist.");
            passwordHash = existing.PasswordHash;
        }
        else
        {
            passwordHash = Hasher.HashPassword(new User(), password!);
        }

        var updated = await dataAccess.UpdateAsync(id, userName, passwordHash, permissions);

        // A password change is how an admin locks out a compromised account -
        // that only works if it also kills any refresh token already issued
        // (stolen or otherwise), not just future login attempts.
        if (updated && passwordChanged)
            await refreshTokenBusiness.RevokeAllForUserAsync(id);

        return updated;
    }

    public async Task<bool> DeactivateAsync(int id)
    {
        var deactivated = await dataAccess.DeactivateAsync(id);
        if (deactivated)
            await refreshTokenBusiness.RevokeAllForUserAsync(id);
        return deactivated;
    }

    public Task<bool> ReactivateAsync(int id) =>
        dataAccess.ReactivateAsync(id);

    /// <summary>
    /// Verifies a username/password pair. Returns the user on success, null on any failure
    /// (unknown username or wrong password look identical to the caller, to avoid leaking
    /// which one was wrong). Does not issue a session/token — that's a separate later step.
    /// </summary>
    public async Task<User?> VerifyCredentialsAsync(string userName, string password)
    {
        var user = await dataAccess.GetByUserNameAsync(userName);
        if (user is null || !user.IsActive)
            return null;

        var result = Hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var newHash = Hasher.HashPassword(user, password);
            await dataAccess.UpdateAsync(user.UserId, user.UserName, newHash, user.Permissions);
        }

        return user;
    }
}
