using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using POS_MB.Business.Email;
using POS_MB.DataAccess;
using POS_MB.DataAccess.Models;

namespace POS_MB.Business;

public partial class clsStudentBusiness(clsStudentDataAccess dataAccess, clsRefreshTokenBusiness refreshTokenBusiness, IEmailSender emailSender)
{
    private static readonly PasswordHasher<Student> Hasher = new();

    // 15 minutes - long enough for a student to actually check their email
    // and come back, short enough to keep the brute-force window tight (the
    // "login" rate-limit policy already caps guesses at 5/minute per IP, so
    // this bounds it to a few dozen attempts total against 1,000,000
    // possible codes).
    private static readonly TimeSpan PasswordResetCodeLifetime = TimeSpan.FromMinutes(15);

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

    // Deliberately silent (never throws, never reveals whether the email
    // matched an account) - a caller who doesn't have an account shouldn't
    // learn that from this endpoint's behavior, same reasoning as
    // VerifyCredentialsAsync returning the same "failed" result for both
    // an unknown email and a wrong password.
    public async Task RequestPasswordResetAsync(string email)
    {
        var student = await dataAccess.GetByEmailAsync(NormalizeEmail(email));
        if (student is null || !student.IsActive) return;

        var code = GenerateResetCode();
        await dataAccess.SetPasswordResetCodeAsync(student.StudentId, HashResetCode(code), DateTime.UtcNow.Add(PasswordResetCodeLifetime));

        await emailSender.SendAsync(student.Email, "Your POS-MB password reset code",
            $"Your password reset code is: {code}\n\nThis code expires in 15 minutes. If you didn't request this, you can safely ignore this email.");
    }

    /// <summary>
    /// Completes a forgot-password reset. Throws a single generic message for every
    /// failure reason (unknown email, wrong code, expired code) - same reasoning as
    /// VerifyCredentialsAsync, an attacker guessing codes shouldn't be able to tell
    /// "wrong code" apart from "no such account" from the response alone.
    /// </summary>
    public async Task ResetPasswordAsync(string email, string code, string newPassword)
    {
        var student = await dataAccess.GetByEmailAsync(NormalizeEmail(email));
        if (student is null || !student.IsActive
            || student.PasswordResetCodeHash is null || student.PasswordResetCodeExpiresAt is null
            || student.PasswordResetCodeExpiresAt < DateTime.UtcNow
            || !FixedTimeEquals(HashResetCode(code), student.PasswordResetCodeHash))
        {
            throw new ArgumentException("This reset code is invalid or has expired.", nameof(code));
        }

        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException("Password is required.", nameof(newPassword));

        await dataAccess.UpdatePasswordAsync(student.StudentId, Hasher.HashPassword(student, newPassword));

        // A successful reset is exactly the moment a session token issued
        // before it (stolen, or simply the reason the student is resetting
        // in the first place) must stop working - same convention as
        // clsUserBusiness's staff password change.
        await refreshTokenBusiness.RevokeAllForUserAsync(student.StudentId, AccountType.Student);
    }

    /// <summary>
    /// Self-service change, for a student who's already logged in and knows their
    /// current password - as opposed to ResetPasswordAsync, which is for a student
    /// locked out and proving identity via an emailed code instead.
    /// </summary>
    public async Task ChangePasswordAsync(int studentId, string currentPassword, string newPassword)
    {
        var student = await dataAccess.GetByIdAsync(studentId)
            ?? throw new ArgumentException("This account could not be found.", nameof(studentId));

        var result = Hasher.VerifyHashedPassword(student, student.PasswordHash, currentPassword);
        if (result == PasswordVerificationResult.Failed)
            throw new ArgumentException("Current password is incorrect.", nameof(currentPassword));

        if (string.IsNullOrWhiteSpace(newPassword))
            throw new ArgumentException("Password is required.", nameof(newPassword));

        await dataAccess.UpdatePasswordAsync(studentId, Hasher.HashPassword(student, newPassword));
        await refreshTokenBusiness.RevokeAllForUserAsync(studentId, AccountType.Student);
    }

    // 6 digits, cryptographically random (not Random.Shared) - this code is
    // itself a temporary credential, not just a display value.
    private static string GenerateResetCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    // SHA-256, not the PasswordHasher used for real passwords - a 6-digit
    // code is already low-entropy and short-lived (15 minutes) by design,
    // so PBKDF2's deliberate slowness buys nothing here, only latency.
    private static string HashResetCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    // Constant-time comparison - a plain == on the hash strings would leak
    // how many leading characters matched via response timing, undermining
    // the whole point of hashing the code in the first place.
    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    // Trim + lowercase before every lookup/storage, rather than relying on the
    // database's collation to happen to be case-insensitive (environment-
    // dependent) and never handling whitespace at all - "Test@Example.com " and
    // "test@example.com" must always resolve to the same account.
    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
