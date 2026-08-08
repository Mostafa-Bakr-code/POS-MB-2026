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
        if (string.IsNullOrWhiteSpace(email) || !EmailPattern().IsMatch(email))
            throw new ArgumentException("A valid email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password is required.", nameof(password));

        if (await dataAccess.GetByEmailAsync(email) is not null)
            throw new ArgumentException("An account with this email already exists.", nameof(email));

        var passwordHash = Hasher.HashPassword(new Student(), password);

        return await dataAccess.AddAsync(email, passwordHash);
    }

    /// <summary>
    /// Verifies an email/password pair. Returns the student on success, null on any
    /// failure (unknown email or wrong password look identical to the caller) -
    /// same reasoning as clsUserBusiness.VerifyCredentialsAsync.
    /// </summary>
    public async Task<Student?> VerifyCredentialsAsync(string email, string password)
    {
        var student = await dataAccess.GetByEmailAsync(email);
        if (student is null || !student.IsActive)
            return null;

        var result = Hasher.VerifyHashedPassword(student, student.PasswordHash, password);
        return result == PasswordVerificationResult.Failed ? null : student;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();
}
