using Microsoft.AspNetCore.Identity;
using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public class clsUserBusiness(clsUserDataAccess dataAccess)
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

        string passwordHash;
        if (string.IsNullOrWhiteSpace(password))
        {
            var existing = await dataAccess.GetByIdAsync(id)
                ?? throw new InvalidOperationException($"User {id} does not exist.");
            passwordHash = existing.PasswordHash;
        }
        else
        {
            passwordHash = Hasher.HashPassword(new User(), password);
        }

        return await dataAccess.UpdateAsync(id, userName, passwordHash, permissions);
    }

    public Task<bool> DeactivateAsync(int id) =>
        dataAccess.DeactivateAsync(id);

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
