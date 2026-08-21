using Dapper;
using POS_MB.DataAccess.Models;

namespace POS_MB.DataAccess;

public class clsStudentDataAccess(ISqlConnectionFactory connectionFactory)
{
    // Password column is aliased to PasswordHash so the model/every call site
    // is honest about the fact that it's never plaintext - same convention as
    // clsUserDataAccess.
    private const string SelectColumns =
        "StudentId, Email, Password AS PasswordHash, SavedCardToken, SavedCardMaskedPan, SavedCardSubtype, " +
        "PasswordResetCodeHash, PasswordResetCodeExpiresAt, IsActive, CreatedAt, UpdatedAt";

    public async Task<Student?> GetByIdAsync(int id)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = $"SELECT {SelectColumns} FROM Students WHERE StudentId = @Id";

        return await connection.QuerySingleOrDefaultAsync<Student>(query, new { Id = id });
    }

    public async Task<Student?> GetByEmailAsync(string email)
    {
        using var connection = connectionFactory.CreateConnection();

        var query = $"SELECT {SelectColumns} FROM Students WHERE Email = @Email";

        return await connection.QuerySingleOrDefaultAsync<Student>(query, new { Email = email });
    }

    public async Task<int> AddAsync(string email, string passwordHash)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            INSERT INTO Students (Email, Password)
            OUTPUT INSERTED.StudentId
            VALUES (@Email, @PasswordHash);";

        return await connection.ExecuteScalarAsync<int>(
            query, new { Email = email, PasswordHash = passwordHash });
    }

    // Overwrites any previously saved card - only one at a time is kept, see
    // the Student model.
    public async Task SaveCardTokenAsync(int studentId, string token, string? maskedPan, string? cardSubtype)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Students
            SET SavedCardToken = @Token, SavedCardMaskedPan = @MaskedPan, SavedCardSubtype = @CardSubtype, UpdatedAt = SYSUTCDATETIME()
            WHERE StudentId = @StudentId";

        await connection.ExecuteAsync(query, new { StudentId = studentId, Token = token, MaskedPan = maskedPan, CardSubtype = cardSubtype });
    }

    public async Task RemoveSavedCardAsync(int studentId)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Students
            SET SavedCardToken = NULL, SavedCardMaskedPan = NULL, SavedCardSubtype = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE StudentId = @StudentId";

        await connection.ExecuteAsync(query, new { StudentId = studentId });
    }

    // Overwrites any previous code - a student requesting a new one makes
    // any earlier, still-unexpired code invalid, since only the latest
    // request is ever something the student could actually be holding.
    public async Task SetPasswordResetCodeAsync(int studentId, string codeHash, DateTime expiresAtUtc)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Students
            SET PasswordResetCodeHash = @CodeHash, PasswordResetCodeExpiresAt = @ExpiresAtUtc, UpdatedAt = SYSUTCDATETIME()
            WHERE StudentId = @StudentId";

        await connection.ExecuteAsync(query, new { StudentId = studentId, CodeHash = codeHash, ExpiresAtUtc = expiresAtUtc });
    }

    // Used by both the forgot-password reset and the logged-in change-
    // password flow - always clears any pending reset code too, whether or
    // not one was actually in progress (a harmless no-op if it wasn't), so
    // a used-or-abandoned code can never be replayed after the password it
    // was issued for has already changed some other way.
    public async Task UpdatePasswordAsync(int studentId, string passwordHash)
    {
        using var connection = connectionFactory.CreateConnection();

        const string query = @"
            UPDATE Students
            SET Password = @PasswordHash, PasswordResetCodeHash = NULL, PasswordResetCodeExpiresAt = NULL, UpdatedAt = SYSUTCDATETIME()
            WHERE StudentId = @StudentId";

        await connection.ExecuteAsync(query, new { StudentId = studentId, PasswordHash = passwordHash });
    }
}
