using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public partial class clsStudentBusiness(clsStudentDataAccess dataAccess)
{
    private static readonly PasswordHasher<Student> Hasher = new();

    public Task<Student?> GetByIdAsync(int id) =>
        dataAccess.GetByIdAsync(id);

    /// <summary>
    /// Creates a new student account. Throws if the email is malformed or already
    /// registered - email doubles as the sign-in identifier (no separate username),
    /// so it must be unique the same way Users.UserName is.
    /// </summary>
    public async Task<int> SignUpAsync(string email, string password)
    {
        var normalizedEmail = NormalizeEmail(email);

        if (string.IsNullOrWhiteSpace(normalizedEmail) || !EmailPattern().IsMatch(normalizedEmail))
            throw new ArgumentException("A valid email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.", nameof(password));

        if (await dataAccess.GetByEmailAsync(normalizedEmail) is not null)
            throw new ArgumentException("An account with this email already exists.", nameof(email));

        var passwordHash = Hasher.HashPassword(new Student(), password);

        return await dataAccess.AddAsync(normalizedEmail, passwordHash);
    }

    /// <summary>
    /// Verifies an email/password pair. Returns the student on success, null on any
    /// failure (unknown email or wrong password look identical to the caller) -
    /// same reasoning as clsUserBusiness.VerifyCredentialsAsync.
    /// </summary>
    public async Task<Student?> VerifyCredentialsAsync(string email, string password)
    {
        var student = await dataAccess.GetByEmailAsync(NormalizeEmail(email));
        if (student is null || !student.IsActive)
            return null;

        var result = Hasher.VerifyHashedPassword(student, student.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : student;
    }

    // Called by the webhook controller after it has already verified the
    // card-token callback's HMAC signature - matches by email (the only
    // identifier Paymob's card-token callback actually carries that
    // correlates back to one of our accounts; Paymob's own numeric order_id
    // in that callback is THEIR order id, not our merchant_order_id/special_reference,
    // so it can't be used to look up the order the usual way). Silently
    // does nothing if the email doesn't match a real student - a webhook
    // has no legitimate reason to fail loudly over that.
    public async Task SaveCardTokenAsync(string email, string token, string? maskedPan, string? cardSubtype)
    {
        var student = await dataAccess.GetByEmailAsync(NormalizeEmail(email));
        if (student is null) return;

        await dataAccess.SaveCardTokenAsync(student.StudentId, token, maskedPan, cardSubtype);
    }

    public Task RemoveSavedCardAsync(int studentId) =>
        dataAccess.RemoveSavedCardAsync(studentId);

    // Trim + lowercase before every lookup/storage, rather than relying on the
    // database's collation to happen to be case-insensitive (environment-
    // dependent) and never handling whitespace at all - "Test@Example.com " and
    // "test@example.com" must always resolve to the same account.
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
